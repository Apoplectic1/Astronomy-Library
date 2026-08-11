# injectable-clock — delta spec

## Purpose

The clock abstraction consumers inject instead of reading the ambient clock: a UTC-now
contract, its production implementation, and the `ObservationMoment` composition point.

## ADDED Requirements

### Requirement: Clock contract is UTC-kind

The clock abstraction SHALL expose the current instant as a `DateTime` whose kind is
`DateTimeKind.Utc`. Every implementation SHALL honor this; consumers MAY pass the value
into the library's UTC-gated math without conversion.

#### Scenario: Production clock returns UTC kind

- **WHEN** the production implementation's current instant is read
- **THEN** its `Kind` is `DateTimeKind.Utc` and its value tracks the system UTC clock

### Requirement: Observation moments compose with an injected clock

Building a zone-paired observation moment "now" SHALL be possible from an injected clock,
so clock-driven consumers never read the ambient clock. The ambient-clock form remains
for interactive consumers.

#### Scenario: Moment from injected clock

- **WHEN** an observation moment is built "now" from a clock and a time zone
- **THEN** its UTC instant is the clock's instant and its zone is the given zone, with no
  ambient-clock read
