# Astronomy Library Review — 2026-05-18

Reviewer: technical-advisor pass over `Astronomy.Core` (~5.5 KLOC of C#), `Astronomy.PCL` (~270 LOC managed wrapper), and `Astronomy.PCL.Native` (~390 LOC C++ ABI shim), plus the `Astronomy.Core.Tests` xUnit + BenchmarkDotNet harness.

The library is in good shape: a small, well-named public surface; one well-defended thread-safety story; near-universal XML doc coverage with `WarningsAsErrors;CS1591` enforcing it; almost every public type has a dedicated test file. The notes below are surgical, not foundational — they are the kind of polish you do when the architecture is already right and you want to keep entropy from creeping in as new surfaces land.

---

## Scope & method

Read in full: every file in `Astronomy.Core` (40 files, ~5.5 KLOC), every file in `Astronomy.PCL` (5 files), every file in `Astronomy.PCL.Native` (7 files: header, def, .cpp, helpers). Scanned the test layout, the `.sln`, all `.csproj`s, and `Directory.Build.props`. Categories the user asked me to emphasize: Separation of Concerns (SoC), reuse, thread safety, dead code, and "best design principles" broadly.

I did not run the build or the tests — read-only audit. None of the findings depend on dynamic behavior; they are visible from the source.

---

## What's working well

### Public-API shape and naming

- **One concept per type.** `AltAz`, `TargetGeometry`, `Location`, `Target`, `NightWindow`, `NightCache`, `MoonAvoidanceProfile`, `IHorizonProfile`, `RiseAndSetEvent` — each name reads as a noun whose definition matches its file. No anaemic-DTO bloat; no god-objects.
- **Static-class verbs.** Pure-function utilities are `static class`: `AltAzCalculator`, `TargetGeometry`, `NightCalculator`, `TwilightCalculator`, `BestSession`, `SessionSolvers`, `CoarseVisibility`, `IntegratedQuality`, `QualitySamples`, `RiseSet`, `SessionAltitude`, `TargetOrdering`, `TransitTime`, `VisibilityWindows`, `SunPosition`, `SunEvents`, `SunTracking`, `SunPower`, `SunHeliographic`, `SunSeparation`, `MoonSeparation`, `MoonAvoidance`, `LunarAge`, `SkyBrightness`, `Twilight`, `Bortle`, `Refraction`. The static/sealed split (verbs vs. nouns) is consistently applied and makes the API discoverable.
- **Magnitude-plus-flag hemisphere convention** is enforced at the type boundary by `Location` / `Target` constructors (normalise negative magnitude and flip the flag, `Locations\Location.cs:151-152` and `Targets\Target.cs:88`) and resolved to signed degrees inside the geometry layer (`AltAzCalculator.At`, `AltAz.cs:69-71`). The split frees `TargetGeometry` to take pure signed math without sign-bookkeeping; the canonical resolution idiom appears verbatim in `AltAz.cs:69-71` and is referenced by `<remarks>` in `TargetGeometry.cs:23-26`, so consumers have a pinned answer to "where do I deal with hemispheres?"
- **`With(...)` builders** on `Location`, `Target`, `MoonAvoidanceProfile` give every immutable POCO the same named-argument copy idiom; consumers learn the pattern once.
- **D/M/S accessors are computed on read** (`Location.LatDegrees`, `Target.RaHours`, etc.) — `CLAUDE.md` calls this out as a convention; the code matches it. No drift possible between stored decimal and presented sexagesimal.
- **Result types are explicit.** `AltAz` (struct) instead of `Tuple<double, double>`; `RiseSetState { Found, Circumpolar, NeverRises }` (`Session\RiseSet.cs:14-22`) instead of overloading `(null, null)`; `RiseAndSetEvent` with nullable `Rise`/`Set` for "didn't happen today". Each is documented with the explicit reason it replaced an earlier ambiguous shape.

### Separation of concerns (the macro picture)

The dependency graph is acyclic and reads like a textbook layer cake:

```
Time + Astrometry/Meeus (low-level math, internal)
        ↓
Astrometry.AstroUtil  +  Sun.*  +  Moon.*  +  Brightness.*  +  TargetGeometry  +  AltAz / AltAzCalculator
        ↓
Horizons / Locations / Targets / Night    (immutable inputs + envelope)
        ↓
Session.*  (placement, search, evaluation, visibility, ordering)
```

The boundary I want to call out specifically: **`TargetGeometry` does pure geometry** (signed degrees in, scalar out, plus the three sentinels `NaN` / `+Inf` / value for `HourAngleAtAltitude`) and **`AltAzCalculator` does coordinate resolution** (`Target`/`Location` POCO → signed degrees → `TargetGeometry`). Composability is exactly right: the Meeus path in `Astrometry\AstroUtil.cs:153-159` (`AltAzFromRaDec`) reuses `TargetGeometry.AltitudeAtHourAngle` / `AzimuthAtHourAngle` rather than re-implementing the spherical-trig kernel. One source of truth for the AltAz formula.

`BestSession` is a particularly clean separation of *placement* (transit-centred-or-wall-pushed in `BestSession.cs:333-394`) from *candidate resolution* (`ResolveCandidates` and the moon-clear intersector) from *quality scoring* (delegated to `IntegratedQuality`). The two-tier API — `For(...)` does the whole thing, `ResolveCandidates(...)` and `PlaceBest(...)` / `PlaceCentered(...)` let a caller compute candidates once and run multiple placement strategies — is the standard "easy default + advanced opt-in" shape and the XML doc on `ResolveCandidates` (`Session\BestSession.cs:118-145`) is explicit about why both exist. `SessionSolvers.LongestDuration` / `LongestDurationIn` mirrors the same two-tier shape.

### Thread safety

The `CLAUDE.md` promise — `Astronomy.Core` is thread-safe by construction with zero mutable static state — checks out under audit:

- **No `lock` / `Interlocked` / `Monitor` / `SemaphoreSlim` / `Mutex` / `SpinLock` anywhere in `Astronomy.Core`.** A `grep` for synchronisation primitives returns no hits.
- **No `[ThreadStatic]`, `ThreadLocal<T>`, `Lazy<T>`, or `ConcurrentDictionary` anywhere in `Astronomy.Core`.**
- Static state across the assembly:
  - `MoonPosition.mTermsLR`, `mTermsB` — `private static readonly int[]` (Meeus 47.A / 47.B tables, populated at type-init, never mutated). Read-only reads from concurrent threads are safe.
  - `Bortle.sZenithMag`, `sExtinctionK500` — same pattern (lookup tables).
  - `VisibilityWindows.ProfileScanStep` — `private static readonly TimeSpan` (value type, immutable).
  - That's the entire list.
- All public input types (`Target`, `Location`, `NightWindow`, `MoonAvoidanceProfile`) are immutable POCOs whose mutations return a fresh instance via `With(...)`.
- Return types are immutable POCOs, small structs (`AltAz`, `RiseSet` tuple), or `IReadOnlyList<T>` of `(DateTime, DateTime)` tuples — no exposed mutable collections.
- The only nondeterminism source is `Location.Default => new Location(... DateTime.Now ...)` (`Locations\Location.cs:229`), explicitly documented as such on the `Default` property and intended only for interactive callers; tests anchor explicitly via `.With(dateTime: ...)`.
- `NightCache` is immutable after construction; construction is sequential, and the underlying `NightCalculator.ComputeNight` is stateless, so a consumer can build several caches in parallel for different locations (CLAUDE.md claim).

The two caveats CLAUDE.md flags are the only ones I can confirm matter:

1. **Caller-supplied `Func<double, double> altitudeQuality`** in `IntegratedQuality.OverSession` / `HalvesAroundMidpoint`. If the consumer passes a closure that mutates captured state, thread safety of that closure is the consumer's problem. Library can't fix that; the doc is sufficient.
2. **`Astronomy.PCL` is NOT covered** by the Core contract. `XisfFile.cs:14` explicitly says "Not thread-safe — use one instance per thread." That matches the underlying PCL C++ library's historical concurrency story (single-threaded). The boundary is drawn at the right place.

### Test surface

37 test files mapping to 39 production files — coverage is essentially per-feature. Highlights:

- **Meeus correctness floor.** `Astronomy.Core.Tests\Tests\Astrometry\MeeusWorkedExamplesTests.cs` pins `SunEphemeris.Apparent`, `MoonPosition.Apparent` / `ApparentEcliptic`, and `MoonIllumination.Fraction` against Meeus's published worked examples (1992-04-12 / 1992-10-13 cases). These four facts protect every higher-level path from a silent regression in the periodic-term sum.
- **Parity fixtures** (`ParityFixtures.cs`, `ParityBaselineTests.cs`) pin the input table that the CoordinateSharp → Meeus swap was measured against. The tolerances baked in (30 arcsec moon, 60 s twilight, 0.005 illumination) are documented in the test file's preamble.
- **Cross-primitive cross-checks.** `SmokeTests.AltitudeAtTransit_MatchesMeridianAltitude` (`SmokeTests.cs:47-65`) checks the closed-form identity at HA=0 across `TransitTime` + `AltAzCalculator` + `TargetGeometry.MeridianAltitude` without needing an external reference value. This is exactly the kind of "free" property test you want layered on top of fixture comparisons.
- **PCL lifecycle.** `XisfLifecycleTests.cs` covers `Open/Close` 1000-cycle leak detection, double-`Dispose`, and access-after-`Dispose` — all three classic native-handle hazards.
- **Benchmark coverage rides next to xUnit.** `Astronomy.Core.Tests` is a single assembly hosting both `[Fact]`s and `[Benchmark]`s, with `Program.cs` delegating to `BenchmarkSwitcher` only under `dotnet run -c Release`. `BDN0001` is suppressed (`Astronomy.Core.Tests.csproj:21`) so xUnit can run under Debug; BDN's own runtime check enforces Release for benchmark runs. This is a clean way to avoid a separate Benchmarks csproj that would have its own dependency surface to maintain.

### PCL interop layer

The C-ABI design is textbook:

- **Single header is the source of truth** (`Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h`): integer status codes (`AstronomyXisfStatus_*`), opaque handle, plain-old-data struct (`AstronomyXisfImageInfo`), wchar paths. Every export wrapped in `ASTRONOMY_PCL_TRY` / `ASTRONOMY_PCL_CATCH` (`src/Exception.h:13-25`) that catches `pcl::Exception` → `std::exception` → `catch (...)` and maps each to a distinct status code. The `catch (...)` arm picks up SEH because `<ExceptionHandling>Async</ExceptionHandling>` is set on the vcxproj — that combination is the only way an access violation in PCL becomes a returnable status instead of a process kill.
- **Last-error message is `thread_local`** (`src/LastError.cpp:7`) and exposed via the two-call idiom (`AstronomyXisf_GetLastErrorMessage` returns the required buffer size when called with a null buffer). The managed side at `Astronomy.PCL\XisfFile.cs:135-143` consumes it correctly.
- **Static `PclRuntimeInit`** in an anonymous namespace at the top of `XisfCApi.cpp:32-44` disables PCL's GUI/console output once, so an exception's destructor won't try to write to a PixInsight console that doesn't exist in a host process. The comment explains exactly why this matters.
- **Sample-format dispatch is in the wrapper, not in PCL.** `AstronomyXisf_ReadImageF32` switches on `(bitsPerSample, ieeefpSampleFormat)` and reads in the file's native type (`pcl::FImage` / `DImage` / `UInt16Image` / `UInt8Image` / `UInt32Image`) before converting to float32. The comment at lines 150-152 captures the *exact* trap (PCL's `ReadImage(FImage&)` auto-converter needs PixInsight platform services that aren't available in a host process; SEH leaks out as `AstronomyXisfStatus_UnknownException`). This is the kind of "trap localised at the boundary" comment that pays for itself the first time someone tries to optimise it away.
- **C# wrapper is small and correct.** `XisfFile` is `IDisposable` with a finalizer guard, idempotent `Dispose`, and `ObjectDisposedException.ThrowIf` on every accessor. `NativeMethods` is `internal` with `[DllImport]` declarations and a single, well-named library constant (`Lib = "Astronomy.PCL.Native"`). The `unsafe` `fixed` block in `ReadImageF32` is the minimum unsafe footprint to pass a `float*` to the native side.

---

## Findings

### A. Duplication / reuse opportunities (low-risk, low-cost cleanup)

#### A1. `SiderealHoursPerSolarDay = 24.06570982441908` is private-const'd in **five** files

```
Session\AltitudeCurve.cs:36
Session\IntegratedQuality.cs:15
Session\RiseSet.cs:31
Session\TransitTime.cs:14
Session\VisibilityWindows.cs:18
```

Each is identical. If one ever gets refined (e.g. to take `julianCenturyT` into account for very long horizons), the others would silently drift. The Session layer already imports `Astronomy.Core.Time` — a single `public static class SiderealConstants` (or a `public const` on `SiderealTime` itself) would centralise this with no behaviour change. The `RiseSet.SiderealDayInSolarHours` derived constant could move with it.

Suggested location: `Astronomy.Core/Time/SiderealTime.cs`, as `public const double SiderealHoursPerSolarDay = 24.06570982441908;`. Tightly-coupled to `SiderealTime.Greenwich`'s polynomial slope already.

#### A2. `EnsureUtc` private static is duplicated in **six** files

```
Astrometry\AstroUtil.cs:161
Sun\SunEvents.cs:267
Sun\SunHeliographic.cs:124
Sun\SunPosition.cs:150
Sun\SunSeparation.cs:142
Sun\SunTracking.cs:115
```

All six bodies are the same one-liner:

```csharp
private static DateTime EnsureUtc(DateTime dt)
    => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
```

Note this is *not* a TZ conversion — it tags an `Unspecified` or `Local` value as Utc verbatim (`SpecifyKind`, not `ToUniversalTime`). That semantic decision deserves to live in one place, not six, especially since `NightCalculator.AsUtc` (`Night\NightCalculator.cs:155-163`) does the opposite (treats `Unspecified` as `Local` and converts). A reader hunting "what does the Library do with non-UTC kinds?" today has to read two different reductions and notice they disagree on `Unspecified`.

Suggested promotion: an `internal static class UtcGuard` (or `public static class TimeKindGuard`) in `Astronomy.Core/Time/`, with both `AsUtcTag(DateTime)` (the Sun-family `SpecifyKind` flavour) and `AsUtcConvert(DateTime)` (the `NightCalculator` flavour) sitting side by side. The naming makes the difference explicit at every call site instead of buried in a private helper.

#### A3. `Wrap360` is private-static'd in both horizon profiles

`Horizons\PolylineHorizonProfile.cs:108-113` and `Horizons\ObstructionTableHorizonProfile.cs:84-89` have byte-identical bodies. `MeeusUtility.Norm360` already does this but is `internal`. Either:

- Promote `Norm360` to `public` on `MeeusUtility` (or a new `public static class AngleMath`), and route both horizon profiles through it; or
- Add a small `internal static class Angles` in `Astronomy.Core/`.

Side benefit: `Sun\SunHeliographic.cs` and `Sun\SunEvents.cs` already use `MeeusUtility.Norm360` (`SunEvents.cs:235`), so the public horizon code path would simply join the same idiom the Sun code uses.

#### A4. `Interp3` / `Interpolate3` / `Unwrap` / `Frac` duplicated across the two Meeus rise/set solvers

```
Astrometry\Meeus\SunEphemeris.cs: Interpolate3 (line 212), Unwrap (222), Frac (230)
Astrometry\Meeus\MoonPosition.cs: Interp3      (line 412), Unwrap (420), Frac (427)
```

All six bodies are byte-identical. The Moon comment at `MoonPosition.cs:383-384` openly says "inlined here so MoonPosition stays self-contained — callers don't need to know which Meeus class owns the helper," which is a defensible choice when the duplication is two small private helpers in the same internal namespace. But there are *three* per class now, and a `private static class RiseSetMath` (internal to `Astrometry.Meeus`) with `Interp3` / `Unwrap` / `Frac` would consolidate them without breaking the "don't know which class owns it" intent.

The `RefineEvent` / `RefineMoonEvent` bodies (`SunEphemeris.cs:166-208`, `MoonPosition.cs:385-410`) are also structurally identical except that the Moon version drops the `isTransit` switch (always false) and runs 5 iterations vs 3. That's a thinner refactor — same loop body parameterised by the iteration count and the `dm` formula. Less urgent than the three primitives above; the kernels are short enough that inlined-twice is defensible.

#### A5. `SessionSolvers.ResolveCandidates` is a near-clone of `BestSession.ResolveCandidates`

`Session\SessionSolvers.cs:406-416` (private) and `Session\BestSession.cs:163-175` (public) are essentially the same function — read visibility windows; short-circuit to them if profile is null or disabled; otherwise intersect with moon-clear. The only differences:

- Public one does `ArgumentNullException.ThrowIfNull` on the three reference args; the private one skips it.
- Public one's `profile` parameter has a `= null` default; private one doesn't.

`SessionSolvers` could simply delegate to `BestSession.ResolveCandidates` — both reach into `BestSession.MoonClearIntersect` anyway (which is `internal` precisely for this kind of cross-class reuse, `BestSession.cs:402`). That removes ~10 lines of near-duplicate logic and one more place to forget if someone later changes the moon-intersection contract.

#### A6. The `(latSigned, decSigned, lonDegEast, raHours)` resolution preamble appears in 15 files

A grep across `Astronomy.Core` for the `North ? ... : -...` / `West ? -... : ...` resolution idiom returns hits in 15 files. The shape is always one of:

```csharp
double latSigned = location.North ? location.Latitude : -location.Latitude;
double decSigned = target.North   ? target.Declination : -target.Declination;
double lonDegEast = location.West ? -location.Longitude : location.Longitude;
double raHours = target.RightAscension;
```

The canonical resolution idiom lives in `AltAzCalculator.At` (`AltAz.cs:69-71`) and `<remarks>` in `TargetGeometry.cs:23-26` reference it. Now 15 callers repeat it. Two contained risks:

- **Convention drift.** If someone adds a fifth field (epoch, proper motion, parallax) the 17 sites all need to be updated.
- **Maintainability tax.** Reviewers reading any one Session helper see four lines of bookkeeping before the actual algorithm starts. The mental model is constant — but it's not free to re-prove on every read.

A modest cleanup: add an `internal static (double LatSigned, double LonEastDeg) AsSignedDegrees(this Location loc)` extension and an `internal static (double DecSigned, double RaHours) AsSignedRaDec(this Target tgt)` extension under `Astronomy.Core/`. That collapses the preamble to two lines per call site and makes the convention enforced by the type, not by reviewer vigilance. Public API doesn't change.

The "tradeoff" reading: at 15 hot-path sites, an extension that returns a tuple does pay the cost of constructing a tuple on every call. JIT inlining of tuples in net10.0 is reliable but not guaranteed. If you want extension methods *and* zero-overhead, make them `[MethodImpl(MethodImplOptions.AggressiveInlining)]` — the hot loops (`AltitudeCurve.Sample`, `IntegratedQuality.OverSession`) would still benefit from the same JIT optimisation they get today.

#### A7. `MeeusUtility.Norm24` is defined but unused

`Astrometry\Meeus\MeeusUtility.cs:60-65`. A grep across the entire Library for `Norm24` returns one hit — the declaration. `Norm360` and `NormPm180` are used heavily; `Norm24` was likely meant for sidereal-hour wrapping and never adopted. Either:

- Adopt it inside `Session\TransitTime.UtcAtOrAfter` (`Session\TransitTime.cs:47-48`) where the `while (deltaLst < 0) deltaLst += 24.0; while (deltaLst >= 24.0) deltaLst -= 24.0;` body would collapse to `deltaLst = Norm24(deltaLst);`, and inside `TargetGeometry.AzimuthAtHourAngle` (`TargetGeometry.cs:133-134`) for the same.
- Or delete it.

The "adopt" path is the better one — it's the same micro-uniformity argument as A3.

### B. Dead-code candidates (small, intentional review)

#### B1. `AstronomyXisf_Add(a, b)` — kept on purpose

`Astronomy.PCL.Native\src\XisfCApi.cpp:245-248`. Exported, declared, mirrored in `NativeMethods.cs:34` and hit by `XisfNativeSmokeTests.cs:11`. `PCL Wrapper Roadmap.md:30` calls it out: "smoke export, kept indefinitely as the first-line probe of the P/Invoke pipe." Not dead — keep, but maybe rename to `AstronomyXisf_Ping(a, b)` so the next reader doesn't squint at "why does the PCL wrapper add integers?" Cheap clarity win.

#### B2. `AstroUtil.GetMoonRiseAndSet` and `GetMoonPhaseName` have no Library callers or tests

`Astrometry\AstroUtil.cs:89-95` (`GetMoonRiseAndSet`) and `:105-121` (`GetMoonPhaseName`). Both are documented as "mirrors NINA's `AstroUtil` shape so port code is drop-in interchangeable" — i.e. they exist for downstream consumers (TargetPlanner port), not internal callers. Justifies their presence, but consider adding even one round-trip xUnit fact per method (the way `MoonAvoidanceTests` does) so a future refactor of the underlying Meeus path can't silently break the NINA-mirroring shape. Right now the only protection is the public-API contract.

#### B3. `NightCache` has no dedicated test file

`Night\NightCache.cs` is the only public type in `Astronomy.Core` without a matching `*Tests.cs` (verified via `find Tests/`). `ComputeYearStartDay` had a known off-by-one in the past (the `// Pre-2026-05-04` note at `NightCache.cs:84-87`); a one-Fact test pinning `ComputeYearStartDay(seed).Day == 1` for a handful of `seed` values would be cheap regression insurance. The cancellation-token path (`NightCache.cs:67`) is also untested — a `Theory` cancelling at day 0 / 30 / 364 would cover it.

#### B4. Several Sun helpers are tested but not consumed internally

`SunPower` (entire class), `SunHeliographic.DiskCenterAt` and `CarringtonRotationNumberAt`, `SunEvents.EquationOfTimeMinutes`, `SunPosition.ApparentDiameter*` — all have dedicated test files but no internal callers. Same as B2: they're explicitly the public surface for downstream solar-imaging / PV consumers. Reasonable to keep; the test coverage is the contract guard.

#### B5. `Location.Horizon` and `Location.Duration` are deliberately `[Obsolete(warning)]`

`Locations\Location.cs:64`, `:73`. CLAUDE.md tracks the transitional contract explicitly: tests still consume them via `TestLocations.PennsPark`, and the doc on each property names the only remaining consumer (TargetPlanner's `NamedLocationSetting` serialisation). Internal reads inside `Location` (the `With` copy, the auto-property init, and `MinutesAboveHorizon`) are pragma-suppressed (`Locations\Location.cs:125-127`, `:159-162`, `:187-201`) with a comment explaining each. Good housekeeping; the `error: false` keeps the deprecation a warning so downstream consumers can migrate at their own pace.

### C. SoC nits

#### C1. `Astronomy.Core.csproj` declares `<Platforms>AnyCPU;x64</Platforms>` even though the solution is x64-only

`Astronomy.Core/Astronomy.Core.csproj:12`. `Astronomy.sln` (lines 32-35) only defines `Debug|x64` and `Release|x64`. The AnyCPU platform in `Astronomy.Core.csproj` is harmless under the solution build (sln drives) but is reachable from `dotnet build Astronomy.Core/Astronomy.Core.csproj` and produces an AnyCPU output that nothing else in the portfolio can consume. CLAUDE.md says "Pure managed C# with no NuGet dependencies … Buildable independently with `dotnet build` if a contributor wants only the managed primitives" — so the AnyCPU surface is intentional for the standalone-managed-build case. If that's the design, keep — but consider documenting the rationale in the csproj itself (one-line `<!-- AnyCPU enables standalone managed build; sln-driven build is x64-only -->`).

#### C2. `MoonSeparation.IntervalsAboveDeg` and `BestSession.MoonClearIntersect` solve almost the same problem differently

`Moon\MoonSeparation.cs:104-156` walks the night at 10-min cadence, samples target-moon separation, and emits contiguous "above threshold" sub-intervals via linear-interp threshold crossings.

`Session\BestSession.cs:402-474` walks each visibility window at 10-min cadence, samples `(separation, moonAlt, age)`, evaluates `MoonAvoidance.IsRejected`, and emits contiguous "clear" sub-intervals via linear-interp delta crossings.

The two loops have the same shape, the same 10-min cadence, and the same linear-interp crossing logic — they differ only in their predicate (scalar `sep >= threshold` vs Lorentzian `actualSep >= requiredSepWithRelax(age, moonAlt)`). They could share an internal `IntervalSweep` helper that takes a predicate delegate and a per-sample observation tuple. *But* — the moon-aware path samples `LunarAge.DaysAt` and `MoonSeparation.ObserveAt` per step (three doubles) while the scalar path samples `DegreesAt` (one double), so the shared helper would have to be generic over the observation type. That's not free; the readability win is real but the type gymnastics could outweigh it. **My call: leave both as-is and add a comment in each pointing at the other.** This is the "two algorithms that look the same but mean different things" case where premature unification costs more than it saves.

#### C3. `BestSession.MoonClearIntersect` is `internal` and consumed by `SessionSolvers`

`Session\BestSession.cs:402` is `internal`, called from `Session\SessionSolvers.cs:415` (and only from there, besides `BestSession`'s own callers). The internal accessor is the right choice — keeping it public would commit to its shape as API. But the cross-class internal call does make `SessionSolvers` lightly coupled to `BestSession`'s private-ish implementation. Acceptable; just worth noting that if the moon-intersection algorithm ever moves (e.g. to a `Moon\MoonClearIntervals.cs` helper of its own), three call sites have to move with it. The A5 refactor would naturally fix this — `BestSession.ResolveCandidates` is already the public API for the same idea.

#### C4. `LunarAge.DaysAt` throws `ArgumentException` for non-UTC `DateTime`

`Moon\LunarAge.cs:58-59`. Stricter than the rest of the Library's `EnsureUtc` family, which silently tags non-UTC kinds (the Sun family) or converts them (`NightCalculator.AsUtc`). The throwing flavour is defensible — `LunarAge` is consumed in tight loops by `BestSession.MoonClearIntersect` where a stray non-UTC value would silently corrupt the answer — but it's an asymmetry worth either documenting in the public XML doc (currently only the `<exception>` tag mentions it) or eliminating by adopting the proposed centralised `AsUtcTag` helper (A2) and picking one stance.

### D. Design polish (genuinely cosmetic)

#### D1. `TargetGeometry.HourAngleAtAltitude` parameter order vs. its siblings

`TargetGeometry.cs:79` takes `(latDeg, decDeg, altDeg)`. `AltitudeAtHourAngle` (`:102`) takes `(haHours, latDeg, decDeg)`. `AzimuthAtHourAngle` (`:130`) takes `(haHours, latDeg, decDeg)`. `MeridianAltitude` (`:28`) takes `(latDeg, decDeg)`. The leading argument differs by method (HA when HA is an input; lat/dec when HA is the output). Defensible by the "input drives the signature" rule; possible to read as inconsistent. Not worth changing — but if `TargetGeometry` ever grows a `HourAngleAtAzimuth` or similar, lean on the (input-first) convention to keep the family parseable.

#### D2. `RiseSet.NextAtOrAfter`'s scalar-overload return signature could use the same record shape as the profile overload

Both overloads return `(RiseSetState State, DateTime? Rise, DateTime? Set)`. C# 9+ `record struct` would let `RiseSetResult { State, Rise, Set }` carry the name and remove the positional unpack burden at call sites (`TargetOrdering.RiseKey` at line 100-110 already destructures three fields). The tuple is fine; the record makes "what's this `(Found, dt, dt)`?" answerable from the type alone. Low priority.

### E. PCL interop polish

#### E1. The `Astronomy.PCL.Native.def` and `XisfCApi.h` lists could drift

Currently both contain identical export sets (8 entries). Adding a new export means editing three places: the header, the `.def`, and `NativeMethods.cs`. A short comment in either the header or the `.def` saying "*keep these in sync*" prevents the first-time-contributor surprise.

#### E2. `XisfFile` finalizer + idempotent `Dispose`

`Astronomy.PCL\XisfFile.cs:104-122` does the standard `Dispose` + `~XisfFile` pair. Both call `AstronomyXisf_Close(_handle)` and zero the handle. Correct, idempotent. Minor: the duplicated body could collapse into a private `CloseNative()` helper. Cosmetic; not breaking.

#### E3. PCL is a known thread-safety hazard; the wrapper inherits that

Reflected correctly in `XisfFile`'s "Not thread-safe — use one instance per thread" remark. Could be reinforced by an analyser annotation or at least a comment in `NativeMethods.cs` calling out that the *thread-local* `g_lastError` storage in `LastError.cpp` means an error consumed from a different thread than the one that produced it will read empty. That's not a bug — it's the right semantics for per-call status — but a one-liner in the wrapper would save someone an hour of debugging when the cross-thread case is first hit.

---

## F. `Astronomy.Core.Tests` regression suite — gaps and improvements

The test suite is the strongest single artefact in the portfolio after the library itself. 37 test files across `Tests/` mapping to 39 production types; one BenchmarkDotNet harness sharing the assembly for hot-path measurement; a per-fixture parity table (`ParityFixtures.cs`) covering DST collisions, polar day / polar night, both hemispheres, and the autumn-equinox dusk-recovery edge case. The notes below are about closing small gaps, not rebuilding anything.

### F1. What the suite gets right (worth preserving when refactoring)

- **Meeus-worked-example gold standard.** `MeeusWorkedExamplesTests.cs` pins the `SunEphemeris.Apparent` 1992-10-13 case, `MoonPosition.ApparentEcliptic` and `Apparent` 1992-04-12 cases, and `MoonIllumination.Fraction` 1992-04-12 case, with tolerances tied to each formula's documented accuracy budget (sun 0.02°, moon 0.005°, illumination 0.001). Reflection is used to keep the Meeus classes `internal` (test comment at `MeeusWorkedExamplesTests.cs:18-21` calls this out as a deliberate choice over polluting `InternalsVisibleTo`). These four facts protect every higher-level path from a silent periodic-term regression — if anything, this pattern should be extended (see F4 below).
- **Cross-primitive identity checks.** `SmokeTests.AltitudeAtTransit_MatchesMeridianAltitude` (`SmokeTests.cs:47-65`) and `AltitudeCurveTests.Sample_MatchesPerMinuteAltAz` (`AltitudeCurveTests.cs:24-43`) are the right shape for testing pure math — assert a closed-form identity across two independent code paths instead of pinning a magic number. A divergence under future refactoring shows up immediately and the diagnostic message names both inputs.
- **Parity fixture table** (`ParityFixtures.cs`) covers nine real-world scenarios — Penns Park across both DST transitions, summer solstice, polar day (Reykjavík), polar night (Antarctic edge), equator solstice, Sydney, Tokyo. Each fixture is small (lat / lon / hemisphere flags / UTC moment / `ExpectValidNight`) and the consuming `ParityBaselineTests` uses `[Theory] [MemberData(...)]` so adding a tenth case is a one-line edit. `ITestOutputHelper` captures the computed dusk/dawn/illumination values for every case on every run, so a future "Phase 3" tightening (snapshot the CoordinateSharp output, assert the Meeus result matches within tolerance) has its reference data already harvestable from the log.
- **DST-collision regression net.** `NightCalculatorTests` (`NightCalculatorTests.cs:24-68`) walks 9 consecutive evenings across the autumn collision window at Penns Park EDT (Oct 6–14) with two distinct assertions — *adjacent dusks must differ* and *night duration must be < 15 h*. Both assertions independently catch the `SunEphemeris.RiseSet` single-event drop that `BracketingPair`'s `FindLatestDuskBefore` exists to recover. The test file's preamble explains exactly why both checks exist; a future refactor of the recovery path won't lose either guard without one of the two failing first.
- **PCL native-handle lifecycle hazards.** `XisfLifecycleTests` covers the three classic native-handle traps in one file: 1000-cycle leak detection via `Process.WorkingSet64` (`XisfLifecycleTests.cs:14-35`), double-`Dispose` (`:38-43`), and access-after-`Dispose` (`:46-51`). The 1.5 GB headroom on the leak test is intentional and the comment explains why (PCL may pool internally; the check catches per-iteration growth, not absolute consumption).
- **Argument-null contract is tested almost everywhere.** A grep across the test suite for `ArgumentNullException` shows it asserted on every public method that throws one — `IntegratedQuality.HalvesAroundMidpoint_NullArgs_Throws`, `RiseSet.Profile_NullArgs_Throws`, `TransitTime.DistanceFromMidpoint_NullArgs_Throws`, `LunarAge.DaysAt_NonUtcKind_Throws`, the three horizon-profile null guards, etc. The decision to `<Nullable>disable</Nullable>` in the test csproj (`Astronomy.Core.Tests.csproj:11`) with the comment explaining why is the right tradeoff — verifying runtime contract is the test's job.
- **Cross-validation sums and ratios.** `IntegratedQualityTests.HalvesAroundMidpoint_SumApproximatesFullIntegral` (`IntegratedQualityTests.cs:64-77`) asserts `halves.first + halves.second ≈ OverSession(full window)` to 3 decimals; `IntegratedQualityTests.HalvesAroundMidpoint_TransitCenteredSession_HalvesAreEqual` asserts symmetry around HA=0. These are the "physics-style" property tests that don't depend on an external reference value — exactly the right safety net for a numerical-quadrature primitive.

### F2. Coverage gaps (production types with no dedicated test file)

A `find Tests/` enumeration plus a `grep` for each public type name surfaces six public surfaces with no dedicated `*Tests.cs` file. Two are previously noted in §B; the rest are new:

| Public type | Dedicated test file? | Covered transitively by | Why a direct test would help |
| --- | --- | --- | --- |
| `Night.NightCache` | **No** (see B3) | `BestSessionTests` builds a `NightCache` to drive its scenarios but doesn't assert on the cache's own contract | `ComputeYearStartDay` has a documented historical off-by-one (`NightCache.cs:84-87`); the cancellation-token path (`NightCache.cs:67`) is wholly unguarded |
| `TargetGeometry` | **No** | `SmokeTests` identity check + every Session test consumes it indirectly | The three sentinel returns from `HourAngleAtAltitude` (`NaN`, `+∞`, value) are the explicit contract referenced by `RiseSet`, `CoarseVisibility`, `VisibilityWindows.ForScalar`. None of those consumers explicitly tests the *boundary* (NaN ↔ value ↔ +∞ transition). One `[Theory]` per sentinel would pin the contract at the source instead of three sites away |
| `Time.JulianDate` / `Time.SiderealTime` | **No** | Every Sun/Moon/Meeus test consumes them; `LunarAgeTests.NewMoonReferenceJd_MatchesJulianDateFromUtc` (`LunarAgeTests.cs:73-81`) gives one round-trip check on `JulianDate.FromUtc` | The OADate-offset idiom in `JulianDate.FromUtc` (`+2415018.5`) and the USNO GMST polynomial in `SiderealTime.Greenwich` are both compact and verifiable against published values (Meeus AA gives JD for several historical instants; USNO publishes GMST tables). Direct tests would catch a regression that everyone's downstream test would also catch but with worse blame. Small "pinned-value" file (e.g. `J2000.0 UT → JD 2451545.0 ± 1e-6` plus `GMST(J2000) = 18.6973745…`) |
| `Brightness.Bortle` | **No** | None — the lookup tables are read by `SkyBrightness` consumers but not asserted on | `ClampIndex` clamps `bortleClass` to `[1, 9]` (`Bortle.cs:62-67`); the silent clamp is a contract worth pinning. Pass `0`, `10`, `-5`, `100` and assert each maps to the boundary value. The tables themselves are the kind of "if this drifts, someone changed the standard" data that benefits from an explicit "table values are X for class N" test |
| `Brightness.Twilight` | Yes (covered) | n/a — has dedicated `TwilightTests.cs` | (Listed for completeness; the existing tests already cover the four calibration points and monotonicity) |
| `Astrometry.ObserverInfo` | **No** | Every `AstroUtil`-consuming test instantiates one | Trivial value type; one constructor + property test (3 lines) closes the file gap. Low value but cheap |
| `Astrometry.RiseAndSetEvent` | **No** | Every `SunEvents`-consuming test reads `Rise` / `Set` | Same as above. Cheap |
| `Astrometry.AstroUtil.GetMoonRiseAndSet` / `.GetMoonPhaseName` | **No** (see B2) | Neither is consumed inside the Library or by tests; only `MoonSeparation.ObserveAt` paths exercise `GetMoonAltAz` transitively | These two are explicitly the NINA-port mirror surface (`AstroUtil.cs:13-15`). A single-line round-trip per method (e.g. `GetMoonPhaseName(known_full_moon_utc) == "Full Moon"`) protects the port contract |

### F3. Coverage gaps (production behaviours with thin tests)

Even where a test file exists, several documented invariants aren't directly asserted:

- **`Location` / `Target` hemisphere normalisation.** The constructor flips `North` / `West` when a negative magnitude is passed (`Locations\Location.cs:151-152`, `Targets\Target.cs:88`). `SmokeTests.LocationDefault_IsPublicSafe` (`SmokeTests.cs:30-44`) only checks the default. A `[Theory]` asserting `new Location(..., latitude: -40, north: true, ...)` yields `{ Latitude == 40, North == false }` (and the same for longitude / declination) would pin the "sign takes precedence over the flag" rule the way `LunarAgeTests` pins its kind contract. Same idea for `Target`'s declination sign flip.
- **`With(...)` builder field-preservation.** `Location.With` and `Target.With` both copy 12 / 6 fields through nullable parameters. If a field is ever added to the constructor and a contributor forgets to thread it through `With`, no test would catch it. One Fact per type — round-trip `instance.With() == instance` field-by-field — closes that loop.
- **D/M/S accessor correctness.** The XML doc on `Target.DecDegrees` (`Targets\Target.cs:25-29`) explicitly notes that an earlier implementation routed through `TimeSpan.FromHours(double)` and produced hour-of-declination values instead of degree-of-declination values. That regression class is exactly the kind a one-liner test prevents — `new Target(..., declination: 41.269167) // expect DecDegrees=41, DecMinutes=16, DecSeconds≈9` — but no test pins it today.
- **`NightWindow.IsValid` sentinel.** The struct's only behaviour beyond field access is the `IsValid` short-circuit that checks both endpoints against `DateTime.MinValue` (`Night\NightWindow.cs:40-41`). Consumers like `CoarseVisibility.IsEverVisible` rely on it (`Session\CoarseVisibility.cs:46`). Direct test: construct windows with each combination of MinValue endpoints and assert `IsValid` resolves correctly.
- **`AltAz.Deconstruct`.** The public `var (alt, az) = ...` pattern is documented (`AltAz.cs:34-42`) but not asserted. Cheap one-liner.
- **`TargetGeometry.HourAngleAtAltitude` sentinel boundary.** As noted in F2: the contract is the *sentinel*, not the value. One `[Theory] [InlineData(...)]` covering "never rises" (e.g. `lat=40, dec=-70, alt=0`), "circumpolar above" (e.g. `lat=70, dec=80, alt=0`), and a normal crossing pins the three return modes.
- **`SunHeliographic.DiskCenterAt` versus a Meeus worked example.** Meeus AA 29.b (pg. 191) works the 1992-10-13 case. The current `SunHeliographicTests` checks Carrington-period delta-of-1 and B0 swing over a year — both useful — but doesn't pin `(P, B0, L0)` against a textbook reference. A fourth `[Fact]` mirroring the `SunEphemeris` 1992 test would close this gap.

### F4. Suite-level improvements

- **Pinned-value baselines for `JulianDate` and `SiderealTime`.** Same shape as `MeeusWorkedExamplesTests` — pull a handful of published reference instants (J2000 UT, the USNO GMST table, the IAU 1976 J2000 epoch) and assert to ~1e-6 tolerance. Cheap, prevents the "everything broke and I don't know which primitive" regression class.
- **Promote `TestLocations` to a `[Theory]` source.** `TestLocations.PennsPark` is reused across maybe two dozen tests; adding `TestLocations.Sydney`, `TestLocations.Equator`, `TestLocations.Reykjavik` and feeding `[ClassData]` / `[MemberData]` to a handful of "this property should hold at any location" tests (`AltitudeAtTransit_MatchesMeridianAltitude`, `Sample_MatchesPerMinuteAltAz`) increases coverage from "works at 40°N suburban" to "works in both hemispheres + polar fringe" with zero new assertion logic. The `ParityFixtures` table is a model for how to do this cleanly.
- **Property-based generators for `Target` / `Location` round-trips.** A small FsCheck or xUnit-AutoData generator that produces random `(lat, lon, ra, dec, north, west)` tuples and asserts `Target.With(...)` round-trips field-by-field, or that `AltAzCalculator.At` is invariant under `(lat, north) → (-lat, !north)` reflection, would surface convention-drift bugs that hand-coded fixtures miss. Optional — and would add a NuGet dependency the Library proper doesn't have today — but worth considering as the public surface grows.
- **`[Trait("Category", ...)]` tags for slow vs fast tests.** `XisfLifecycleTests.OpenClose_Loop_DoesNotLeak` runs 1000 iterations; the rest of the suite is sub-second. Tagging it `[Trait("Category", "Slow")]` lets the dev-loop `dotnet test --filter Category!=Slow` shave the slow check out of inner-loop runs while CI still catches it. Same for the year-sweep parity tests when they materialise. xUnit's `[Trait]` is zero-cost when unused.
- **Phase 3 of the parity sweep.** `ParityBaselineTests.cs:14-23` documents the deferred deliverable: snapshot CoordinateSharp output against the fixture table; tighten the assertions to "Meeus matches CoordinateSharp within 30 arcsec / 60 s / 0.005". The fixture table is in place, the consuming `[Theory]` is in place, the tolerance budget is documented — all that's missing is the snapshot. This is the single highest-leverage outstanding test improvement in the suite. Without it, the parity claim ("Meeus reproduces CoordinateSharp's behaviour") is plausible-only, not pinned. With it, a future regression in any Meeus primitive shows up as a single failing parity case with the disagreement quantified.
- **Make `AstronomyXisf_Add` a non-test "ping" once Phase 3 lands.** Right now `XisfNativeSmokeTests.Add_ReturnsSum` (`XisfNativeSmokeTests.cs:11`) is a 1-line `Assert.Equal(7, ...)`. As more PCL surfaces land, the first test that *reads* an XISF file (`XisfReadTests.Open_GetInfo_ReadsFloat32`) is also a P/Invoke pipeline check, so the `Add` ping is mostly belt-and-braces. Keep, but consider renaming the *test* to `Smoke_NativeLoadIsHealthy` so a reader scrolling the test list sees what it's actually checking. (The native export itself stays, per `PCL Wrapper Roadmap.md:30`.)
- **`Astronomy.PCL.Native` has no parametric coverage of sample-format dispatch.** `XisfReadTests.Open_GetInfo_ReadsFloat32` reads one fixture (`TestData/test.xisf`) and asserts the buffer is filled and contains a finite positive value. The `(bitsPerSample, ieeefpSampleFormat)` switch in `AstronomyXisf_ReadImageF32` (`XisfCApi.cpp:153-219`) has five branches: float32, float64, UInt16, UInt8, UInt32. Only the branch matching the test fixture's encoding is exercised today. The fixture is a single file (`test.xisf` copied from PCL upstream), so coverage of the other four would need additional test fixtures generated via the `xisf.exe` CLI or via the PCL `XISFWriter`. Defer until a downstream consumer actually feeds the wrapper a UInt16 or 64-bit-float image — the comment at `XisfCApi.cpp:150-152` documents exactly why each branch was added — but it's worth tracking the gap.
- **Benchmark hygiene.** `HotPathBenchmarks` declares `[InProcessConfig]` because the assembly transitively references `Astronomy.PCL.Native.vcxproj` which `dotnet build` can't drive (`HotPathBenchmarks.cs:180-190`). Sound workaround. One side effect: `Astronomy.Core`-only benchmarks (e.g. `FmaBenchmarks`, `AltitudeCurveBenchmark`) also run in-process even though they don't depend on the native DLL. If benchmark stability ever becomes an issue, the simplest fix is to split `Astronomy.Core.Tests` into `Astronomy.Core.Benchmarks` (separate csproj, no PCL ref, BDN's default toolchain) and leave `Tests/` for xUnit-only. Premature today; worth flagging.

### F5. Test-suite action plan

1. **F2.NightCache** (close the file gap) — one test class, ~30 LOC.
2. **F3.TargetGeometry sentinels** (`[Theory]` covering the three branches of `HourAngleAtAltitude`) — ~15 LOC.
3. **F3.Location/Target hemisphere normalisation** (`[Theory]` × 4 covering the four flip cases) — ~20 LOC.
4. **F2.JulianDate / SiderealTime pinned-value baselines** — ~30 LOC.
5. **F2.Bortle clamping + table-value pin** — ~15 LOC.
6. **F4.Promote `TestLocations` to a `[Theory]` source** for the cross-location-invariant tests — refactor of ~3 existing tests, ~50 LOC net.
7. **F4.Phase 3 parity snapshot** — the single most valuable outstanding improvement, but the largest. Requires capturing the current CoordinateSharp baseline somewhere durable (could be a static `Dictionary<string, (double duskOffset, double dawnOffset, …)>` in `ParityFixtures`), then tightening `ParityBaselineTests`' `InRange` calls to use those baselines with the documented tolerances. Defer if the CoordinateSharp removal is fully complete and there's no longer a comparison oracle; in that case, replace with snapshots taken from an authoritative source like Stellarium / USNO MICA, which is the same idea but with a stable reference.

Items 1-5 are mechanical and total under 200 LOC. Item 6 is a cleanup that increases existing-test coverage breadth. Item 7 is the strategic improvement that elevates parity from "plausibility-only" to "asserted against a fixed oracle."

---

## Recommended action plan (smallest blast radius first)

1. **A1** — promote `SiderealHoursPerSolarDay` to a single `public const` in `Time/SiderealTime`. ~10 file touches, mechanical, no behavioural change.
2. **A2** — centralise `EnsureUtc` / `AsUtc` into a `TimeKindGuard` helper with both flavours named explicitly. Forces the design decision into one place and gives `LunarAge.DaysAt`'s throwing variant something to align with (or against).
3. **A3** — promote `Wrap360` to `public Norm360` (already in `MeeusUtility`) or a small `AngleMath` helper; route both horizon profiles through it. Trivial.
4. **A5** — delete `SessionSolvers.ResolveCandidates`; call `BestSession.ResolveCandidates` instead. Two lines deleted, one line changed.
5. **A4** — `private static class RiseSetMath` inside `Astrometry.Meeus` for the three duplicated primitives.
6. **B3** — add a `NightCacheTests.cs` covering `ComputeYearStartDay` (the historical off-by-one fix) and the cancellation-token path.
7. **A6** — extension-method preamble (`AsSignedDegrees`, `AsSignedRaDec`) with `AggressiveInlining`. Larger touch (~17 files), bigger readability payoff; do last so the smaller cleanups don't interleave with the bigger one.
8. **A7** — adopt or delete `MeeusUtility.Norm24`. A 30-second decision.

None of items 1-7 changes any public-API shape, behaviour, or output. Each is a low-risk consolidation against the entropy that naturally accumulates in a 5.5 KLOC astronomy library.

---

## Counterpoints I considered but rejected

- **"Make `MoonPosition.mTermsLR` / `mTermsB` an `ImmutableArray<int>` or `ReadOnlySpan<int>`."** No — `private static readonly int[]` indexing is JIT-friendly and the array is never exposed. `ImmutableArray<int>` adds an indirection the hot loop doesn't need; `ReadOnlySpan<int>` can't be a static field (ref-struct restriction). Keep as-is.
- **"Split `AstroUtil` into per-method classes."** No — `AstroUtil` is explicitly the NINA-port mirror surface (`Astrometry/AstroUtil.cs:13-15`). Splitting it would break the "drop-in interchangeable" intent. The five public methods are appropriately related.
- **"Move `MeeusUtility.HorizonDipDeg` out of the Meeus namespace."** No — Meeus is the citation source (see the formula attribution at `MeeusUtility.cs:144-146`); keeping it next to the other Meeus primitives keeps the citation surface together.
- **"Replace the `(DateTime Start, DateTime End)` tuples with a named struct."** Tempting (you'd get XML doc per field), but the tuple type flows through `IReadOnlyList<(DateTime, DateTime)>` returns in five public APIs (`VisibilityWindows.For`, `BestSession.ResolveCandidates`, `SessionSolvers.LongestDurationIn`, `MoonSeparation.IntervalsAboveDeg`, `SunSeparation.IntervalsBelowDeg`). Switching to a record would be a breaking-API change for downstream consumers. Defer until the v2-API window opens.

---

## Final read

This is a library that knows what it is. The public surface is small (a handful of static classes plus four immutable POCOs and one interface), the conventions are stated up-front and applied consistently, the thread-safety promise is real and verifiable, the test coverage tracks the public surface 1-to-1, and the PCL interop boundary is drawn in exactly the place where it pays its way. The findings above are entropy-management, not architectural debt — the kind of pass you do every six months to keep small duplications from quietly becoming large ones.

The two consolidations I'd want to see first are **A1** (one place for `SiderealHoursPerSolarDay`) and **A2** (one place for the `EnsureUtc` / `AsUtc` decision). Both are pure-mechanical, both eliminate the "which copy is canonical?" question, and A2 in particular surfaces an interesting latent inconsistency between the Sun family (`SpecifyKind`) and `NightCalculator` (`ToUniversalTime`) that's worth resolving deliberately rather than by accident.
