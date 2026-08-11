# add-meridian-primitives — tasks

## 1. Primitives

- [x] 1.1 `MeridianSide.cs` in `Astronomy.Core/Session/` — East/West enum, sky-side semantics,
  XML docs naming the ASCOM pier-side inversion trap and the adapter's mapping responsibility
- [x] 1.2 `Meridian.cs` in `Astronomy.Core/Session/` — `HourAngleAt` (signed `[-12, +12)`),
  `SideAt`, `TransitsIn`, `FlipTimeIn` (shifted-search per design decision 4), `SplitAtFlip`;
  `Intervals.RequireCanonical` private → internal for the shared gate

## 2. Tests

- [x] 2.1 `MeridianTests.cs` — hour-angle sign convention around a real transit; SideAt
  East/West/at-transit; TransitsIn half-open boundaries + the two-transits-in-24h case;
  FlipTimeIn in-session / pre-session-transit / no-transit / negative-allowance cases;
  SplitAtFlip straddle, boundary no-op, canonical-violation throw, total-time preservation
- [x] 2.2 Full solution build (VS MSBuild) + full test suite green

## 3. Docs and closure

- [x] 3.1 `docs/architecture/core.md` Session row gains `Meridian` / `MeridianSide`; CHANGELOG
  entry (same commit as code)
- [x] 3.2 IS ROADMAP "AL gaps to close for IS" item 2 marked closed (IS repo cross-note)
