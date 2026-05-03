# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout

`Astronomy.sln` is **x64-only** (Debug/Release × x64; no AnyCPU/x86) and holds four buildable projects, plus a `PCL` Solution Folder containing seven view-only PCL projects (`PCL.vcxproj` + the six 3rd-party `.lib`s — `cminpack`, `lcms`, `lz4`, `RFC6234`, `zlib`, `zstd`) sourced from `Library\PCL\`. The PCL projects have `ActiveCfg` set but `Build.0` omitted: full source visibility and IntelliSense / F12 in the IDE, but `Build Solution` and `msbuild Astronomy.sln` skip them. PCL rebuilds happen manually via `Library\PCL\src\pcl\windows\vc18\PCL.sln`.

The four buildable projects:

- **`Astronomy.Core`** — `netstandard2.0`, `LangVersion 7.3`. The library proper. Pure managed C# with **no NuGet dependencies** (post-`2249834` CoordinateSharp removal — every helper is now Meeus-backed). XML doc generation is on (`GenerateDocumentationFile=true`), so public surface is expected to carry `///` docs. Buildable independently with `dotnet build` if a contributor wants only the managed primitives.
- **`Astronomy.Core.Tests`** — `net10.0` x64, `OutputType=Exe`. Hosts both xUnit tests (`Tests/`) and BenchmarkDotNet benchmarks (`Benchmarks/`) in a single assembly. `GenerateProgramFile=false` because `Program.cs` defines its own `Main` that delegates to `BenchmarkSwitcher.FromAssembly(...)`. References both `Astronomy.Core` and `Astronomy.PCL`.
- **`Astronomy.PCL.Native`** — vcxproj, x64-only C++ DLL. Statically links the vendored PixInsight Class Library (`Library\PCL\lib\x64\$(Configuration)\*-pxi.lib`). Public surface is the `extern "C"` C ABI in `include\Astronomy\PCL\XisfCApi.h`. Mirrors PCL's build flavor (`/MD`, `stdcpp17`, `__PCL_WINDOWS`) using the same MSVC toolset PCL itself uses (`v145` as of VS2026 — both wrapper and PCL bumped together); compiled with `AdvancedVectorExtensions2` (AVX2) for portability — PCL's own AVX-512 paths remain runtime-gated inside the static lib.
- **`Astronomy.PCL`** — `net8.0` x64. Managed P/Invoke wrapper. Public surface: `XisfFile : IDisposable` (`Open` / `SelectImage` / `ReadImageF32`), `XisfImageInfo`, `XisfColorSpace`, `XisfException`. Internal `NativeMethods` in `Interop/` holds the `[DllImport]` declarations. `<InternalsVisibleTo Include="Astronomy.Core.Tests" />` lets the smoke test bypass the wrapper. **net8.0 not netstandard2.0**: VS2026's `MSBuild.exe` has a defect resolving `System.Runtime.InteropServices.DllImportAttribute` for `netstandard2.0` projects — `net8.0` works under both `MSBuild.exe` and `dotnet build`. `Astronomy.PCL` has no `net481` consumer (TargetPlanner does charting, not file I/O), so the trade-off is free.

`Library\PCL\` is the vendored PixInsight Class Library (~10 GB of source + prebuilt `.lib` outputs). It's gitignored — re-extract from `PCL\PCL-master.zip` (snapshot pinned 2025-02-22 per `PCL InterOp.md`) on a fresh clone.

## Build / test / benchmark

The mixed C++/C# solution requires `MSBuild.exe` (from VS2022+) for full builds — `dotnet build Astronomy.sln` cannot drive the C++ vcxproj. Locate it via `vswhere.exe` or run from a Developer Command Prompt.

```bash
# Full mixed build (native + managed) — REQUIRED to produce Astronomy.PCL.Native.dll
msbuild Astronomy.sln /p:Configuration=Debug /p:Platform=x64 /m

# Run xUnit tests (all). The native DLL must already be built; --no-build skips a redundant rebuild.
dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj -c Debug -p:Platform=x64 --no-build

# Run a single test by fully-qualified name or substring filter
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AltitudeCurveTests"
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "TargetDefault_IsM31"

# Pure-managed Astronomy.Core build (no SLN, no C++ tooling needed)
dotnet build Astronomy.Core/Astronomy.Core.csproj

# Run BenchmarkDotNet — Release is mandatory (Debug numbers are misleading)
msbuild Astronomy.sln /p:Configuration=Release /p:Platform=x64 /m
dotnet run -c Release --project Astronomy.Core.Tests -p:Platform=x64 -- --filter *
dotnet run -c Release --project Astronomy.Core.Tests -p:Platform=x64 -- --list tree
```

`BDN0001` (BenchmarkDotNet's "build in Release" warning) is suppressed in the csproj so xUnit can run cleanly under Debug; the runtime check inside BenchmarkSwitcher still enforces Release for benchmark runs.

**Trap to avoid:** `dotnet build Astronomy.sln` silently produces a managed-only build (skipping the vcxproj) and any test that touches `Astronomy.PCL` then throws `DllNotFoundException`. Always use `msbuild` for the SLN.

## Architectural conventions

These are baked into the public API and must be respected when adding code:

- **Hemisphere convention.** `Latitude`, `Longitude`, and `Declination` are stored as **non-negative magnitudes**, with direction in companion bool flags (`Location.North` / `Location.West` / `Target.North`). Constructors normalize: a negative magnitude is flipped positive and the corresponding flag is inverted (sign takes precedence over the supplied flag). Internal math reconstructs signed values just before feeding geometry.
- **RA is decimal hours in `[0, 24)`**, not degrees. Declination is decimal degrees.
- **Altitude is unrefracted** — degrees above the mathematical horizon, never adjusted for atmospheric refraction. Don't introduce a refraction term without coordinating across `AltAz`, `TargetGeometry`, `RiseSet`, etc.
- **Azimuth** is degrees from North, clockwise, in `[0, 360)`.
- **DateTime kinds.** `Location.DateTime` is caller-owned. `Local`/`Unspecified` are treated as local; `Utc` is no-op'd. `NightWindow` outputs are `Kind=Utc`. Helpers like `AltAzCalculator.Of` call `ToUniversalTime()` internally.
- **Immutability + `With(...)`.** `Location` and `Target` are immutable; mutations produce new instances via a `With` method that takes optional parameters.
- **D/M/S accessors are computed on read**, never stored. Don't add stored DMS fields — they would drift.

## Thread safety

`Astronomy.Core` is **thread-safe by construction** since the CoordinateSharp removal in commit `2249834`. Verified: zero mutable static fields, zero static caches / dictionaries / `Lazy<T>` / singletons; all public APIs take parameters with no hidden state. All inputs are immutable POCOs (`Target`, `Location`, `NightWindow`, `MoonAvoidanceProfile`); all returns are immutable POCOs, small structs (`AltAz`, `RiseSet`), or `IReadOnlyList<T>`. **Consumers may call any helper from any thread without external synchronization.**

Two caveats limit that promise:

1. **Caller-supplied delegates are the consumer's responsibility.** Methods that take `Func<...>` parameters — most notably `IntegratedQuality.OverSession(..., Func<double, double> altitudeQuality)` and `IntegratedQuality.HalvesAroundMidpoint(...)`, plus any future delegate-taking helper — invoke the delegate from whatever thread called the method. If a consumer passes a closure that mutates captured state (e.g. `int count = 0; alt => { count++; return ...; }`), thread safety of that closure is on the consumer. Pure / referentially transparent functions are always safe.

2. **`Astronomy.PCL` is NOT part of this contract.** PCL is a separate assembly (`Astronomy.PCL.csproj` + `Astronomy.PCL.Native.vcxproj`) wrapping the vendored PixInsight Class Library. `Astronomy.Core` never references it. The underlying PCL C++ library historically isn't safe to call across threads — assume single-threaded access until PCL's own concurrency story is documented per-API.

`NightCache` is a per-`Location` amortization helper: build once per Graph click, hand to multiple targets so each target's year work doesn't re-derive the same 365-day `NightWindow` series. The instance is immutable after construction; concurrent readers are safe. Several caches can be built in parallel for different locations.

## Code organization (high-level)

- `AltAz.cs` / `TargetGeometry.cs` — topocentric coordinate primitives (the core "where is this target right now" math).
- `Time/` — `JulianDate`, `SiderealTime` (GMST/LST).
- `Locations/`, `Targets/` — the immutable observer + target value types described above.
- `Horizons/` — `IHorizonProfile` and three implementations (`Scalar`, `Polyline`, `ObstructionTable`) for flat-vs-azimuth-varying horizon math.
- `Night/` — twilight calculation, `NightWindow` (a single night's astronomical/nautical/civil dusk/dawn), `NightCache` (year-of-nights for one location).
- `Session/` — higher-level analysis built on the primitives: `AltitudeCurve` (uniform-grid sampling via linear LST advance — ~2.6x faster than per-sample `AltAzCalculator.Of`), `RiseSet`, `TransitTime`, `VisibilityWindows`, `CoarseVisibility`, `IntegratedQuality`, `BestSession`, `TargetOrdering`.
- `Moon/` — `MoonSeparation`.

## PCL local build

`Library\PCL\` is the vendored Pleiades PixInsight Class Library, locally pruned to **Windows-only** — the macOS and Linux build trees were stripped. The canonical pinned snapshot is `Library\PCL\PCL-master.zip` (2025-02-22 per `PCL InterOp.md`). Re-extract on a fresh clone, or to discard local edits.

**Toolset.** All PCL projects (`PCL.vcxproj` + the six 3rd-party libs + `xisf.vcxproj`) are at `<PlatformToolset>v145</PlatformToolset>`, matching `Astronomy.PCL.Native`. The directory naming `vc17` under `src\3rdparty\*\windows\vc17\` and `src\modules\*\windows\vc17\` is historical PCL convention and was deliberately not renamed; only the main PCL solution moved from `src\pcl\windows\vc17\` → `src\pcl\windows\vc18\` to signal the VS2026 build flavor.

**Main solution.** `Library\PCL\src\pcl\windows\vc18\PCL.sln`. Builds PCL plus the six 3rd-party static libs into `Library\PCL\lib\x64\{Debug,Release}\*-pxi.lib` and the xisf utility into `Library\PCL\bin\x64\{Debug,Release}\xisf.exe`. Astronomy.PCL.Native consumes the seven `.lib` outputs from `lib\x64\$(Configuration)\`.

**Required environment variables** (consumed by PCL.vcxproj, the 3rd-party libs, and xisf.vcxproj — set as system env vars and reboot before opening the SLN; otherwise the C++ build fails to find headers and libraries):

- `PCLINCDIR` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\include`
- `PCLSRCDIR` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\src`
- `PCLLIBDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\lib\x64`
- `PCLBINDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\bin\x64`

**xisf utility.** PCL's CLI test app at `Library\PCL\src\utils\xisf\`. Statically links the same seven `.libs` Astronomy.PCL.Native does (`PCL-pxi`, `lz4-pxi`, `zlib-pxi`, `zstd-pxi`, `lcms-pxi`, `cminpack-pxi`, `RFC6234-pxi`, plus `Userenv`). Built by PCL.sln (which lists xisf.vcxproj) or independently via `xisf.sln` in the same directory; both produce the same `xisf.exe`. **xisf.exe must be invoked from its solution directory** so its relative-path arguments resolve. `Test_GetXisfKeywords.bat` is a developer-convenience smoke that runs `xisf.exe --read-fits-keywords` against `TestData\test.xisf`. Both `xisf.vcxproj` and `xisf.sln` are x64-only — Win32/x86 was dropped because the v140_xp toolset isn't installed under VS2026.

**Re-snapshot caveat.** Re-extracting `PCL-master.zip` clobbers all local edits — the `vc18` directory, the v143→v145 toolset bumps across PCL + 3rd-party + xisf, the Win32/x86 strip on xisf, anything else added under `Library\PCL\`. Plan to re-apply or keep a patch.

**Upstream Pleiades docs.** `Library\PCL\README.md` (PCL overview), `CODING_STYLE.md` (Pleiades C++ conventions), `COPYING.md` + `LICENSE.txt` (PCLL license) are unmodified upstream content; left alone deliberately so re-snapshot doesn't churn them.

## PCL interop

The hybrid architecture from `PCL InterOp.md` is **implemented** for the first surface (XISF read). Two projects:

- `Astronomy.PCL.Native` (C++ DLL, statically links PCL `.lib`s from `Library\PCL\lib\x64\`).
- `Astronomy.PCL` (managed P/Invoke wrapper, `net8.0` x64).

C ABI is the source of truth: `Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h`. Every export is wrapped in `ASTRONOMY_PCL_TRY` / `ASTRONOMY_PCL_CATCH` (in `src\Exception.h`) — this catches `pcl::Exception` and `std::exception` and translates to status codes; the `catch (...)` branch picks up SEH (access violations etc.) due to `<ExceptionHandling>Async</ExceptionHandling>`. Last-error message is thread-local (`src\LastError.cpp`); consumers retrieve via `AstronomyXisf_GetLastErrorMessage` (two-call idiom: query length, alloc, fetch).

**Watch-out: don't use `XISFReader::ReadImage(FImage&)` for non-float source images.** PCL's auto-converting read path appears to need PixInsight platform services that aren't available in a host process — UInt16 or UInt32 sources surface as an unrecognized SEH exception ("Unknown C++ exception" at `catch (...)`). The wrapper dispatches on `ImageOptions.bitsPerSample` / `ieeefpSampleFormat`, reads in the file's native type (`UInt16Image`, `FImage`, `DImage`, …), then converts to float32 in our own loop. See `XisfCApi.cpp:AstronomyXisf_ReadImageF32`.

Adding new PCL surfaces: bind on demand. Add the `extern "C"` export to `XisfCApi.h` and `Astronomy.PCL.Native.def`, implement in `XisfCApi.cpp` wrapped in the macro, mirror the `[DllImport]` in `Astronomy.PCL/Interop/NativeMethods.cs`, expose the C# surface in `Astronomy.PCL`. Test in `Astronomy.Core.Tests/Tests/PCL/`.
