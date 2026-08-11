# add-meridian-primitives

## Why

IS ROADMAP "AL gaps to close for IS" item 2 (the last unshipped library-shaped entry from the
scheduler dossier's gaps list, which explicitly assigns `MeridianFlipTime` to the library): the
machinery all exists — `TargetGeometry`'s hour-angle inversions, `TransitTime`'s LST inversion,
`UtcInterval` from `add-interval-algebra` — but there is no API for "which side of the meridian",
"when does the flip land in this session", or "split these candidate windows into same-side
pieces". IS's solver needs flip moments as first-class interval boundaries (finish-east /
finish-west / flip-cost placement), and its pier-side-persistence policy can't be written
without them.

## What Changes

- New `Astronomy.Core.Session.Meridian` static class, composition over existing machinery:
  signed hour angle at an instant, side-of-meridian at an instant, upper-transit enumeration
  within a `UtcInterval`, flip time within a session given a track-past-meridian allowance,
  and splitting canonical window lists at flip boundaries.
- New `MeridianSide` enum — **sky-side semantics** (decision 2026-08-11 with the user): East =
  target east of the meridian (pre-transit), West = at/past transit. Deliberately *not* ASCOM
  pier-side vocabulary — mapping to a mount's pier side is the NINA adapter's one-liner, and the
  pure-astrometry library stays clear of the pierEast-points-west inversion trap.
- No changes to existing APIs; purely additive.

## Capabilities

### New Capabilities

- `meridian-primitives`: side-of-meridian geometry and flip timing — hour angle, meridian side,
  transit enumeration, session flip time, and same-side window splitting.

### Modified Capabilities

<!-- none — additive; no existing spec's requirements change. -->

## Impact

- **Code**: `Astronomy.Core/Session/` (new `Meridian.cs`, `MeridianSide.cs`) +
  `Astronomy.Core.Tests` (new `MeridianTests.cs`). `Intervals`' private canonical-list gate
  becomes `internal` so `Meridian.SplitAtFlip` validates inputs through the same contract.
- **Consumers**: none today (additive). IS is the intended consumer; TP could later surface
  flip markers on charts.
- **Docs**: `docs/architecture/core.md` Session row + CHANGELOG entry ride the code commit.
- **Release**: rides the same unpublished AL state as `add-interval-algebra`; both publish
  before IS's first cut (AL-first gate).
