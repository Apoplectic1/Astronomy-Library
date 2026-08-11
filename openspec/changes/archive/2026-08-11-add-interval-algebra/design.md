# add-interval-algebra — design

## Context

See `proposal.md` — Why. Current state that shapes the design: three public producers return
`IReadOnlyList<(DateTime Start, DateTime End)>`; `BestSession` additionally exposes the same
tuple currency in its public surface (`ResolveCandidates` returns it, `PlaceBest`/`PlaceCentered`
accept it as candidate lists) and hand-rolls intersection in the internal `MoonClearIntersect`.
Portfolio constraints: UTC-kind discipline at boundaries (`TimeKindGuard` convention),
collection returns are `IReadOnlyList<T>` (TP CLAUDE.md convention), fail-fast on contract
violations (global rule #16), no back-compat (global rule #15).

## Goals / Non-Goals

**Goals:**

- One interval currency across `Astronomy.Core`'s public surface — producers *and* the
  `BestSession` candidate-list parameters/returns, so no tuple interval API survives.
- Set operations sufficient for a weighted-interval solver's inner loop (IS): intersect,
  union, subtract, clip.

**Non-Goals:**

- Domain-specific clippers (max-altitude, flip-window) — later changes bring them with their
  features (decision recorded in proposal).
- Touching `MoonClearIntersect`'s moon-physics sampling, `QualitySamples`, or `AltitudeCurve`
  (samples, not intervals).
- A self-validating set wrapper type (see Decisions — rejected alternative).

## Decisions

1. **Type: `readonly record struct UtcInterval(DateTime Start, DateTime End)` in
   `Astronomy.Core.Time`.** The name carries the kind contract the way `TimeKindGuard` does;
   the ctor throws on non-UTC kinds and `End <= Start` (fail-fast, no normalization — a stray
   Local kind is a caller bug, not an input to fix). Struct over class (TS uses a class):
   value semantics, non-nullable, allocation-free in solver loops. Convenience members:
   `Duration`, `Contains(DateTime)`, `Overlaps(UtcInterval)`.
   *Alternative considered:* reusing the tuple with extension methods — rejected: no invariant
   enforcement, no discoverability, and the point is one named currency.

2. **Ops: static class `Intervals` in `Astronomy.Core.Time`, over `IReadOnlyList<UtcInterval>`.**
   `Intersect(a, b)`, `Union(a, b)`, `Subtract(a, b)`, `Clip(list, bound)` — pure functions
   returning `IReadOnlyList<UtcInterval>` per the collection convention. Each op validates the
   ordered-disjoint-merged invariant on inputs and throws on violation (O(n) at n < ~10 per
   night — free; converts a latent producer bug into a loud failure per rule #16).
   *Alternative considered:* an `IntervalSet` wrapper enforcing the invariant by construction —
   rejected as ceremony at this scale; the 2026-05-18 review's C2 follow-up already found
   type gymnastics outweigh the win here.

3. **Half-open semantics `[Start, End)`.** Touching intervals intersect to nothing and union
   into one — no epsilon handling, no double-counted boundary instant. The producers' existing
   dusk/dawn boundary *placement* (`Max`/`Min` at window edges) is unchanged; only the
   representation's edge convention is now explicit.

4. **Producer convergence is value-identical.** `VisibilityWindows.For`,
   `MoonSeparation.IntervalsAboveDeg`, `SunSeparation.IntervalsBelowDeg`,
   `BestSession.ResolveCandidates` (+ candidate-list parameters) swap element type only; the
   same instants come back. Existing tests convert their expectations mechanically —
   byte-identical interval values is the review bar for the swap commits.

5. **Six-case coverage by construction, six-case tests by citation.** Generic half-open
   subtraction is total — TS's explicit `throw` on unhandled geometry has nothing to guard.
   The test suite still enumerates the six `MaximumAltitudeClipper` relative positions
   explicitly (named for the TS reference) so the correspondence is pinned, plus
   property-style tests (union/intersect duality, subtract-then-union round trip).

## Risks / Trade-offs

- [Input validation in every op re-walks lists] → n is single-digit per night per constraint;
  if a profiled hot loop ever cares, relax to debug-only *then*, with evidence.
- [Latent invariant violation in an existing producer surfaces as a new throw] → that is the
  desired fail-fast behavior; the swap commits run the full Core test suite, so any such case
  surfaces at implementation time, not at a consumer.
- [TP/TSM/XFM build against AL via project reference and see a signature change] → *(corrected
  during apply, 2026-08-11)*: TP's `ChartCacheStore` **does** call `ResolveCandidates` /
  `PlaceBest` / `PlaceCentered` — but strictly pass-through (`var` + `.Count` + `.Start`/`.End`
  property reads, names `UtcInterval` matches), so recompile-only held. Verified empirically:
  TP, TSM, and XFM all rebuilt clean with zero edits. The producers themselves have no external
  call sites. TP CLAUDE.md's convention wording updates when TP next touches AL surface.

## Migration Plan

Single change, no staging: type + ops land first (additive), producer/BestSession convergence
rides the same change behind them, docs (`ARCHITECTURE.md` API tour) ride the final commit.
AL publishes before IS's first cut per the portfolio release gate. Rollback = revert the
change; no persisted data is involved.

## Open Questions

None — naming, semantics, and scope were decided 2026-08-11 with the user (converge producers;
subtract subsumes the clipper; no wrapper type).
