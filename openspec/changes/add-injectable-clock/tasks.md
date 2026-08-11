# add-injectable-clock — tasks

## 1. Implementation

- [x] 1.1 `Astronomy.Core/Time/IClock.cs` — `IClock` (`DateTime UtcNow { get; }`, UTC-kind
  contract in XML docs, dossier rationale one-liner) + `SystemClock` (trivial wrapper,
  singleton `SystemClock.Instance`)
- [x] 1.2 `ObservationMoment.Now(TimeZoneInfo zone, IClock clock)` overload
- [x] 1.3 Tests: `SystemClock.UtcNow` kind + tracks system clock; `ObservationMoment.Now(zone,
  clock)` uses the injected instant (fake clock inline in the test)
- [x] 1.4 Full solution build (VS MSBuild) + full test suite green

## 2. Docs and closure

- [x] 2.1 `docs/architecture/core.md` Time row + CHANGELOG entry (same commit as code)
- [x] 2.2 IS ROADMAP "AL gaps to close for IS" item 3 marked closed (IS repo cross-note)
