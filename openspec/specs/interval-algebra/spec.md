# interval-algebra

## Purpose

The UTC time-interval value type and set operations (intersect, union, subtract, clip) that
`Astronomy.Core`'s interval producers return and downstream interval solvers compose.

## Requirements

### Requirement: Interval construction is UTC-only and fail-fast

An interval SHALL be an immutable value with a start and end instant, both
`DateTimeKind.Utc`. Construction SHALL throw when either endpoint has a non-UTC kind
(no silent conversion) or when the end is not strictly after the start. There SHALL be
no representation of an empty interval — emptiness is expressed as an empty interval list.

#### Scenario: Non-UTC endpoint rejected

- **WHEN** an interval is constructed with a `DateTimeKind.Local` or `Unspecified` endpoint
- **THEN** construction throws, naming the offending endpoint and expected kind

#### Scenario: Degenerate interval rejected

- **WHEN** an interval is constructed with end equal to or before start
- **THEN** construction throws

### Requirement: Interval lists are ordered and disjoint

Every interval list accepted or returned by the algebra operations SHALL be ordered
ascending by start and pairwise disjoint. Elements MAY touch end-to-start — adjacent
distinct intervals are a legitimate currency (e.g. same-side pieces split at a meridian
flip) and SHALL NOT be rejected or silently coalesced by non-union operations.
Union's output SHALL additionally be merged: no two result elements overlap or touch.

#### Scenario: Union merges overlapping and touching intervals

- **WHEN** union is applied to lists containing overlapping or exactly-touching intervals
- **THEN** the result coalesces them into single intervals

#### Scenario: Touching input is accepted by non-union operations

- **WHEN** a list whose elements touch end-to-start is passed to subtract or intersect
- **THEN** the operation runs normally, treating the touching elements as distinct

### Requirement: Intersection

Intersection of two interval lists SHALL return exactly the instants contained in both.
Boundary-touching inputs (one interval ending where another starts) SHALL produce no
degenerate output — the touching instant yields nothing.

#### Scenario: Overlapping intervals intersect

- **WHEN** `[20:00, 23:00)` is intersected with `[22:00, 02:00)`
- **THEN** the result is the single interval `[22:00, 23:00)`

#### Scenario: Touching intervals produce empty intersection

- **WHEN** `[20:00, 22:00)` is intersected with `[22:00, 23:00)`
- **THEN** the result is an empty list

### Requirement: Subtraction covers the forbidden-band cases

Subtracting a span from a window SHALL return the 0–2 intervals of the window not covered by
the span, handling every relative position of window vs span: disjoint before, disjoint after,
span clips the head, span clips the tail, span swallows the window (empty result), and span
strictly inside the window (two results). Subtraction of an interval list from an interval
list SHALL generalize this element-wise while preserving the list invariant.

#### Scenario: Forbidden band strictly inside the window splits it

- **WHEN** span `[23:00, 00:30)` is subtracted from window `[21:00, 03:00)`
- **THEN** the result is `[21:00, 23:00)` and `[00:30, 03:00)`

#### Scenario: Span swallows the window

- **WHEN** span `[20:00, 04:00)` is subtracted from window `[22:00, 23:00)`
- **THEN** the result is an empty list

### Requirement: Clip to bounds

Clipping an interval list to a bounding interval SHALL be equivalent to intersecting the list
with that single bound.

#### Scenario: Windows clipped to the night

- **WHEN** a list of visibility windows is clipped to a night's dusk–dawn bound
- **THEN** every returned interval lies within the bound and partial overlaps are trimmed to it

### Requirement: Interval producers return the shared type

The library's public interval-producing APIs (target visibility windows per night,
moon-separation-above-threshold intervals, sun-separation-below-threshold intervals) SHALL
return the shared interval type, carrying interval values identical to those previously
returned as start/end tuples, and their outputs SHALL satisfy the ordered-disjoint
invariant. No tuple-returning interval API SHALL remain public.

#### Scenario: Visibility windows compose with the algebra directly

- **WHEN** a caller computes visibility windows and moon-clear intervals for the same night
- **THEN** the two results intersect via the algebra with no representation conversion
