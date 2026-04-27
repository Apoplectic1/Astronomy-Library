# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout

Two projects in `Astronomy.sln`:

- **`Astronomy.Core`** — `netstandard2.0`, `LangVersion 7.3`. The library proper. Pure managed C#; only NuGet dep is `CoordinateSharp 3.4.1.1`. XML doc generation is on (`GenerateDocumentationFile=true`), so public surface is expected to carry `///` docs.
- **`Astronomy.Core.Tests`** — `net10.0`, `OutputType=Exe`. Hosts both xUnit tests (`Tests/`) and BenchmarkDotNet benchmarks (`Benchmarks/`) in a single assembly. `GenerateProgramFile=false` because `Program.cs` defines its own `Main` that delegates to `BenchmarkSwitcher.FromAssembly(...)`. `xUnit` discovery still works through `dotnet test` regardless of the custom `Main`.

## Build / test / benchmark

```bash
# Build everything
dotnet build Astronomy.sln

# Run xUnit tests (all)
dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj

# Run a single test by fully-qualified name or substring filter
dotnet test Astronomy.Core.Tests --filter "FullyQualifiedName~AltitudeCurveTests"
dotnet test Astronomy.Core.Tests --filter "TargetDefault_IsM31"

# Run BenchmarkDotNet — Release is mandatory (Debug numbers are misleading)
dotnet run -c Release --project Astronomy.Core.Tests -- --filter *
dotnet run -c Release --project Astronomy.Core.Tests -- --list tree
```

`BDN0001` (BenchmarkDotNet's "build in Release" warning) is suppressed in the csproj so xUnit can run cleanly under Debug; the runtime check inside BenchmarkSwitcher still enforces Release for benchmark runs.

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

`PCL InterOp.md` is an architectural decision document, not a build artifact. It describes a planned hybrid architecture (managed `Astronomy.PCL` + native `Astronomy.PCL.Native` DLL wrapping the PixInsight Class Library at `E:\Projects\VisualStudio\Astronomy\PCL\`). **No implementation exists yet** — neither project is in the solution. Don't infer they exist; consult that document if work in this area starts.
