# add-meridian-primitives — design

## Context

See `proposal.md` — Why. Existing machinery this composes: `SiderealTime.Local(utc, lonDegEast)`
(LST in hours), `Target.AsSignedRaDec()` / `Location.AsSignedDegrees()`,
`TransitTime.UtcAtOrAfter(target, location, after)` (analytic LST=RA inversion), and
`UtcInterval` / `Intervals` from `add-interval-algebra`. Conventions in force: UTC-kind
normalization at public boundaries (`TimeKindGuard.AsUtc` — the producers' pattern),
`IReadOnlyList<T>` collection returns, canonical-list contract with fail-fast validation.

## Goals / Non-Goals

**Goals:**

- The five primitives from the spec, as pure composition — no new astronomy.
- Sky-side vocabulary end to end (`MeridianSide.East/West`).

**Non-Goals:**

- Placement strategies (finish-east / finish-west / flip-cost) — IS solver policy.
- Pier-side mapping, mount limits beyond a single track-past allowance, counterweight
  geometry — the NINA adapter's and mount's domain.
- Lower culminations (anti-transits). Nothing schedules on them; `TargetGeometry` already
  exposes `LowerCulminationAltitude` for the geometry question.

## Decisions

1. **One static class `Meridian` in `Astronomy.Core.Session`**, beside `TransitTime`:
   `HourAngleAt(target, location, utc) → double`, `SideAt(target, location, utc) → MeridianSide`,
   `TransitsIn(target, location, UtcInterval) → IReadOnlyList<DateTime>`,
   `FlipTimeIn(target, location, UtcInterval session, TimeSpan trackPastMeridian) → DateTime?`,
   `SplitAtFlip(target, location, IReadOnlyList<UtcInterval> windows, TimeSpan trackPastMeridian)
   → IReadOnlyList<UtcInterval>`. *Alternative:* folding into `TransitTime` — rejected; that
   class is a single analytic inversion, this is the scheduling-facing surface over it.

2. **`HourAngleAt` = normalize(LST − RA) to `[-12, +12)`.** Half-open at +12 mirrors the
   half-open interval convention; `SideAt` is `HourAngleAt < 0 ? East : West`, making the
   transit instant itself West — consistent with `[transit, …)` sessions being post-flip.

3. **`TransitsIn` iterates `TransitTime.UtcAtOrAfter`** from `window.Start`, stepping past each
   found transit by one tick, until at/after `window.End`. Bounded by ~⌈duration / sidereal
   day⌉ + 1 iterations; no closed-form enumeration needed.

4. **`FlipTimeIn` searches transits in `[session.Start − allowance, session.End − allowance)`**
   and returns the first `transit + allowance` — this is what makes the pre-session-transit /
   in-session-flip case fall out correctly instead of being an edge case. Negative allowance
   is legal arithmetic (flip-before-meridian mounts), documented, untested against a mount —
   the math is the same either way.

5. **`SplitAtFlip` validates input through the same canonical gate as `Intervals`** —
   `Intervals.RequireCanonical` goes `private` → `internal`. Splitting preserves total time
   exactly (pieces meet at flip instants). *(Amended during apply, 2026-08-11:)* the analytic
   LST inversion carries ~0.1 ms floating-point jitter across recomputations, so "flip exactly
   on a boundary" is unrealizable at tick precision — and IS's replanning path re-splits
   already-split windows, where jitter would emit ~100 µs sliver pieces. A split is therefore
   suppressed when either resulting piece would be under a **one-second tolerance** (far above
   jitter, far below any scheduling quantum). This is numerical-noise semantics like the
   producers' zero-length guards, not a contract-violation fallback.

7. **The interval algebra's "merged" input rule is relaxed to "touching allowed"**
   *(amended during apply, 2026-08-11 — edits `add-interval-algebra`'s unarchived spec delta
   in place)*: `SplitAtFlip`'s output pieces touch at flip instants by construction and are
   semantically distinct (different sides) — coalescing them would be wrong, so touching
   lists are a legitimate currency the algebra must accept. The canonical contract is now
   ordered + pairwise disjoint; "no overlap or touch" remains as `Union`'s *output*
   guarantee. Transit-search seeds also step by one minute rather than one tick
   (`TransitsIn`, `SplitAtFlip`) — a one-tick advance stutters on LST jitter, re-finding
   the same transit repeatedly.

6. **UTC handling follows the producers:** `AsUtc` normalization on bare `DateTime` inputs
   (`HourAngleAt`/`SideAt`); `UtcInterval` inputs are already gated by construction.

## Risks / Trade-offs

- [`TransitTime.UtcAtOrAfter` precision near interval edges: a transit exactly on
  `window.End` must be excluded] → half-open comparison (`< End`) and one-tick stepping; the
  two-transit 24h test pins the boundary behavior.
- [Sky-side naming will surprise a reader expecting ASCOM pier side] → the enum's XML docs
  state the mapping explicitly and name the inversion trap; the adapter owns the translation.

## Migration Plan

Additive, single change. Docs (core.md Session row, CHANGELOG) ride the code commit —
no MAINTAIN conflict this time.

## Open Questions

None.
