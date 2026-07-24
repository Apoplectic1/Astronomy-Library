# Proposal: ks-dmag-moon-gate

## Why

The moon-avoidance gate in the placement primitives is an ACP/TS-style Lorentzian — five knobs
(`SeparationDeg`, `WidthDays`, plus a three-parameter Relax zone) crudely approximating what the
Library's own Krisciunas–Schaefer model computes exactly: moon disc intensity (phase), airmass
attenuation (moon altitude), target altitude, and separation. The 2026-05-24 design notes settled
the direction: **replace, don't relax** — "accept this minute if the K-S-predicted sky brightness
at the target is within `ToleranceMag` of the moonless baseline. One scalar tolerance, full physics."

Why now: the 2026-07-24 docs audit re-verified the seam facts, and exploration confirmed the gate
is nearly free — `KsAt` costs ~5.6 ns against the ~1,425 ns moon-position evaluation the gate
already pays per minute, and every input it needs is either already computed in the loop or one
~147 ns sun call away. Three ROADMAP items close in one move:

1. **Partial-moon-impact tolerance** — the gate *is* the tolerance path (small tolerance ≈ hard
   reject; generous tolerance spans moon-lit time the Lorentzian rejects outright).
2. **Refraction asymmetry (~34′/~2 min)** — the gate adopts the K-S apparent-altitude moon
   convention (Saemundsson), matching TP's Sky chart; the Lorentzian's geometric-altitude
   inconsistency dies with the Lorentzian.
3. **TS-style Relax zone** — obsolete; K-S airmass attenuation handles moon-near/below-horizon
   automatically (apparent alt ≤ 0 → zero contribution). All seven shipped TP filter defaults
   already run with Relax off — the feature is dead weight.

## What Changes

- **New K-S Δmag gate** inside `BestSession.MoonClearIntersect` (seam unchanged, internal): the
  1-minute walk + boundary interpolation now runs on `ToleranceMag − Δmag(t)`, where
  `Δmag = ln(1 + bMoon/(bDark + bTwilight)) / 0.92104` — the closed-form brightness increase over
  the moonless baseline, derived from `KsAt`'s existing three-component decomposition (the
  bandwidth scale cancels exactly; twilight deliberately does not — a brighter twilight sky
  genuinely dilutes the moon's relative impact).
- **BREAKING — Lorentzian deleted, no shim** (portfolio no-back-compat rule): `MoonAvoidance`
  statics (`LorentzianRequiredSep`, `RequiredSepWithRelax`, `IsRejected`) and the
  `MoonAvoidanceProfile` shape (5 Lorentzian/Relax fields) are removed. A new minimal profile
  replaces it: `Enabled` + `ToleranceMag` + filter band (`CenterNm`, `BandwidthNm`). Profile
  parameter type changes on `BestSession.For`/`ResolveCandidates` and four `SessionSolvers`
  entry points.
- **Site parameters come from `Location`**, not the profile: `v0Mag` via
  `Bortle.DefaultZenithMag(location.BortleClass)`, band extinction via
  `SkyBrightness.ScaleK(location.ExtinctionK, CenterNm)` — the same assembly TP's Sky chart
  already uses, now inside the gate.
- **Refraction convention**: the gate applies `Refraction.SaemundssonDeg` to the moon altitude
  internally. `MoonSeparation.ObserveAt` keeps returning geometric altitude (CONSUMERS assumption
  #3 unchanged).
- **`MoonAvoidance.DaysInLunarCycle` moves to `LunarAge`** (it is `LunarAge.SynodicMonthDays`'s
  compile-time source today — the dependency inverts before the Lorentzian file dies).
- **BREAKING — TP migrates in a coordinated phase** (sole consumer): `Filter` record drops the
  five Lorentzian/Relax fields and gains `ToleranceMag` (it already carries `CenterNm`/
  `BandwidthNm`); the eleven moon-avoidance UI controls collapse to ~three (enable, tolerance,
  filter strip); `EditFiltersForm` drops six columns; the dead `MoonSweepSample`/
  `NightCacheEntry.MoonSamples` sweep (written every year-pass, read nowhere) is deleted rather
  than ported. Operational heads-up, no migration code: `%APPDATA%\TargetPlanner\filters.json`
  resets — delete it and re-tune from the new defaults.

## Capabilities

### New Capabilities

- `moon-brightness-gate`: the K-S Δmag moon gate's behavioral requirements — acceptance semantics
  (Δmag vs `ToleranceMag`), the apparent-altitude (refraction) convention, site-parameter sourcing
  from `Location`, moonless-baseline definition (twilight in the denominator), boundary
  interpolation accuracy, disabled/null-profile short-circuits, and purity/thread-safety for
  per-target parallelism.

### Modified Capabilities

*(none — `contract-assumption-pinning` gains new registry entries for the gate's semantics, but
its own requirement — every numbered assumption maps to a test or registry entry — is unchanged.)*

## Impact

**Library (Phase 1, one commit):**
- `Astronomy.Core/Moon/MoonAvoidance.cs` — replaced by the new profile + gate math home.
- `Astronomy.Core/Session/BestSession.cs` — `MoonClearIntersect` rework; `For`/`ResolveCandidates`
  signatures. `Astronomy.Core/Session/SessionSolvers.cs` — four public entry points + two private
  helpers. `Astronomy.Core/Moon/LunarAge.cs` — constant moves in.
- Tests: `MoonAvoidanceTests.cs` (24 methods) deleted and replaced by gate tests; 7
  `BestSessionTests` + 1 `SessionSolversTests` reworked. Contract tests **survive unchanged**
  (#3 geometric `ObserveAt`, #4 `KsAt` golden — both become load-bearing for the gate).
- Docs in the same commit: ARCHITECTURE (Moon/Session mechanics, thread-safety POCO list),
  CONSUMERS (pinned surface + new assumptions), ROADMAP (three open items close), CHANGELOG.

**TargetPlanner (Phase 2, coordinated — TP build breaks between phases, so both land in one arc):**
- `Filters/Filter.cs` + `FilterLibrary.cs` (7 builtin rows, `DiffersFromBuiltinDefault`,
  persistence), `State/PlanningPolicy.cs` (`MoonProfile`), `Forms/MainForm.Designer.cs` +
  `MainForm.FilterMenuPresenter.cs` (UI), `Forms/EditFiltersForm.cs`,
  `Caches/ChartCacheStore.cs` (profile re-derivation + three compute helpers),
  `Forms/Presenters/MainForm.SortPresenter.cs` (two `MoonProfile` consumers),
  `Caches/MoonSample.cs` + `NightCacheEntry.MoonSamples` (dead — delete). 7 test files.
- `HdmKey` cache invalidation needs **no design change** — it keys on `Filter` structural
  equality, so the new field set flows through automatically.

**Verification seam:** `..\build-all.ps1` (constellation DRC) proves the cross-repo contract after
both phases; TP UI changes additionally need visual verification by the user.
