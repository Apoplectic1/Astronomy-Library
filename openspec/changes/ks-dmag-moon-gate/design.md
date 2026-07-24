# Design: ks-dmag-moon-gate

## Context

`BestSession.MoonClearIntersect` (internal, `BestSession.cs:402`) walks each visibility window at
1-minute cadence, evaluating the ACP/TS Lorentzian (`MoonAvoidance.RequiredSepWithRelax`) and
emitting the accepted sub-intervals with linear-interpolated boundaries. `SessionSolvers` threads
the same profile through four public entry points. TP is the sole consumer.

Exploration facts the design leans on (2026-07-24, cited in the change log):

- `MoonSeparation.ObserveAt` already returns `(SeparationDeg, MoonAltDeg, MoonAzDeg)` — the third
  element was added *for* K-S callers. One `MoonPosition.Topocentric` pass per call (~1,425 ns).
- Phase angle is free: the loop already calls `LunarAge.DaysAt` per minute;
  `SkyBrightness.PhaseAngleDegFromAgeDays` is pure arithmetic.
- `KsAt` (~5.6 ns) computes three independent nanolambert components (`bDark`, `bTwilight`,
  `bMoon`) combined only at the final line (`SkyBrightness.cs:205`) — a Δmag decomposition is
  structurally trivial. The bandwidth scale cancels in the Δ; twilight does not (by design).
- `Location` carries `BortleClass` + `ExtinctionK` (k at 500 nm); `Bortle.DefaultZenithMag` and
  `SkyBrightness.ScaleK` are the existing conversions. TP's Sky chart assembles KsAt inputs
  exactly this way (`AltitudeSubChart_Sky.cs:343-348`).
- `SunPosition.AltAzAt` is ~147 ns (low-precision Meeus ch. 25) — the only new physics call.
- `LunarAge.SynodicMonthDays` aliases `MoonAvoidance.DaysInLunarCycle` (compile dependency).
- Contract tests #3 (`ObserveAt` geometric) and #4 (`KsAt` 10-param golden) survive unchanged.

## Goals / Non-Goals

**Goals**
- One-scalar, full-physics moon gate: accept a minute iff `Δmag(t) ≤ ToleranceMag`.
- Resolve the refraction asymmetry (gate adopts K-S apparent-altitude convention).
- Delete the Lorentzian + Relax machinery entirely (no shim; TP migrates in the same arc).
- Keep the seam (`MoonClearIntersect`), the 1-min cadence, the boundary interpolation, and the
  thread-safety story (pure statics, immutable profile) intact.

**Non-Goals**
- No quality-*weighted* penalty path (the gate is still binary accept/reject per minute; the
  tolerance is what makes it a "partial-moon" policy). A weighted integration into
  `IntegratedQuality` remains future work if ever wanted.
- No lunar-twilight-glow modeling (~10-15 min post-moonset; negligible, per the design notes).
- No change to `VisibilityWindows` (stays moon-blind) or to the `PlaceBest` sin(alt) fast path
  (CONSUMERS #11).
- No Garstang/Falchi urban in-scatter work (parked in ROADMAP).

## Decisions

### D1. Gate math — Δmag via a decomposed K-S evaluation

`Δmag(t) = NoMoonMag(t) − WithMoonMag(t) = ln(1 + bMoon/(bDark + bTwilight)) / 0.92104`

Implemented as a new `SkyBrightness.KsMoonDeltaMag(...)` (name final at implementation) that
computes the three components once and returns the Δ directly — **not** two `KsAt` calls (avoids
double component evaluation and the NaN-below-horizon path) and **not** a caller-side subtraction.
`KsAt` itself is untouched (contract #4 golden preserved). The bandwidth parameter is omitted from
the Δ function's signature — it cancels; taking it would imply sensitivity that doesn't exist.

*Alternative considered:* two `KsAt` calls (with/without moon) — rejected: 2× cost for nothing and
an awkward `moonAltDeg = -5` sentinel call; the decomposition is the honest shape.

### D2. Profile shape — `MoonLimitProfile`, minimal

```csharp
public sealed class MoonLimitProfile   // immutable, With(...), same POCO pattern
{
    bool   Enabled;
    double ToleranceMag;   // accept minute iff Δmag <= ToleranceMag
    double CenterNm;       // band center — drives ScaleK + twilight scaling
}
```

- **Renamed** (not `MoonAvoidanceProfile` reshaped): the semantics inverted — the old type said
  "how far must the moon be", the new one says "how much sky brightening is acceptable". A stale
  mental model reading old code/notes should hit a compile error, not a silently different meaning.
- **No `BandwidthNm`** — cancels in Δmag (D1). TP's `Filter` keeps its `BandwidthNm` for the Sky
  chart; the profile projection just doesn't carry it.
- **No site fields** — `v0Mag` and band-k derive inside the gate from the `Location` already
  passed to `MoonClearIntersect`: `Bortle.DefaultZenithMag(location.BortleClass)` +
  `ScaleK(location.ExtinctionK, CenterNm)`. Site truth stays on `Location` (single source).
- Singletons: `Disabled`, `Narrowband` (ToleranceMag 1.0, CenterNm 656), `Broadband`
  (ToleranceMag 0.30, CenterNm 540), plus `Custom(...)` — values from the calibration below.

*Alternative considered:* keeping the `MoonAvoidanceProfile` name — rejected per above; the
constellation-wide compile break is deliberate and the DRC catches every site.

### D3. Gate evaluation per minute (inside `MoonClearIntersect`)

Per sample: one `ObserveAt` (sep unused by the gate math but `MoonAltDeg`/`MoonAzDeg` are), one
`AltAzCalculator.At` for target alt/az, `LunarAge.DaysAt` → `PhaseAngleDegFromAgeDays`, one
`SunPosition.AltAzAt`, then `KsMoonDeltaMag`. Moon altitude is refraction-lifted first:
`moonAltApparent = MoonAltDeg + Refraction.SaemundssonDeg(MoonAltDeg)` — the K-S/Sky-chart
convention (resolves the asymmetry; `ObserveAt` stays geometric, contract #3 untouched).
Boundary interpolation runs on `g(t) = ToleranceMag − Δmag(t)` — same crossing logic, same
few-second accuracy claim. Target-below-horizon inside a visibility window is a contract
violation of the visibility input → fail fast (consistent with the audit's rule-#16 posture),
not a silent skip.

Cost: adds ~150 ns/sample on a ~1,500 ns baseline (~10%) — no cadence change needed.

*Alternative considered:* precompute a `MoonEphemeris.Sample` night-grid (target-independent,
~5 µs/sample amortized across targets) — deferred; the per-target loop is already paid and the
10% delta doesn't justify restructuring the seam. Noted as a future optimization if TP's
per-target parallel sweep ever profiles hot.

### D4. Calibration — A-anchored, B-sanity-checked (computed 2026-07-24, real `KsAt`)

Method: at the Lorentzian's own accept/reject boundary (required separation at a given lunar age),
compute the actual K-S Δmag over a geometry grid (moon alt × target alt × feasible Δaz), Bortle 5,
k₅₀₀ = 0.28, sun −18°. The Δmag at the boundary is the tolerance that reproduces that boundary.

| Regime | Lorentzian boundary | K-S Δmag at boundary |
|---|---|---|
| NB 60°/7d, full moon | 60° | 1.45–1.68 |
| NB, waxing gibbous | 48° | 0.99–1.06 |
| NB, half moon | 30° | 0.50–0.61 |
| NB, crescent | 18.5° | 0.14–0.17 |
| BB 120°/14d, full moon | 120° | 1.65–2.03 |
| BB, gibbous | 113° | 0.85–1.12 |
| BB, half moon | 96° | 0.29–0.47 |
| BB, crescent | 77° | 0.06–0.10 |

*(Corrected during implementation 2026-07-24: the first cut of this table mislabeled the NB
gibbous cluster as "full moon" — a truncated calibration readout. The pinning test caught it.)*

**Finding (B check): the Lorentzian was never an iso-quality contour.** Its implied sky-quality
tolerance varies ~10–30× across the lunar cycle — needlessly strict at crescent (rejecting skies
brightened by only ~0.1 mag) and permissive at full moon (NB: ~1.6 mag ≈ 4.4× sky; BB: ~1.7–2.0
mag ≈ **5–6× the integration time** for background-limited imaging). A single K-S tolerance
replaces that wobble with a constant sky-quality guarantee — this *is* the improvement, so full
A-equivalence across the cycle is neither possible nor desirable.

**Chosen defaults — both are the Lorentzian boundary's cycle-median Δmag:**
- **Narrowband: `ToleranceMag = 1.0`** (cycle median 0.99). B check: sky ×2.5 ≈ 2.5×
  integration — reasonable for emission-line targets whose signal doesn't scale with sky. The
  classic full-moon rule implied ~1.6 (sky ×4.4); a user wanting classic full-moon Hα behavior
  raises the per-filter tolerance toward 1.6.
- **Broadband: `ToleranceMag = 0.30`** (cycle median 0.300). B check: sky ×1.32 ≈ 1.3×
  integration — a defensible broadband budget; the full-moon-implied ~1.7–2.0 fails B outright.
- Consequence, stated plainly: **near full moon the gate is stricter than the old rule for both
  families** (moderately for NB, strongly for BB) and more permissive at half/crescent. Charts
  will show fewer full-moon fits and more crescent fits; that is the physics, not a regression.
- Per-filter override in TP's `EditFiltersForm` (Tolerance column) — defaults are starting points.
- Calibration pinning tests record the NB full-moon boundary (~1.63, above the shipped default —
  the strictness relationship) and the NB gibbous / cycle-median anchor (~1.0) plus the BB
  half-moon anchor (~0.32), so the anchors are reproducible if `KsAt` internals ever change.

### D5. Deletion order + the constant

`DaysInLunarCycle` moves into `LunarAge` (as `SynodicMonthDays`'s real home, inverting today's
alias direction) **before** `MoonAvoidance.cs` is deleted. `MoonLimitProfile` lands in
`Astronomy.Core/Moon/MoonLimitProfile.cs`; the gate math in `SkyBrightness` (it is brightness
physics, not moon geometry).

### D6. TP phase (coordinated, same arc) + the in-repo direction note

TP's build breaks the moment Phase 1 lands. **First Phase-1 task after the break is created:
write `TargetPlanner/docs/2026-XX-XX-ks-dmag-migration.md`** (committed to TP) carrying: what
changed in the Library, the Filter record's new shape (drop 5 Lorentzian/Relax fields, add
`ToleranceMag`; keep `CenterNm`/`BandwidthNm`), the UI collapse (11 controls → enable + tolerance
+ filter strip), the `EditFiltersForm` column changes, the two `ToProfile()` call sites, the
`SortPresenter` `MoonProfile` consumers, the dead `MoonSweepSample`/`NightCacheEntry.MoonSamples`
sweep to delete (not port), the `filters.json` delete-and-retune step, and the new builtin
defaults (NB 1.0 / BB 0.30 per family). TP's own CLAUDE.md gets a one-line pointer. A future TP
session that opens to a red build finds its map in-repo.

## Risks / Trade-offs

- [BB behavior shift near full moon: fewer accepted windows than the Lorentzian] → deliberate
  (D4); called out in the TP note + CHANGELOG so chart differences aren't mistaken for a bug.
  Per-filter tolerance is user-tunable the moment it surprises.
- [Twilight in the Δ denominator makes the gate more permissive in twilight] → physically correct
  (moon matters less against a twilight sky); noted in the spec so a test doesn't "fix" it.
- [K-S near-moon regime (sep < ~10°) is outside K-S validity] → the gate inherits the documented
  `SkyBrightness` caveat; at any sane tolerance those minutes reject anyway (Δmag is enormous).
- [Urban low-altitude K-S overdrive (open ROADMAP item) now feeds a *gate*, not just a chart] →
  overdrive *dims* predicted sky at low alt → *under*-states Δmag → gate too permissive below
  ~10° target altitude at high-k sites. TP's existing 10° floor policy covers the practical
  range; noted in the spec as a known limit tied to the parked Garstang item.
- [`filters.json` silently resets / partially binds (`ToleranceMag` → 0.0 = maximally strict)] →
  operational note (delete the file); TP loader's silent-fallback behavior is existing design.

## Migration Plan

1. **Phase 1 (Library, one commit):** constant move (D5) → `MoonLimitProfile` + `KsMoonDeltaMag`
   → `MoonClearIntersect` rework (D3) → `BestSession`/`SessionSolvers` signatures → delete
   `MoonAvoidance.cs` → tests (delete 24, rework 8, add gate + calibration tests) → docs
   (ARCHITECTURE, CONSUMERS pinned surface + new assumptions, ROADMAP closes 3 items, CHANGELOG).
2. **Write the TP direction note** (D6) — committed to TP immediately after Phase 1, before any
   TP code changes.
3. **Phase 2 (TP):** per the note; ends with `..\build-all.ps1` green + user visual verification
   of the collapsed UI and chart behavior.

Rollback: single-commit phases; `git revert` per repo. No data migration in either direction
(catalog/settings untouched; `filters.json` regenerates from builtins either way).

## Open Questions

- Final name bikeshed at implementation: `MoonLimitProfile` vs `MoonBrightnessProfile`;
  `KsMoonDeltaMag` vs `MoonDeltaMag`. (Shape is settled; naming left for the code review.)
- Whether the calibration pinning test should also pin the BB half-moon anchor (leaning yes,
  cheap).
