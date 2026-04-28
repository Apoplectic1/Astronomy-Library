# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout

`Astronomy.sln` is **x64-only** (Debug/Release × x64; no AnyCPU/x86) and holds four projects:

- **`Astronomy.Core`** — `netstandard2.0`, `LangVersion 7.3`. The library proper. Pure managed C#; only NuGet dep is `CoordinateSharp 3.4.1.1`. XML doc generation is on (`GenerateDocumentationFile=true`), so public surface is expected to carry `///` docs. Buildable independently with `dotnet build` if a contributor wants only the managed primitives.
- **`Astronomy.Core.Tests`** — `net10.0` x64, `OutputType=Exe`. Hosts both xUnit tests (`Tests/`) and BenchmarkDotNet benchmarks (`Benchmarks/`) in a single assembly. `GenerateProgramFile=false` because `Program.cs` defines its own `Main` that delegates to `BenchmarkSwitcher.FromAssembly(...)`. References both `Astronomy.Core` and `Astronomy.PCL`.
- **`Astronomy.PCL.Native`** — vcxproj, x64-only C++ DLL. Statically links the vendored PixInsight Class Library (`Library\PCL\lib\x64\$(Configuration)\*-pxi.lib`). Public surface is the `extern "C"` C ABI in `include\Astronomy\PCL\XisfCApi.h`. Mirrors PCL's build flavor (`/MD`, `v143`, `stdcpp17`, `__PCL_WINDOWS`); compiled with `AdvancedVectorExtensions2` (AVX2) for portability — PCL's own AVX-512 paths remain runtime-gated inside the static lib.
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

## CoordinateSharp thread-safety gate

`CoordinateSharp 3.4.1.1` has internal state in `Celestial.CalculateCelestialTimes` that is not safe under concurrent calls — racing calls can produce results with null `AdditionalSolarTimes` entries that should be valid. **Every call into `CalculateCelestialTimes` (in this repo and downstream consumers) must go through `Astronomy.Core.CoordinateSharpGate.Calculate(...)`**, which serializes them on a single static lock. The lock is held only around the calculation itself; the returned `Celestial` is constructed with `EagerLoadType.Celestial`, so subsequent property reads don't need to be under the lock — but the returned instance is not itself thread-safe and must not be shared across threads.

`NightCache` exists to amortize this serialization for multi-target Graph workloads: build it once on a background task, then per-target year work is pure AltAz math that parallelizes freely.

## Code organization (high-level)

- `AltAz.cs` / `TargetGeometry.cs` — topocentric coordinate primitives (the core "where is this target right now" math).
- `Time/` — `JulianDate`, `SiderealTime` (GMST/LST).
- `Locations/`, `Targets/` — the immutable observer + target value types described above.
- `Horizons/` — `IHorizonProfile` and three implementations (`Scalar`, `Polyline`, `ObstructionTable`) for flat-vs-azimuth-varying horizon math.
- `Night/` — twilight calculation, `NightWindow` (a single night's astronomical/nautical/civil dusk/dawn), `NightCache` (year-of-nights for one location).
- `Session/` — higher-level analysis built on the primitives: `AltitudeCurve` (uniform-grid sampling via linear LST advance — ~2.6x faster than per-sample `AltAzCalculator.Of`), `RiseSet`, `TransitTime`, `VisibilityWindows`, `CoarseVisibility`, `IntegratedQuality`, `BestSession`, `TargetOrdering`.
- `Moon/` — `MoonSeparation`.

## PCL interop

The hybrid architecture from `PCL InterOp.md` is **implemented** for the first surface (XISF read). Two projects:

- `Astronomy.PCL.Native` (C++ DLL, statically links PCL `.lib`s from `Library\PCL\lib\x64\`).
- `Astronomy.PCL` (managed P/Invoke wrapper, `net8.0` x64).

C ABI is the source of truth: `Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h`. Every export is wrapped in `ASTRONOMY_PCL_TRY` / `ASTRONOMY_PCL_CATCH` (in `src\Exception.h`) — this catches `pcl::Exception` and `std::exception` and translates to status codes; the `catch (...)` branch picks up SEH (access violations etc.) due to `<ExceptionHandling>Async</ExceptionHandling>`. Last-error message is thread-local (`src\LastError.cpp`); consumers retrieve via `AstronomyXisf_GetLastErrorMessage` (two-call idiom: query length, alloc, fetch).

**Watch-out: don't use `XISFReader::ReadImage(FImage&)` for non-float source images.** PCL's auto-converting read path appears to need PixInsight platform services that aren't available in a host process — UInt16 or UInt32 sources surface as an unrecognized SEH exception ("Unknown C++ exception" at `catch (...)`). The wrapper dispatches on `ImageOptions.bitsPerSample` / `ieeefpSampleFormat`, reads in the file's native type (`UInt16Image`, `FImage`, `DImage`, …), then converts to float32 in our own loop. See `XisfCApi.cpp:AstronomyXisf_ReadImageF32`.

Adding new PCL surfaces: bind on demand. Add the `extern "C"` export to `XisfCApi.h` and `Astronomy.PCL.Native.def`, implement in `XisfCApi.cpp` wrapped in the macro, mirror the `[DllImport]` in `Astronomy.PCL/Interop/NativeMethods.cs`, expose the C# surface in `Astronomy.PCL`. Test in `Astronomy.Core.Tests/Tests/PCL/`.
