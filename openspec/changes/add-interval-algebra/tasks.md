# add-interval-algebra — tasks

## 1. Type and ops (additive)

- [ ] 1.1 `UtcInterval` readonly record struct in `Astronomy.Core/Time/UtcInterval.cs` — UTC-only
  fail-fast ctor (non-UTC kind or `End <= Start` throws), `Duration`, `Contains(DateTime)`,
  `Overlaps(UtcInterval)`; XML docs state half-open `[Start, End)` semantics
- [ ] 1.2 `Intervals` static class in `Astronomy.Core/Time/Intervals.cs` — `Intersect`, `Union`,
  `Subtract`, `Clip` over `IReadOnlyList<UtcInterval>`; ordered-disjoint-merged input validation
  throwing on violation; XML docs describe the invariant as the caller contract
- [ ] 1.3 Tests: `UtcIntervalTests.cs` (ctor rejections, Contains/Overlaps edges) and
  `IntervalsTests.cs` — the six named `MaximumAltitudeClipper` relative-position cases,
  touching-boundary scenarios from the spec, invariant-violation throws, and property-style
  round trips (subtract-then-union, intersect/union duality)

## 2. Producer convergence (BREAKING, value-identical)

- [ ] 2.1 `VisibilityWindows.For` returns `IReadOnlyList<UtcInterval>`; internal list building
  swaps element type; `CoarseVisibility` / `SessionSolvers` callers follow
- [ ] 2.2 `MoonSeparation.IntervalsAboveDeg` and `SunSeparation.IntervalsBelowDeg` return
  `IReadOnlyList<UtcInterval>`
- [ ] 2.3 `BestSession` surface converges: `ResolveCandidates` return type, `PlaceBest` /
  `PlaceCentered` candidate-list parameters, internal `MoonClearIntersect` bookkeeping
- [ ] 2.4 Update existing tests mechanically (tuple expectations → `UtcInterval`); assert
  value-identity where a test pinned exact instants — no expected-value changes anywhere
- [ ] 2.5 Confirm no public tuple-interval API remains (`grep '(DateTime Start, DateTime End)'`
  over public signatures) and full `dotnet build` + `dotnet test` of the AL solution passes

## 3. Docs and consumers

- [ ] 3.1 AL `ARCHITECTURE.md` API-tour rows for `UtcInterval` / `Intervals`; note the
  producers' return-type change in the same rows (docs ride the code commit)
- [ ] 3.2 AL `ROADMAP.md` shipped digest + CLAUDE.md follow-ups refresh; cross-note that IS
  ROADMAP "AL gaps to close for IS" item 1 is satisfied by this change
- [ ] 3.3 Rebuild TP / TSM / XFM against the updated Library working tree (recompile-only
  expected — no direct call sites); fix any surprise the blast-radius check missed
