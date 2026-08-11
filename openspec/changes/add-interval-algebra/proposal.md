# add-interval-algebra

## Why

`Astronomy.Core` produces UTC time intervals from three public APIs (`VisibilityWindows.For`,
`MoonSeparation.IntervalsAboveDeg`, `SunSeparation.IntervalsBelowDeg`) as bare
`(DateTime Start, DateTime End)` tuples with no shared type and no set operations, and
`BestSession.MoonClearIntersect` hand-rolls its own intersection internally (the deferred C2
finding from the 2026-05-18 library review — unification was premature then for lack of a
consumer). The IntervalScheduler plugin (IS) is that consumer now: its solver's inner loop is
interval set algebra — visibility ∩ moon-ok ∩ night ∩ same-pier-side, minus allocated time —
and it is item 1 of the IS ROADMAP's "AL gaps to close for IS" (IS repo, commit `fab51d9`).
AL releases before IS can cut its first version, so this lands first.

## What Changes

- New public interval type in `Astronomy.Core.Time` — an immutable UTC interval value type with
  the portfolio's UTC-kind discipline enforced at construction (fail fast on non-UTC kinds, per
  the `TimeKindGuard` convention).
- New public set-operation API over ordered interval lists: intersect, union, subtract, and
  clip-to-bounds. `Subtract(window, forbiddenSpan)` returns 0–2 intervals and generically covers
  all six cases of TS's `MaximumAltitudeClipper` reference pattern (the unimodal forbidden-band
  clip); no domain-specific clipper wrappers ship in this change (decision 2026-08-11 — they
  arrive with the meridian-flip/pier-side work that needs them).
- **BREAKING** — the three public producers (`VisibilityWindows.For`,
  `MoonSeparation.IntervalsAboveDeg`, `SunSeparation.IntervalsBelowDeg`) change return type from
  `IReadOnlyList<(DateTime, DateTime)>` to the new interval type (decision 2026-08-11: converge
  on the target state, no back-compat, per portfolio rule). Verified blast radius: Library-internal
  callers only (`BestSession`, `CoarseVisibility`, `SessionSolvers`, tests) — TP/TSM/XFM do not
  call these three APIs directly.
- `BestSession.MoonClearIntersect`'s interval bookkeeping rides on the shared type internally
  (its moon-physics sampling is untouched — it is a sampled predicate sweep, not pure algebra).

## Capabilities

### New Capabilities

- `interval-algebra`: the UTC time-interval value type and set operations (intersect, union,
  subtract, clip) that `Astronomy.Core`'s interval producers return and downstream solvers
  (IS's weighted-interval scheduler) compose.

### Modified Capabilities

<!-- none — openspec/specs/ is empty; the producers' behavior is unchanged (same intervals,
     new return type), and no existing spec documents the tuple shape. -->

## Impact

- **Code**: `Astronomy.Core` (`Time/`, `Session/VisibilityWindows.cs`, `Moon/MoonSeparation.cs`,
  `Sun/SunSeparation.cs`, `Session/BestSession.cs` internals) + `Astronomy.Core.Tests`.
- **Consumers**: TP/TSM/XFM recompile cleanly (no direct call sites); IS/ISM are the intended
  new consumers. Doc convention carry-over: TP `CLAUDE.md` documents the `IReadOnlyList<T>`
  collection-return convention naming these APIs — its wording updates when TP next builds
  against the new AL.
- **Release**: AL publishes a new version before IS's first cut (portfolio AL-first gate).
- **Docs**: AL `ARCHITECTURE.md`/`CLAUDE.md` API tour rows; IS dossier's shipped-surface list
  gains the new entries at its next delta pass.
