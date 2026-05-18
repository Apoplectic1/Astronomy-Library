# Library Review — Re-check after implementation (2026-05-18)

Companion to `docs/2026-05-18-library-review.md` and `docs/2026-05-18-library-review-followups.md`. A read-only pass verifying that each item from the original review landed in the source tree as the follow-up doc claims, and surfacing any small new findings introduced by the refactor session.

Reference: 21 commits on `dev` from `cf12cde` through `9ba35f0`, plus the four new helper files (`TimeKindGuard`, `RiseSetMath`, `LocationExtensions`, `TargetExtensions`) and ten new test files (`AltAzTests`, `AstroUtilMoonTests`, `BortleTests`, `HemisphereNormalisationTests`, `JulianDateAndSiderealTimeTests`, `LocationTests`, `NightCacheTests`, `NightWindowTests`, `TargetGeometryTests`, `TargetTests`).

---

## Verdict

Every actionable item from the review landed. The mechanical refactors (A1, A2, A3, A4, A5, A7) hit zero net behaviour change while collapsing the duplication; the test additions (F2 / F3 / F4 / F5.7) brought coverage from 7 missing-test-file gaps + thin invariants down to comprehensive. Three substantive bonuses also landed that I'd marked as deferred or out-of-scope:

- **A2 unified an inconsistent contract.** The Sun-family `EnsureUtc` used `SpecifyKind` (silently tag non-Utc as Utc — wrong for non-Utc inputs that aren't actually Utc), while `NightCalculator.AsUtc` used `ToUniversalTime` (real conversion). Both now route through `TimeKindGuard.AsUtc`, which uses the converting flavour. The asymmetry I flagged in C4 of the original review wasn't just an aesthetic inconsistency — it was a silent-incorrect-result bug for any caller that passed a non-Utc DateTime to `SunPosition.AltAzAt` and friends. Fixed as a side benefit of A2.
- **F5.7 went further than I scoped.** I'd outlined a self-snapshot Phase 3 with the NINA-as-oracle as a follow-up. The actual implementation includes a `_DumpBaselinesForRegeneration` xUnit Fact (skipped by default) that regenerates the dictionary from the current Library output and prints C# initializers ready to paste into `ParityFixtures.Baselines`. Eliminates the "how do I refresh the baseline after a deliberate change?" question from the next-maintainer's checklist. Nice ergonomic touch.
- **A6 was applied surgically.** I'd recommended an extension on all ~15 inline-preamble sites. The implementation hit 7 sites — specifically the ones where the full 4-value preamble (lat + lon + dec + ra) was being unpacked. Single-value sites (lat-only, lon-only, or dec-only) were left inline as a deliberate tradeoff: an extra method-and-tuple-extraction at a single-value site has worse signal-to-noise than the inline conditional. Defensible; see "Small residual" #1 below for the remaining surface area.

The thread-safety claim still holds verbatim: zero `lock` / `Interlocked` / `Monitor` / `SemaphoreSlim` / `ThreadStatic` / `ThreadLocal` / `Lazy<T>` / `ConcurrentDictionary` anywhere in `Astronomy.Core`; the same five `static readonly` fields as before (Bortle's two tables, MoonPosition's two tables, VisibilityWindows.ProfileScanStep); the four added helper classes (`TimeKindGuard`, `RiseSetMath`, `LocationExtensions`, `TargetExtensions`) are all `internal static` with zero state. `DateTime.Now` still only at `Location.Default`.

---

## Per-item verification

| Item | Status | Notes |
| --- | --- | --- |
| **A1** SiderealHoursPerSolarDay centralised | ✓ | `Time/SiderealTime.cs:21`. All 5 prior duplicate `private const` declarations removed; 7 consumer references now resolve to `SiderealTime.SiderealHoursPerSolarDay`. The `SiderealDayInSolarHours` derived constant (`SiderealTime.cs:27`) moved with it; consumed by `RiseSet.cs:90`. `SiderealTime.Greenwich` polynomial slope also now uses the constant (`SiderealTime.cs:36`) — completes the circle. |
| **A2** EnsureUtc → TimeKindGuard | ✓ + bonus | New `Time/TimeKindGuard.cs` (`internal static`). 6 prior `private static DateTime EnsureUtc` duplicates removed; `NightCalculator.AsUtc` also folded in. Bonus: unified the silent `SpecifyKind` vs converting `ToUniversalTime` divergence between the Sun family and `NightCalculator` — both now use the converting flavour. `AltAzCalculator.Of` (`AltAz.cs:95`) also routes through it now; previously it called `.ToUniversalTime()` directly. |
| **A3** Wrap360 → Norm360 | ✓ | Both horizon profiles call `MeeusUtility.Norm360` (`PolylineHorizonProfile.cs:48, 67`; `ObstructionTableHorizonProfile.cs:44, 67`). No remaining `private static double Wrap360` in the assembly. |
| **A4** RiseSetMath extraction | ✓ | New `Astrometry/Meeus/RiseSetMath.cs` (`internal static`) with `Interp3` / `Unwrap` / `Frac`. `SunEphemeris` and `MoonPosition` now both call through the helper at 5 and 8 sites respectively. Bodies are byte-identical to the pre-extraction `Interpolate3` / `Interp3` pair (the `SunEphemeris` rename to `Interp3` collapses the two-name divergence too). |
| **A5** SessionSolvers.ResolveCandidates | ✓ | Private clone deleted. 6 call sites (`SessionSolvers.cs:91, 233, 279, 374, 413, 461`) now call `BestSession.ResolveCandidates` directly. |
| **A6** Hemisphere extensions | ✓ partial | New `Locations/LocationExtensions.cs` (`AsSignedDegrees`) and `Targets/TargetExtensions.cs` (`AsSignedRaDec`), both `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. Applied at 7 multi-value sites (AltAz:68-69, AltitudeCurve:72-73, VisibilityWindows:80-81, IntegratedQuality:55-56 / 97-98, SunEvents:259, SunPosition:54). 12 single-value sites still inline; see Small residual #1. |
| **A7** Norm24 adoption | ✓ | `TransitTime.cs:45` and `TargetGeometry.cs:133` both collapse the inline `while (x < 0) x += 24; while (x >= 24) x -= 24;` loops to one `MeeusUtility.Norm24(x)` call. |
| **B1** Add → Ping rename | ✓ | All five touch points: `XisfCApi.h:50`, `XisfCApi.cpp:245`, `Astronomy.PCL.Native.def:13`, `NativeMethods.cs:39`, `XisfNativeSmokeTests.cs:15`. `PCL Wrapper Roadmap.md:30` also updated. |
| **B2** NINA-mirror contract guards | ✓ | New `Tests/AstroUtilMoonTests.cs` (5 facts). Pins `GetMoonPhaseName` at the new-moon reference + four cardinal phases via `[Theory] [InlineData]` + a full-cycle canonical-name sweep, and `GetMoonRiseAndSet` for both the mid-latitude success case and the elevation-shifts-rise-earlier-set-later contract. |
| **B3** NightCacheTests | ✓ | New `Tests/NightCacheTests.cs` (8 facts / theories). Pins `ComputeYearStartDay`'s reduce-to-first-of-month rule across 5 seed shapes (including the leap-day case the previous off-by-one would have mishandled), Kind preservation across all 3 `DateTimeKind`s, `ComputeYearDaysCount` leap-year handling, ctor null / negative / zero / cancellation-token guards. |
| **C1** AnyCPU rationale | ✓ | Documented in `2026-05-18-library-review-followups.md`. Not pulled into the csproj itself; deliberate (the standalone-managed-build use case is the rationale, and that's now captured in the follow-up doc indexed by the review). |
| **C4** LunarAge exception | ✓ | `CLAUDE.md:54` "DateTime kinds" bullet now names `TimeKindGuard.AsUtc` as the canonical helper and explicitly notes the `LunarAge.DaysAt` exception with the `BestSession.MoonClearIntersect` tight-loop rationale. The TimeKindGuard XML doc itself (`TimeKindGuard.cs:9-23`) repeats this exception so the helper's source is self-explaining. |
| **D2** RiseSetResult record struct | ✓ | `Session/RiseSet.cs:38-41` defines `public readonly record struct RiseSetResult(RiseSetState, DateTime?, DateTime?)`. Both `NextAtOrAfter` overloads (scalar and `IHorizonProfile`) return it. Positional destructure idiom unchanged at call sites; name-access (`result.State`) now possible. Follow-up doc claims 440 tests still pass post-conversion. |
| **E1** .def keep-in-sync comment | ✓ | `Astronomy.PCL.Native.def:1-4` block. |
| **E3** Cross-thread `GetLastErrorMessage` caveat | ✓ | `NativeMethods.cs:6-10` block. Explicitly names the thread_local hazard and recommends same-thread retrieval. |
| **F2.NightCache** | ✓ | See B3 above. |
| **F2.TargetGeometry sentinels** | ✓ | New `Tests/TargetGeometryTests.cs` (3 theories). Sentinel `[Theory]` covers the four branches (never-rises NaN, circumpolar-above +Inf in both hemispheres, finite-value normal). Closed-form `MeridianAltitude` and the `AltitudeAtHourAngle(HA=0) == MeridianAltitude` identity also pinned. |
| **F2.JulianDate / SiderealTime baselines** | ✓ | New `Tests/JulianDateAndSiderealTimeTests.cs` (5 facts). J2000.0 epoch pinned to 6 decimals, Meeus AA Ex 7.a JD pinned, GMST(J2000) pinned to 8 decimals against the USNO constant, J2000+1d wrap pinned, Local-at-Greenwich identity pinned. |
| **F2.Bortle clamp + table** | ✓ | New `Tests/BortleTests.cs` (4 theories). Below-range / above-range clamps explicitly tested with `int.MinValue` / `int.MaxValue` edge cases. Full 9-row table pinned with a single `[Theory] [InlineData]`. |
| **F3.Location / Target hemisphere normalisation** | ✓ | New `Tests/HemisphereNormalisationTests.cs` (3 theories × 4 inputs = 12 cases). Each covers the "negative magnitude flips flag" rule for Lat, Lon, Dec independently. The implementation comment at the constructor names "sign takes precedence over the supplied flag" as the rule; the test names it back. |
| **F2.POCO contracts** | ✓ | New `Tests/LocationTests.cs`, `Tests/TargetTests.cs`, `Tests/AltAzTests.cs`, `Tests/NightWindowTests.cs`. Locations pin D/M/S decomposition and `With()` round-trip; Targets pin M31 Default + the (previously-buggy) Dec degree-vs-hour decomposition + `With()` round-trip; AltAz pins ctor and `Deconstruct` order; NightWindow pins `IsValid` across all four endpoint combinations. Each test file's preamble names the historical regression class it guards against. |
| **F3.SunHeliographic Meeus example** | ✓ | `MeeusWorkedExamplesTests.cs:107-121` `SunHeliographic_DiskCenterAt_Meeus_1992Oct13_Matches`. Reference values `(P, B0, L0) = (26.27°, 5.99°, 238.63°)` at 1992-10-13 0h TD, 0.05° tolerance per the helper's documented accuracy budget. Follow-up doc records the Library actual as `(26.2737°, 5.9877°, 238.6479°)` — within ~0.02° of the textbook values, well inside tolerance, confirming the math against an absolute reference rather than only a year-sample self-consistency check. |
| **F4.TestLocations as `[Theory]` source** | ✓ | `TestLocations.cs:105-112` `All()` enumerable returns 5 `(name, Location)` pairs covering Penns Park, Sydney (southern hemisphere east), Quito (equator-degenerate), Reykjavik (polar fringe), McMurdo (polar antarctic). `SmokeTests.AltitudeAtTransit_MatchesMeridianAltitude` now `[Theory] [MemberData(...)]`-driven across all five, verifying the closed-form identity holds at both hemispheres + equator + polar latitudes. The non-Penns-Park fixtures are also reusable for any future cross-location-invariant test. |
| **F5.7 Phase 3 parity** | ✓ + bonus | `ParityFixtures.cs:54-59` defines `BaselineSnapshot` record; lines 126-191 hold the frozen 9-case dictionary captured 2026-05-18. `ParityBaselineTests.cs:84-110, 127-144` add `NightCalculator_MatchesBaseline` and `MoonSeparation_MatchesBaseline` `[Theory]` tests with the exact tolerances I recommended (60 s twilight, 0.005 illumination, 30 arcsec moon-alt, 60 arcsec separation). Polar cases handled via the `MinValue` sentinel; illumination / separation / moon-alt still asserted because they're real values regardless of sun visibility. **Bonus**: `_DumpBaselinesForRegeneration` (`[Fact(Skip=...)]` at line 153) auto-emits the dictionary entries for re-snapshotting after a deliberate behaviour change. The NINA-as-oracle Phase 3 extension is documented as a separate uncertain-cost integration in `followups.md:25-45`. |

---

## Small residual

These are not regressions — they're polish-level items that the refactor session didn't touch because they're not blocking anything. Worth knowing about; none are urgent.

### 1. `SunPosition.ApparentAltitudeAt` XML doc references Bennett but the code uses Saemundsson

`Astronomy.Core/Sun/SunPosition.cs:108, 113`. Commit `f444ef2` switched the underlying formula from Bennett to Saemundsson (the rename is in the commit message: "Saemundsson 1986 refraction, drop Bennett"). The implementation at line 125 now reads `altGeom + Refraction.SaemundssonDeg(altGeom)`. The XML doc on the same method still says:

```
/// Apparent (refraction-corrected) altitude of the Sun in degrees. Adds Bennett's
/// atmospheric refraction to the geometric altitude returned by AltAzAt.
///
/// Bennett's formula is defined for apparent altitude as input; we feed the
/// geometric altitude as the standard approximation (Meeus AA p. 105). Error is
/// below 0.01 deg at all altitudes -- well below tracker precision.
```

That's a docs-vs-code drift. The "input-vs-output convention" caveat in the second paragraph is also stale: Saemundsson IS defined for true (geometric) altitude as input, so the "standard approximation" warning no longer applies — the new formula's input convention matches the call-site verbatim, which is actually a small accuracy win the docstring doesn't mention.

Two-line fix:

```csharp
/// Apparent (refraction-corrected) altitude of the Sun in degrees. Adds Saemundsson 1986
/// atmospheric refraction (via Refraction.SaemundssonDeg) to the geometric altitude
/// returned by AltAzAt.
///
/// Saemundsson is defined for geometric (true) altitude as input -- matches AltAzAt's
/// output convention directly, no approximation pass needed.
```

`MeeusUtility.SaemundssonRefractionDeg` and `Refraction.SaemundssonDeg` themselves already have correct docs; this is the only consumer-site staleness.

### 2. `AltAzCalculator.Of` XML doc references `.ToUniversalTime()` but the code routes through `TimeKindGuard.AsUtc`

`Astronomy.Core/AltAz.cs:79-87`. The body now reads `TimeKindGuard.AsUtc(location.DateTime)` (line 95), which has the same effect as `.ToUniversalTime()` for any `DateTimeKind`, but is now the canonical path per the CLAUDE.md "DateTime kinds" bullet. The docstring still names the older idiom:

```
/// Overload that reads the UTC instant from
/// <c>location.DateTime.ToUniversalTime()</c>. Accepts
/// <see cref="Location.DateTime"/> with any <see cref="DateTimeKind"/>: Local and
/// Unspecified are treated as local and converted via Windows rules; Utc is a
/// no-op.
```

Semantically still accurate; just doesn't tie the reader to the helper's name. A `<see cref="Time.TimeKindGuard.AsUtc"/>` mention (or even just replacing the prose with "routes through `TimeKindGuard.AsUtc`") would keep the docstring discoverable from the helper class.

### 3. A6 single-value sites still inline

12 sites still use the inline `North ? +X : -X` form for single-value extractions (one of lat / lon / dec):

```
Astronomy.Core/Moon/MoonSeparation.cs:63           (lat only)
Astronomy.Core/Night/NightCalculator.cs:55         (lat only)
Astronomy.Core/Session/CoarseVisibility.cs:48      (lat only)
Astronomy.Core/Session/RiseSet.cs:73-74            (lat+dec, no lon/ra)
Astronomy.Core/Session/SessionAltitude.cs:83-84    (lat+dec, no lon/ra)
Astronomy.Core/Session/SessionSolvers.cs:210-211   (lat+dec, no lon/ra)
Astronomy.Core/Session/SessionSolvers.cs:354-355   (lat+dec, no lon/ra)
Astronomy.Core/Session/TransitTime.cs:41           (lon only)
Astronomy.Core/Sun/SunEvents.cs:105                (lon only)
Astronomy.Core/Sun/SunEvents.cs:130                (lat only)
Astronomy.Core/Sun/SunPosition.cs:101              (lon only)
Astronomy.Core/Sun/SunPower.cs:141                 (lat only)
```

The follow-up doc's "applied at 7 multi-value sites" claim matches this — A6 was applied to the full-preamble sites and skipped the single-value ones. Defensible: at a single-value site, `loc.AsSignedDegrees().LatSigned` does the tuple construction work to throw away half the result, and the inline conditional is shorter and equally readable. The cleanup would be:

- Either add `AsSignedLatitude(this Location)` / `AsLonEastDeg(this Location)` / `AsSignedDeclination(this Target)` single-value extension overloads, and route the 12 sites through them; or
- Leave as-is and accept that the convention is enforced at multi-value sites and inline elsewhere.

I'd lean toward the second — single-value extensions add 4-6 extra methods to the extension classes for what amounts to one-line conditional bookkeeping. The multi-value form was the real readability win; the single-value form barely is. **Recommend: leave as-is.** Worth mentioning only because if a future contributor adds a 13th single-value site, they'll naturally reach for the inline form (visible at any of the 12 neighbors) instead of inventing a new extension. The convention is "extension at multi-value, inline at single-value" — and the LocationExtensions / TargetExtensions class-level XML doc could state this explicitly so it doesn't need to be re-discovered.

### 4. `360.985647` sidereal-degrees-per-day literal in two Meeus call sites

`Astrometry/Meeus/SunEphemeris.cs:172` and `Astrometry/Meeus/MoonPosition.cs:390` use the literal `360.985647 * m` for the Meeus chapter-15 GAST-at-instant calculation. This is `15 * SiderealHoursPerSolarDay` to 6 decimals (15 × 24.06570982 = 360.98564730). The A1 centralisation eliminates the sidereal-hours-per-solar-day literal but doesn't reach the sidereal-degrees-per-day expression of the same physical quantity.

Two contained risks:
- If a future refinement updates `SiderealHoursPerSolarDay`'s precision (currently 8 sig fig past the decimal), the `360.985647` literal would drift; the math would silently disagree by tiny amounts.
- It's not obvious to a new reader that these two literals are the same physical quantity.

A `public const double SiderealDegreesPerSolarDay = SiderealHoursPerSolarDay * 15.0` on `SiderealTime` and routing both Meeus call sites through it would close the small remaining gap. Five-line edit; same shape as A1.

Marking as low priority because Meeus's own chapter-15 reference uses the `360.985647` literal verbatim — the citation match is part of the file's audit trail. A reasonable design choice is to leave the Meeus citation literal in place and let the A1 derivation drift be caught by the parity tests if it ever happens. Either approach is defensible; flagging so the choice is conscious.

### 5. New extension classes lack the convention-explicit "single-value sites stay inline" note

`LocationExtensions.cs:5-16` and `TargetExtensions.cs:5-15` document the WHY (replace the per-callsite preamble; AggressiveInlining keeps hot loops at parity) but don't state the WHEN (apply at multi-value sites where the full preamble was unpacked; single-value sites stay inline). Without the explicit note, a contributor refactoring an unrelated method might wonder why some neighbouring sites use the extension and others don't.

One-paragraph addition to the class-level XML doc would close this. Optional; cosmetic.

---

## Items I'd recommend NOT touching

These three temptations come up when you have a recently-passing test suite and a clean diff:

- **Don't add `SiderealDegreesPerSolarDay` mechanically across the codebase.** See Residual #4 — the Meeus citation literal is doing work as a citation. If you do add the constant, route only the *non-Meeus* callers through it, and leave the chapter-15 sites with the literal + a comment naming Meeus 15.4.
- **Don't add single-value extensions** (see Residual #3). The marginal readability is negative.
- **Don't broaden the F5.7 baseline tolerances.** They're tight enough to catch real drift (e.g. a typo in a Meeus coefficient) and loose enough not to fire on platform float-rounding noise. The 60 s twilight + 30 arcsec moonAlt + 0.005 illumination + 60 arcsec separation budget came from the documented accuracy budgets of the underlying formulas; tightening would catch nothing real, loosening would mask real regressions.

---

## What changed in the thread-safety picture

Re-running the same audit as the original review:

- `lock` / `Interlocked` / `Monitor` / `SemaphoreSlim` / `Mutex` / `SpinLock`: zero hits in `Astronomy.Core`. ✓
- `[ThreadStatic]` / `ThreadLocal<T>` / `Lazy<T>` / `ConcurrentDictionary`: zero hits. ✓
- Static state: same 5 items as before — `Bortle.sZenithMag`, `Bortle.sExtinctionK500`, `MoonPosition.mTermsLR`, `MoonPosition.mTermsB`, `VisibilityWindows.ProfileScanStep`. None of the four new helpers (`TimeKindGuard`, `RiseSetMath`, `LocationExtensions`, `TargetExtensions`) introduce state — all are `internal static` pure-function holders. ✓
- `DateTime.Now` / `UtcNow` / `Today`: one hit, `Location.Default` (`Locations/Location.cs:229`). Same as before, still intentional and still documented. ✓

The "thread-safe by construction" claim survives the refactor unchanged. CLAUDE.md's articulation of it (`CLAUDE.md:59-69`) is still accurate.

---

## Things the test suite now catches that it didn't before

A short list, for posterity:

- A Meeus formula edit that produces silently-wrong twilight events: **F5.7 parity baseline** (was: only plausibility-in-range guards).
- A typo in the `Bortle` lookup table: **`BortleTests.Defaults_MatchPublishedTable`** (was: zero coverage).
- A regression in the hemisphere-normalisation rule (e.g. someone "fixing" the sign-takes-precedence logic): **`HemisphereNormalisationTests`** all 12 cases (was: zero coverage).
- A return-tuple field reorder in `RiseSet.NextAtOrAfter` after the record-struct switch: caught by every existing positional destructure call site at compile time (was: would have been a silent semantic shift).
- A Meeus periodic-term coefficient drift in `SunHeliographic.DiskCenterAt`: **`MeeusWorkedExamplesTests.SunHeliographic_DiskCenterAt_Meeus_1992Oct13_Matches`** (was: only "stays in canonical range" + "B0 swing >= 12 deg over a year" — both monotonic / range checks that would pass even for a substantially-wrong constant offset).
- An `AltitudeAtTransit == MeridianAltitude` identity break at southern, equatorial, or polar latitudes: **`SmokeTests`** `[Theory]` over `TestLocations.All` (was: Penns Park only).
- A `NightCache.ComputeYearStartDay` regression to the documented off-by-one: **`NightCacheTests.ComputeYearStartDay_ReducesToFirstOfMonth_PreservingTimeOfDay`** all 5 cases (was: zero coverage; only NightCalculator was tested).
- A `NightCache` cancellation-token semantics break: **`NightCacheTests.Ctor_AlreadyCancelledToken_Throws`** (was: zero coverage).
- A Dec D/M/S regression to the pre-`Target.DecDegrees` hour-vs-degree confusion: **`TargetTests.DecDms_ForM31_MatchesDegreeOfDeclinationDecomposition`** (was: implicit via tests that don't read DMS).
- A `JulianDate.FromUtc` constant drift: **`JulianDateAndSiderealTimeTests.FromUtc_J2000Epoch_MatchesPublishedJulianDate`** (was: caught only indirectly via downstream Meeus tests that have wider tolerances).
- A `SiderealTime.Greenwich` polynomial constant drift: **`Greenwich_AtJ2000_MatchesUSNOConstant`** (was: same as above).

---

## Final read

The refactor session was unusually clean. Every actionable item from the original review landed, two of them with bonuses (A2 fixed a latent silent bug; F5.7 added regeneration ergonomics I didn't think to specify). Test coverage went from the per-public-type baseline plus four Meeus worked examples to that-plus-eleven-new-test-files-with-tight-invariants-pinned. The thread-safety contract is unchanged. The four small residuals above are documentation drift (#1, #2, #5) and one principled deferral (#3, #4) — they're polish, not architecture.

The library is in a strong place to ship into TargetPlanner / XisfManager / IS / ISP / ISS as the post-CoordinateSharp astrometry baseline. The parity baseline gives the consuming apps a quantified drift envelope; the Meeus worked-example tests give them an absolute-reference floor; the comprehensive POCO contract tests give them confidence that hemisphere conventions and immutable-builder semantics won't shift under their feet.
