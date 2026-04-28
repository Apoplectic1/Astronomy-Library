# PCL InterOp — Unifying Astronomy across C# and C++ on Windows

*Discussion summary, 2026-04-25.*

## Goal

The `Astronomy` library at `E:\Projects\VisualStudio\Astronomy\Library\` should expose [PCL](https://pixinsight.com/developer/pcl/) (the PixInsight Class Library, C++), now vendored at `E:\Projects\VisualStudio\Astronomy\Library\PCL\`, to managed C# consumers, while continuing to be usable from C++ consumers without disrupting them. Windows-only is the immediate target; Linux portability is acknowledged as harder and explicitly deferred.

Consumer matrix:
- **C# apps:** TargetPlanner (`net481`), XisfManager / IS / ISS (`net10`).
- **C++ apps:** existing xisf utility in `PCL\src\utils\xisf\`, plus any future C++ tools.
- **Library targets:** `Astronomy.Core` is `netstandard2.0`; `Astronomy.PCL` is `net8.0` (it has no `net481` consumer — TargetPlanner doesn't use PCL, and the VS2026 msbuild has a defect with netstandard2.0 + `System.Runtime.InteropServices` reference resolution).

---

## Decision: Option 3 (Hybrid)

Three architectures were considered. Only the chosen one is described in detail below; the others are summarized for context.

| Option | Summary | Verdict |
|--------|---------|---------|
| **1. Parallel libraries** | `Astronomy` for C# + `Astronomy` for C++ as separate code bases sharing branding only. | Simplest, but shared algorithms get written twice. |
| **2. C++-first unified core** | Rewrite `Astronomy.Core`'s algorithms in C++; managed code becomes a thin P/Invoke shim. | Real single source of truth, but expensive: rewrites existing working code; needs deep interop comfort. |
| **3. Hybrid (chosen)** | Keep `Astronomy.Core` managed-only; wrap PCL once in a native DLL exposed to BOTH languages. | Preserves existing C# code, uses only the easy interop direction (C# → C++), supports both consumers. |

**Why Option 3 is right for this project:** the existing `Astronomy.Core` C# code keeps working, no rewrites are needed for current apps, and PCL becomes the shared C++ value (which it already is — the xisf C++ app already uses it directly). The only "new" work is binding PCL features to C# as the need arises.

---

## The Architecture

```
E:\Projects\VisualStudio\Astronomy\Library\        (the Astronomy library family)
├── Astronomy.sln                                  (x64-only; AnyCPU/x86 dropped)
├── Astronomy.Core\                                (C#, netstandard2.0)
├── Astronomy.Core.Tests\                          (xUnit + BenchmarkDotNet, net10.0 x64)
├── Astronomy.PCL.Native\                          (C++ DLL — vcxproj, x64)
│   ├── include\Astronomy\PCL\                     (public headers for C++ apps)
│   └── src\                                       (impl + extern "C" exports for C#)
├── Astronomy.PCL\                                 (C# managed P/Invoke wrapper, net8.0 x64)
└── PCL\                                           (Pleiades' source/lib — vendored, gitignored)
```

### Two assemblies, two consumers

There are TWO assemblies with "Astronomy.PCL" in the name. They serve different consumers and should not be confused:

- **`Astronomy.PCL.Native`** — C++ DLL. *This is what C++ apps consume.*
  - Exposes **C++ headers** (`#include <Astronomy/PCL/...>`) for C++ apps that want shared helpers.
  - Exposes **C ABI exports** (`extern "C"` functions) for the C# wrapper to P/Invoke.
- **`Astronomy.PCL`** — C# managed assembly. *This is what C# apps consume.*
  - Just C# classes that P/Invoke into `Astronomy.PCL.Native`.
  - C++ apps never reference this — it is not a C++-consumable thing.

### Why not put PCL inside `Astronomy.Core`?

`Astronomy.Core` is a `netstandard2.0` C# project. A netstandard2.0 csproj cannot embed C++ source or be a C++/CLI mixed-mode assembly. It can declare `[DllImport]`, but that does not help when PCL is heavily templated C++ — there is no flat C ABI to import directly. So PCL needs a separate native wrapper project.

---

## How each consumer reaches PCL

### C# apps

```csharp
using Astronomy.Core;          // existing managed astronomy math
using Astronomy.PCL;           // new: managed wrappers over PCL features

var altaz = AltAzCalculator.Of(target, location);   // pure C# (Astronomy.Core)
var img   = Astronomy.PCL.Xisf.Load(path);          // P/Invoke → native PCL
```

C# code never sees `extern "C"` or `DllImport` — that is hidden inside `Astronomy.PCL`.

### C++ apps

Two paths, freely combinable in the same `.cpp` file:

```cpp
#include <pcl/Image.h>                         // Path 1: direct PCL — full feature surface
#include <Astronomy/PCL/Xisf.h>                // Path 2: helpers from Astronomy.PCL.Native

pcl::Image<float> raw = ...;                   // direct PCL
auto file = Astronomy::PCL::LoadXisf(path);    // helper that internally uses PCL
```

The existing xisf C++ app needs **no changes** — it keeps using direct PCL exactly as it does today. The Astronomy helpers are additive: use them where they save effort, ignore them otherwise.

---

## C# ↔ C++ interop in 90 seconds

### Three mechanisms for C# → C++

| Mechanism | When to use |
|-----------|-------------|
| **P/Invoke (`[DllImport]`)** | Default. Standard, boring, correct for ~90% of cases. Works from any .NET target. |
| **C++/CLI** | One mixed-mode assembly that includes PCL headers AND exposes managed types. Trade: Windows-only, locks to a specific .NET target (`net48` or `net8.0-windows`), cannot be netstandard2.0. |
| **COM** | Heavy ceremony (IUnknown, GUIDs, type libraries). Do not pick for greenfield work. |

For this project, **P/Invoke is the right tool**. C++/CLI is tempting but only pays off when binding hundreds of fine-grained PCL surfaces with rich types.

### What crosses the C ABI cleanly

- **Crosses easily:** `int`, `double`, `float`, byte arrays, blittable structs.
- **Crosses with care:** strings (`CharSet` attribute), arrays of complex types, callbacks (delegates).
- **Does not cross:** C++ classes, templates, exceptions, STL types.

### C++ → C# is the hard direction

Mechanisms exist (C++/CLI reversed, CLR hosting API, NativeAOT + `UnmanagedCallersOnly`, out-of-process IPC) but all are heavier than C# → C++. **Industry-standard rule:** if shared algorithms are needed in both languages, write them in C++ and call them from C# — not the reverse.

We never need this direction in the chosen architecture: existing managed `Astronomy.Core` algorithms stay in C#; if a C++ app needs one, the pragmatic approach is to reimplement that specific algorithm in C++.

### Caveats that bite real projects

These turn into multi-day debugging sessions if not anticipated upfront:

- **Memory ownership.** Every buffer crossing the boundary needs a clear allocator/freer agreement. Standard pattern: native allocates → returns pointer + length → C# copies → C# calls a `Free` export.
- **Bitness.** Native DLL must match the .NET process bitness. `csproj Platform=x64` and `vcxproj Platform=x64` must agree. Most "DllNotFoundException" failures trace here.
- **Deployment.** Native DLL must sit next to the .NET executable at runtime, be on `PATH`, or live in a NuGet `runtimes\win-x64\native\` folder.
- **Debugging across the boundary.** In Visual Studio, enable **Native code debugging** on the .NET project to step from C# into C++. Off by default.
- **C++ exceptions.** Do not cross any boundary cleanly. Always catch in C++ and translate to error codes (or a thread-local last-error).
- **Threading, locales, `errno`.** Global per process — mixed-language code can collide.
- **Type marshaling cost.** Strings and complex structs are where engineering goes. Start with simple types; only marshal complexity when it earns its keep.

---

## Performance

| Path | Per-call overhead | Notes |
|------|-------------------|-------|
| C++ app → `Astronomy.PCL.Native` helpers | **Zero.** | Just a regular C++ call; compiler can inline through the helper. Same speed as direct PCL. |
| C# app → `Astronomy.PCL` → native via P/Invoke | **~20–50 ns** | Negligible at coarse grain (load a file, plate-solve an image). Bad at one-call-per-pixel. |

**Inner-loop rule:** never call P/Invoke per pixel. Pass a buffer in, return a buffer out, do the loop on the native side.

---

## When to write a new app in C++ at all

For this project's matrix, default new apps to C#. Choose C++ when:

1. **Performance** — inner loops where .NET overhead matters. The dominant reason in practice.
2. **PixInsight modules** — required to be C++; PixInsight loads native modules.
3. **Existing C++ code** — do not rewrite the xisf utility for no reason.
4. **PCL ergonomics** — when an app uses many PCL templates with rich types, wrapping each is more work than just writing C++.

---

## Tradeoffs to revisit later

These were touched on in discussion but do not need to be settled before the first wrapper exists:

- **In-process (DLL) vs. out-of-process (CLI tool).** For batch operations (load 100 XISF files), the existing xisf binary called via `Process.Start` is a viable MVP — it requires zero binding work. In-process via `Astronomy.PCL` is required only for fine-grained calls.
- **Wrap-on-demand vs. comprehensive coverage.** Wrap-on-demand is the right starting strategy: bind functions as you need them, grow organically. Comprehensive upfront is more work for less early benefit.
- **Static vs. dynamic linking of PCL.** Static is the default and simplest — PCL on Windows ships primarily as `.lib` static libs, and the `src-pcl-windows-vc17` build flavor is set up for it. Static linking gives you one fat `Astronomy.PCL.Native.dll` with PCL baked in.
- **Astronomy.Core long-term form.** Stay managed unless C++ apps genuinely need its algorithms. Most likely "stay managed forever" given C++ apps are mostly PCL-driven.
- **Naming / sub-namespacing.** Single `Astronomy.PCL` vs. multiple sub-namespaces (`Astronomy.PCL.Xisf`, `Astronomy.PCL.Astrometry`, etc.) — decide when the wrapper grows large enough to need it.
- **Versioning and packaging.** Local `ProjectReference` is fine for first iteration. NuGet packaging with `runtimes\win-x64\native\` layout becomes useful once the wrapper is consumed by code outside this solution.
- **Testing strategy.** Wrapper layer needs its own tests — round-trip values through the C ABI, exercise memory ownership patterns, verify error-code translation.

---

## Effort scaling: which PCL features to expose first?

| Surface | Approximate C wrapper effort |
|---------|------------------------------|
| XISF I/O only | 200–400 lines |
| + astrometry / plate solving | ~1000 lines |
| + image processing math (`Image<T>`, FFT, transforms) | 2000+ lines |

If "XISF for XisfManager" is the immediate need, the existing xisf binary called out-of-process may be the right MVP — zero binding work required. The wrapper investment pays off when fine-grained calls or rich shared types make process-spawn overhead unacceptable.

---

## Practical setup notes

- Mixed C++ / C# in one VS solution is fully supported; both new projects live alongside `Astronomy.Core`.
- The C++ project is a `.vcxproj` with per-config (Debug/Release × x64) outputs.
- Public C++ headers live under `Astronomy.PCL.Native\include\Astronomy\PCL\`; consumers add this as `<AdditionalIncludeDirectories>`.
- Static-link PCL via `<AdditionalLibraryDirectories>` pointing at `..\PCL\lib\x64\$(Configuration)\` (relative to the vcxproj).
- For the first iteration, NuGet packaging is unnecessary — local `ProjectReference` and DLL deployment work fine.
- `Astronomy.PCL.Native.dll` must be deployed next to any C# executable that uses `Astronomy.PCL`. Use `<Content Include="...">` with `CopyToOutputDirectory`, or a post-build copy step.

---

## License note

PCL is under the [PixInsight Class Library License](https://pixinsight.com/developer/pcl/) — permissive (BSD-style), redistribution allowed, attribution required. Bundle `LICENSE.txt` alongside any redistributed binaries.

PCL snapshot pinned at `PCL\PCL-master.zip` from 2025-02-22. Re-snapshot only when there is a specific reason to upgrade; PCL templates can introduce ABI churn that breaks the wrapper if not coordinated.

---

## Status

**First surface implemented: XISF read.** `Astronomy.PCL.Native` (vcxproj, statically links PCL `.lib`s from `Library\PCL\lib\x64\$(Configuration)\`) plus `Astronomy.PCL` (net8.0 P/Invoke wrapper) are in `Astronomy.sln`. Public C# surface: `XisfFile : IDisposable` with `Open` / `SelectImage` / `ReadImageF32`. Tests live in `Astronomy.Core.Tests/Tests/PCL/`. The C ABI surface is in `Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h` — extension is wrap-on-demand per the strategy in this doc.

### Two findings worth carrying forward

1. **Read in the file's native sample format, then convert in our own code.** PCL's auto-converting `XISFReader::ReadImage(FImage&)` for non-float source files (e.g. UInt16 → Float32) appears to need PixInsight platform services that aren't available in a host process — the call raises an SEH access violation that surfaces through `catch (...)` as "Unknown C++ exception". The wrapper dispatches on `ImageOptions.bitsPerSample` / `ieeefpSampleFormat` and reads as `UInt16Image` / `FImage` / etc. directly, then scales to float32 ourselves. See `XisfCApi.cpp:AstronomyXisf_ReadImageF32`.

2. **`Astronomy.PCL` targets `net8.0`, not `netstandard2.0`.** Visual Studio 2026 (build 18.x) `MSBuild.exe` has a defect resolving `System.Runtime.InteropServices.DllImportAttribute` for `netstandard2.0` projects — `dotnet build` resolves it correctly, but VS's `msbuild.exe` (which is what builds the SLN with the C++ vcxproj reference) does not. Since `Astronomy.PCL` has no `net481` consumer (TargetPlanner does charting, not file I/O), `net8.0` is a clean trade-off.
