# PCL Wrapper Roadmap — Extending Astronomy.PCL beyond the first XISF surface

**Charter / sub-roadmap.** Forward scope for the **PCL managed wrapper specifically** — *what additional PCL surface (`Astronomy.PCL` / `Astronomy.PCL.Native`) to wrap next*, the proposed API shape, and the open design questions that blocked landing code. A focused companion to the top-level `ROADMAP.md` (whole-library direction) and to `archive/PCL-InterOp.md` (the archived architectural decision record).

*Discussion-stage document, captured 2026-04-28. Pick up cold from this file when ready to resume.*

This is a companion to `archive/PCL-InterOp.md` (the archived architectural decision doc) and `CLAUDE.md` (operating notes). InterOp settled the macro-level question — how PCL is wrapped for managed consumers (Option 3 / Hybrid). This file scopes **what additional PCL surface to wrap next**, captures the design pivot mid-discussion, and lists the open decisions that blocked landing code.

## Status today

> **Snapshot as captured 2026-04-28 — not current truth.** Live wrapper state (surface, TFM, toolset)
> is in `ARCHITECTURE.md` § *Astronomy.PCL / Astronomy.PCL.Native*; the TFM below has since narrowed to
> `net10.0-windows`. **Before resuming this plan, see the premise check in `ROADMAP.md`
> § *Open: parked PCL wrapper-extension plan*.**

`Astronomy.PCL` (managed wrapper, `net10.0` x64 — was net8.0 initially; bumped 2026-05-04) currently exposes:

```csharp
public sealed class XisfFile : IDisposable
{
    public static XisfFile Open(string path);
    public string FilePath { get; }
    public int ImageCount { get; }
    public XisfImageInfo SelectImage(int index);
    public float[] ReadImageF32();
    public void ReadImageF32(float[] destination);
    public void Dispose();
}
public readonly struct XisfImageInfo { /* Width / Height / ChannelCount / BitsPerSample / IsFloatingPoint / ColorSpace / SampleCount */ }
public enum XisfColorSpace { Unknown=-1, Gray=0, Rgb=1, CieXYZ=2, CieLab=3, CieLch=4, Hsv=5, Hsi=6 }
public sealed class XisfException : Exception { public int StatusCode { get; } }
```

C ABI surface (in `Astronomy.PCL.Native/include/Astronomy/PCL/XisfCApi.h`):

- `AstronomyXisf_Open / Close / NumberOfImages / SelectImage / GetImageInfo / ReadImageF32 / GetLastErrorMessage`
- `AstronomyXisf_Ping` (smoke export, kept indefinitely as the first-line probe of the P/Invoke pipe).

Metadata reads (FITS keywords, properties, header XML), partial-pixel reads, and any write capability are **unimplemented**.

## Target surface — thirteen functions, in the user's words

| User-listed function | xisf.cpp counterpart | Underlying PCL primitive | Status |
|---|---|---|---|
| `ReadXisf(file)` | — | `XISFReader::ReadImage(FImage&)` | **Done** as `ReadImageF32`. |
| `ExtractXisfHeader(file)` | `ExtractXISFHeaders` (xisf.cpp:239) | `XISFReader::ExtractHeader(path)` (static) | New. |
| `EnumerateImage(file)` | `EnumerateImages` (xisf.cpp:260) | `XISFReader::ImageInfo()` + `ImageOptions()` + `ImageId()` per image | New. |
| `EnumerateProperties(file)` | `EnumerateProperties` (xisf.cpp:303) | `XISFReader::ImagePropertyDescriptions()` | New. |
| `ReadProperty(file, propertyId)` | `ReadProperty` (xisf.cpp:364) | `XISFReader::ReadProperty(id)` → `Variant` | New. |
| `ReadImageProperty(file, idx, propertyId)` | `ReadImageProperty` (xisf.cpp:404) | `XISFReader::ReadImageProperty(id)` → `Variant` | New. |
| `ReadFitsHeaderKeywords(file, idx)` | `ReadFITSHeaderKeywords` (xisf.cpp:458) | `XISFReader::ReadFITSKeywords()` → `FITSKeywordArray` | New. |
| `ReadPixelSamples(file, idx, firstRow, rowCount, channel)` | `ReadPixelSamples` (xisf.cpp:537) | `XISFReader::ReadSamples(samples, firstRow, rowCount, channel)` | New (subset of full image read). |
| `WriteXisf(file, fileName)` | — | `XISFWriter::Create` + `WriteImage` + `Close` | New. Read clone → write to fresh path. |
| `WriteProperty(file, propertyId)` | — | `XISFWriter::WriteProperty(id, Variant)` | New. **Note:** user signature is missing the *value* parameter. |
| `WriteFitsHeaderKeywords(file, idx)` | — | `XISFWriter::WriteFITSKeywords(FITSKeywordArray)` | New. **Note:** user signature is missing the *keywords* parameter. |
| `WriteImageProperty(file, idx, propertyId)` | — | `XISFWriter::WriteImageProperty(id, Variant)` | New. **Note:** user signature is missing the *value* parameter. |
| `WritePixelSamples(file, idx, firstRow, rowCount, channel)` | — | `XISFWriter::WriteSamples(...)` | New. **Note:** user signature is missing the *sample buffer* parameter. |

xisf.cpp is the canonical pattern source for the read side — eight of nine read functions trace directly to it. The write-side functions have no xisf.cpp counterparts; they would build directly on `pcl::XISFWriter`.

## Hard constraints surfaced from PCL

Two PCL realities shape the API:

1. **`pcl::XISFWriter` does not modify files in place.** Its lifecycle is `Create(path, count)` → per-image `WriteImage` / `WriteFITSKeywords` / `WriteImageProperty` → unit-level `WriteProperty` → `Close()`. To "edit" an existing XISF, the caller (or our wrapper) reads source, mutates in memory, writes a fresh file, and optionally renames atomically over the original. There is no `WriteFitsKeywords(file, idx, keywords)` primitive that touches an existing file. (Declared at `Library\PCL\include\pcl\XISF.h:1326`.)

2. **`pcl::Variant` is a tagged union with a `ToString()` flattener.** Property values can be Int8/16/32/64, UInt*, Float32/64, String, TimePoint, Vector, Matrix, Float64Array, etc. (`Library\PCL\include\pcl\Variant.h:331`, `ToString` at line 1785). For v1 marshaling, returning UTF-16 strings via `Variant::ToString()` covers all types at the cost of type fidelity. Typed accessors (`ReadPropertyAsInt32`, `ReadPropertyAsFloat64Array`, etc.) are a v2 expansion.

A third reality, less constraining but worth noting: pixel I/O is the only operation in PCL that is *expensive*. Metadata reads cost milliseconds; a single `ReadImage` on a 5496×3672×1 fixture costs hundreds of ms and 80 MB of RAM. The wrapper's existing convention is "pixel I/O is explicit" (calling `Open` / `SelectImage` / `GetImageInfo` never reads pixels). Preserve this on the new surfaces — `ReadPixelSamples` / `WriteImage` / `WritePixelSamples` only fire when called.

## Proposed API pivot — single `XisfFile`, mode-switched

Per user direction during the discussion (2026-04-28): do not introduce a parallel `XisfWriter` managed type. Extend the existing `XisfFile : IDisposable` so a single class handles both reading existing files and creating new ones. Method signatures of the new methods mirror the read-side conventions already in place (`Open` factory, instance properties, instance methods, exceptions on failure).

Sketch:

```csharp
public sealed class XisfFile : IDisposable
{
    // Read-mode factory — today's behavior, unchanged.
    public static XisfFile Open(string path);

    // Write-mode factory — new. Mirrors XISFWriter::Create.
    public static XisfFile Create(string path, int imageCount);

    // Mode-aware properties:
    public string FilePath { get; }
    public XisfFileMode Mode { get; }    // Read | Write

    // Read-mode methods (Open only — throw InvalidOperationException on Write):
    public int ImageCount { get; }
    public XisfImageInfo SelectImage(int index);
    public float[] ReadImageF32();                                   // existing
    public void ReadImageF32(float[] destination);                   // existing
    public float[] ReadPixelSamples(int firstRow, int rowCount, int channel);
    public string ExtractHeader();                                   // XML
    public IReadOnlyList<XisfPropertyDescription> EnumerateProperties();
    public IReadOnlyList<XisfPropertyDescription> EnumerateImageProperties();
    public string ReadProperty(string id);                           // Variant.ToString() v1
    public string ReadImageProperty(string id);
    public IReadOnlyList<XisfFitsKeyword> ReadFitsKeywords();

    // Write-mode methods (Create only — throw InvalidOperationException on Read):
    public void WriteImage(float[] samples, XisfImageInfo info);
    public void WritePixelSamples(int firstRow, int rowCount, int channel, float[] samples);
    public void WriteFitsKeywords(IReadOnlyList<XisfFitsKeyword> keywords);
    public void WriteProperty(string id, string value);              // Variant from string v1
    public void WriteImageProperty(string id, string value);

    public void Dispose();    // mode-aware cleanup
}

public enum XisfFileMode { Read, Write }
public readonly struct XisfPropertyDescription { public string Id { get; } public string TypeId { get; } }
public readonly struct XisfFitsKeyword { public string Name { get; } public string Value { get; } public string Comment { get; } }
```

Native side: the C ABI handle gains a discriminator field (`HandleType { Reader, Writer }`). `AstronomyXisf_Open` produces a Reader handle, a new `AstronomyXisf_Create(path, imageCount, outHandle)` produces a Writer handle. All existing exports check the tag at the top of the function body; mismatched mode returns a new `AstronomyXisfStatus_WrongMode` (code TBD). C# `XisfFile` enforces the same constraint *before* the P/Invoke for clarity, with the native check as defense in depth.

To "modify an existing file" (e.g. add FITS keywords): caller orchestrates the round-trip — `Open(src)` → read everything they want to preserve → `Create(dst)` → write everything plus the additions → `Close`. **No higher-level helper in v1.** A `XisfFile.CopyTo(dst, mutator)` helper that internally does the read-modify-write is on the table for v2 if the round-trip pattern recurs.

## Open design questions

These need to settle before code lands:

1. **API model.** Single mode-switched `XisfFile` (above sketch) is the current recommendation. Alternatives:
   - Separate `XisfFile` (read-only, today's type) + `XisfWriter` (new write-only type). PCL's own model. Type-safe by construction; no runtime mode checks.
   - Single `XisfFile` + a `XisfFile.CopyTo(srcPath, dstPath, mutator)` helper for "add FITS keywords" use cases. Higher-level convenience on top of the primitives.

2. **Variant marshaling strategy.**
   - v1 = string-only via `Variant.ToString()` and `Variant(const String&)`. Smallest ABI; loses type fidelity for round-trips of non-string properties.
   - Typed from day one. Adds `ReadPropertyAs{Int32,Int64,Float32,Float64,String,Float64Array,TimePoint,…}` exports plus `WriteProperty{Int32,…}` symmetric set. Bigger ABI but allows precise round-trip.

3. **First-PR scope.** Recommended minimum:
   - **Phase 0 + A** — scaffolding plus six metadata-read methods (ExtractHeader, EnumerateProperties, EnumerateImageProperties, ReadFitsKeywords, ReadProperty, ReadImageProperty), Variants string-only. Six new exports, no write infrastructure yet.
   - Alternatives: also include B (ReadPixelSamples) for partial-pixel reads; or skip A and start with C (Create + WriteImage round-trip) if "produce XISF files from C#" is the more pressing need; or land everything in one PR.

## Recommended staging (subject to question 3)

| Phase | Scope | New exports |
|---|---|---|
| 0 | Mode-tag plumbing on native handle, mode-aware Close, C# `Mode` property | 0 (scaffolding) |
| A | Metadata read (six methods) | `ExtractHeader`, `EnumerateProperties`, `EnumerateImageProperties`, `ReadFitsKeywords`, `ReadProperty`, `ReadImageProperty` |
| B | Pixel slice read | `ReadPixelSamples` |
| C | Write infrastructure + first surface | `Create`, `WriteImage` |
| D | Write metadata | `WriteFitsKeywords`, `WriteProperty`, `WriteImageProperty` |
| E | Pixel slice write | `WritePixelSamples` |

Each phase ships its own commit on `dev`. Tests against `Library\PCL\src\utils\xisf\TestData\test.xisf` (read phases) and tmp-directory round-trip (write phases — open existing → read pixels + info → Create tmp file → write → re-open → assert equivalence within float tolerance, then teardown).

## References

- `Library\archive\PCL-InterOp.md` — architectural decision doc (Option 3 / Hybrid, P/Invoke not C++/CLI). Archived.
- `Library\CLAUDE.md` — operating notes (Solution layout, build/test/benchmark commands, PCL local build, PCL interop). Already documents the wrap-on-demand strategy and the pattern for adding a new export (`extern "C"` declaration in `XisfCApi.h` + `Astronomy.PCL.Native.def` + implementation in `XisfCApi.cpp` + `[DllImport]` in `NativeMethods.cs` + public surface in `XisfFile.cs` + test in `Astronomy.Core.Tests/Tests/PCL/`).
- `Library\PCL\src\utils\xisf\xisf.cpp` — read-side patterns. Lines cited in the table above.
- `Library\PCL\include\pcl\XISF.h` — `XISFReader` at 937, `XISFLogHandler` at 833, `XISFWriter` at 1326.
- `Library\PCL\include\pcl\Variant.h` — `Variant` class at 331, `ToString()` at 1785.
- `Library\Astronomy.PCL\XisfFile.cs` — current public surface to extend.
- `Library\Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h` — current C ABI surface to extend.
