# Library Review — Execution and Follow-ups (2026-05-18)

Companion to `docs/2026-05-18-library-review.md`. Captures what was actioned during the 2026-05-18 implementation session, what was deliberately deferred, and the concrete plan for the items that still need work.

## Execution summary

The review's 9-case ParityFixtures table plus the A1-A7, B1-B5, C1-C4, D1-D2, E1-E3, and F2-F5.7 categories were worked end-to-end in 21 commits on the `dev` branch. The diff against branch-point `f444ef2` adds two internal helper classes (`TimeKindGuard`, `RiseSetMath`), two extension classes (`LocationExtensions.AsSignedDegrees`, `TargetExtensions.AsSignedRaDec`), the F5.7 frozen baseline snapshots in `ParityFixtures.Baselines`, and ~114 net new test cases (324 → 438 passing). Zero production behaviour regressions. The Sun-family `EnsureUtc` silent-bug class was fixed as a side benefit of A2.

Reference: `git log f444ef2..HEAD --oneline` on `dev`.

## Resolved post-publication

### SunHeliographic 1992 worked example (F3)

Resolved 2026-05-18 via user-supplied Meeus AA reference values (chapter "Ephemeris for Physical Observations of the Sun", Carrington's formulas) cross-verified against PyMeeus's `Sun.ephemeris_physical_observations` and the soniakeys/meeus Go port: `(P, B0, L0) = (26.27, 5.99, 238.63)` deg at 1992-10-13 0h TD. Test landed in `Astronomy.Core.Tests/Tests/Astrometry/MeeusWorkedExamplesTests.cs` as `SunHeliographic_DiskCenterAt_Meeus_1992Oct13_Matches`, using a 0.05° tolerance (matches the helper's documented accuracy budget; the UT-vs-TT epoch interpretation costs <0.001° for the ~58s ΔT in 1992). Library actual: P=26.2737°, B0=5.9877°, L0=238.6479° — well inside tolerance, confirming the helio math is correct against an absolute reference rather than just monotonically self-consistent across a year sweep.

## Deferred items

### D2 — `RiseSet.NextAtOrAfter` to `record struct`

Deferred per the review's own framing. The current `(RiseSetState State, DateTime? Rise, DateTime? Set)` tuple-return is consumed positionally at downstream call sites; promoting to a named record carries a public-API break risk that wasn't worth absorbing in this session. Revisit when the next public-API revision window opens.

### F5.7 cross-implementation parity oracle (Phase 3, NINA-as-oracle)

The 2026-05-18 F5.7 commit (`4973cf7`) frozen the post-CoordinateSharp Library output as a self-snapshot baseline — sufficient to catch downstream drift but not to verify against an independent implementation. Promoting the parity test to assert "Library matches NINA within tolerance" requires running NINA's astrometry code over our 9 fixtures.

**Integration scope (estimated 30-60 min, several uncertain points):**

1. **Tool project.** Create `tools/NinaParityExtract/NinaParityExtract.csproj` (net10.0-windows, OutputType=Exe). Add `<ProjectReference Include="..\..\..\NINA\NINA.Astrometry\NINA.Astrometry.csproj" />` plus the same project's transitive references (`NINA.Core`, `NINA.Profile`).

2. **Native co-location.** `NINA.Astrometry` P/Invokes `NOVAS31.dll` (the C++ NOVAS31 sub-project at `NINA/NOVAS31/`). The tool's output directory needs `NOVAS31.dll` co-located. NINA's own build copies it via the `NOVAS31` vcxproj output; the tool can either:
   - Add a build event copying from `..\..\NINA\NOVAS31\Output\x64\Debug\NOVAS31.dll`, or
   - Reference NINA's `Output\x64\` as an extra library search path.

3. **Avoid `NighttimeCalculator`'s `IProfileService`.** `NighttimeCalculator.Calculate` requires an `IProfileService`. Skip it and call the underlying `AstroUtil.GetNightTimes(referenceDate, lat, lon, elevation)`, `GetMoonRiseAndSet(...)`, `GetMoonIllumination(date, ObserverInfo)`, `GetMoonAltitude(date, ObserverInfo)` directly. The `ObserverInfo` constructor is pure POCO.

4. **Separation computation.** NINA doesn't have a public Target-Moon separation method. Compute manually: target's RA/Dec → Alt/Az via `AstroUtil` (NOVAS path), moon's Alt/Az via `GetMoonAltitude` + `GetMoonAzimuth`, then angular distance via spherical-law-of-cosines or haversine. Or extract NINA's internal computation if `MoonSeparation`-style helper exists in NINA's `MoonInfo`.

5. **Emit and integrate.** Run the tool, capture C# initializers (one `NinaBaselineSnapshot` per fixture) to stdout, paste into a new `ParityFixtures.NinaBaselines` dictionary. Tighten `ParityBaselineTests` with new `*_MatchesNinaBaseline` `[Theory]` assertions using the same tolerances (60 s twilight, 30 arcsec moon, 0.005 illumination, 60 arcsec separation).

**Risk points:** native-DLL co-location is the most likely first stumble; the `IProfileService`-avoidance route should be straightforward since `AstroUtil` is largely pure-functional. The WPF dependency in `NINA.Astrometry.csproj` (`UseWPF=true`) should not impede a console app referencing it, as long as the tool never instantiates WPF types.

**Alternative oracle (lower-fidelity):** NOAA Solar Calculator (`gml.noaa.gov/grad/solcalc`) and USNO Astronomical Applications API provide rise/set/twilight for arbitrary lat/lon/date. Manual or scripted lookup for the 9 fixtures would give cross-source twilight baselines without the NINA integration cost; less rigorous than a code-parity check, but verifies the right order of magnitude.

## Intentional non-actions

Per the original review, the following were considered and deliberately left alone:

- **C2** (`MoonSeparation.IntervalsAboveDeg` vs `BestSession.MoonClearIntersect` shared `IntervalSweep`) — premature unification, generic over observation type adds type gymnastics that outweigh the readability win.
- **D1** (`TargetGeometry.HourAngleAtAltitude` parameter order vs siblings) — defensible by the "input drives the signature" rule.
- **E2** (`XisfFile` `Dispose` + finalizer body collapse to `CloseNative()`) — cosmetic only.
- **C4** (`LunarAge.DaysAt` throws on non-Utc) — documented as the deliberate exception to the [TimeKindGuard](Astronomy.Core/Time/TimeKindGuard.cs) lenient canonical (see CLAUDE.md "DateTime kinds" bullet).
