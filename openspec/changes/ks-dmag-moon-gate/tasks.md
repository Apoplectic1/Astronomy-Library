# Tasks: ks-dmag-moon-gate

## Phase 1 — Library (one commit)

- [x] 1.1 Move `DaysInLunarCycle` into `LunarAge` (rename to its real home; invert the
      `SynodicMonthDays` alias) — `Astronomy.Core/Moon/LunarAge.cs`, ahead of the deletion.
- [x] 1.2 Add `SkyBrightness.KsMoonDeltaMag(...)` — decomposed Δmag (D1): three components once,
      `ln(1 + bMoon/(bDark + bTwilight))/0.92104`; no bandwidth parameter; `KsAt` untouched.
- [x] 1.3 Add `Astronomy.Core/Moon/MoonLimitProfile.cs` — `Enabled`/`ToleranceMag`/`CenterNm`,
      `With(...)`, singletons `Disabled` / `Narrowband` (1.0, 656) / `Broadband` (0.30, 540),
      `Custom(...)`. Same immutable POCO pattern as `Location`/`Target`.
- [x] 1.4 Rework `BestSession.MoonClearIntersect` (D3): per-minute Δmag via ObserveAt (alt+az) +
      target `AltAzCalculator.At` + `LunarAge`→phase + `SunPosition.AltAzAt`; Saemundsson on moon
      alt; interpolate on `ToleranceMag − Δmag`; fail fast on target-below-horizon input.
- [x] 1.5 Swap profile type on `BestSession.For`/`ResolveCandidates` and
      `SessionSolvers.{LongestDuration, LowestHorizon, LongestDurationCentered,
      LowestHorizonCentered}` (+ private `FitsAt`/`FitsCenteredAt`).
- [x] 1.6 Delete `Astronomy.Core/Moon/MoonAvoidance.cs` (Lorentzian statics + old profile).
- [x] 1.7 Tests: delete `MoonAvoidanceTests.cs` (24); rework the 7 `BestSessionTests` + 1
      `SessionSolversTests` profile tests onto `MoonLimitProfile`; add gate tests per spec
      scenarios (accept/reject, monotonicity, apparent-altitude boundary, twilight dilution,
      site-from-Location, null/Disabled equivalence, interpolation, fail-fast) + the calibration
      anchor pins (NB ≈ 1.0 full-moon, BB ≈ 0.3 half-moon).
- [x] 1.8 Benchmarks: update `HotPathBenchmarks` profile reference; sanity-check gate cost.
- [x] 1.9 Docs, same commit: ARCHITECTURE (Moon/Session mechanics, thread-safety POCO list),
      CONSUMERS (pinned surface swap + new assumptions: gate refracts internally, bandwidth
      independence, Location-derived site params), ROADMAP (close partial-moon + refraction-
      asymmetry items; note the Lorentzian finding), CHANGELOG entry.
- [x] 1.10 Verify: full mixed build (msbuild -restore) + all five test projects green; commit.

## Phase 1.5 — TP direction note (committed to TP before any TP code)

- [x] 2.1 Write `TargetPlanner/docs/<date>-ks-dmag-migration.md`: Library change summary, Filter
      record new shape (drop 5, add `ToleranceMag`; keep CenterNm/BandwidthNm), builtin defaults
      per family (NB 1.0: H/O/S; BB 0.30: L/R/G/B), UI collapse map (11 controls → enable +
      tolerance spinner + filter strip), EditFiltersForm 9→4 columns, `ToProfile()` sites
      (PlanningPolicy:76, ChartCacheStore:515, FilterMenuPresenter:117/:384), SortPresenter
      MoonProfile consumers (:248/:269), delete dead `MoonSweepSample`/`NightCacheEntry.MoonSamples`
      sweep (ChartCacheStore:660-694), `filters.json` delete-and-retune step, expected chart
      behavior shift (BB stricter near full moon — physics, not regression).
- [x] 2.2 One-line pointer in TP `CLAUDE.md` open follow-ups; commit to TP.

## Phase 2 — TP migration (separate session, guided by the note)

- [ ] 3.1 `Filter` record 9→5 fields + `ToProfile()`; `FilterLibrary` builtins + `DiffersFromBuiltinDefault`.
- [ ] 3.2 `PlanningPolicy.MoonProfile` type swap; `ChartCacheStore` re-derivation + 3 helpers;
      `SortPresenter` call sites.
- [ ] 3.3 UI: Designer controls collapse; `FilterMenuPresenter` scrub/write/enable methods;
      `EditFiltersForm` columns.
- [ ] 3.4 Delete dead moon sweep; fix stale `MoonSample.cs` docstring by deletion.
- [ ] 3.5 TP tests (7 files) onto the new shape; delete `filters.json`; retune.
- [ ] 3.6 Verify: `..\build-all.ps1` green end-to-end; user visual verification of UI + charts;
      TP docs (ARCHITECTURE/CHANGELOG/README mentions) updated; commit.
