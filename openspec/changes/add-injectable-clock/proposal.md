# add-injectable-clock

## Why

IS ROADMAP "AL gaps to close for IS" item 3, closing the IS dossier's injectable-clock decision
(SCHEDULER_DESIGN.md, pinned 2026-08-11): no IS component reads `DateTime.UtcNow` directly —
"now" enters the planner as a parameter (already the `Astronomy.Core` convention) and enters the
executing container via an injected clock. Both IS and ISM's precompute consume the abstraction,
so it needs one AL home rather than two per-app definitions.

## What Changes

- New `IClock` interface (`DateTime UtcNow { get; }`, contract: always `DateTimeKind.Utc`) and
  trivial `SystemClock` production implementation in `Astronomy.Core.Time`, beside
  `TimeKindGuard` / `ObservationMoment`.
- New `ObservationMoment.Now(TimeZoneInfo zone, IClock clock)` overload so clock-driven
  consumers build display moments without touching the ambient path — the existing
  `Now(zone)` (AL's one sanctioned ambient-clock read) stays for interactive apps.
- Test fakes stay consumer-side (a fixed/stepping fake is three lines); the library ships
  only the contract and the production implementation.

## Capabilities

### New Capabilities

- `injectable-clock`: the clock abstraction — UTC-kind contract, production implementation,
  and the `ObservationMoment` composition point.

### Modified Capabilities

<!-- none — ObservationMoment gains an additive overload; no existing requirement changes. -->

## Impact

- **Code**: `Astronomy.Core/Time/IClock.cs` (+ `SystemClock` in the same file),
  `Time/ObservationMoment.cs` (one overload), small test additions.
- **Consumers**: none today; IS's container and ISM's precompute are the intended consumers.
  The thread-safety audit note ("zero ambient-clock reads outside `ObservationMoment.Now`")
  is unchanged — `SystemClock` wraps that same single sanctioned read pattern.
- **Docs**: `docs/architecture/core.md` Time row + CHANGELOG ride the code commit; IS ROADMAP
  gap 3 cross-note.
