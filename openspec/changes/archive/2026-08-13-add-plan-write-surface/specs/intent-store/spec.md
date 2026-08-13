# intent-store — delta for add-plan-write-surface

## ADDED Requirements

### Requirement: Plan-plane writes go through the plan surface

The store SHALL expose a plan-plane write/read surface (`PlanWriter` with `PlanIntent` /
`PlanIntervalIntent` schema-mirror records) so no caller hand-writes SQL against `plan` /
`plan_interval`. `UpsertPlan` SHALL be a full-value upsert keyed by the caller-supplied id with
`created_at` written on create only. `ReplaceIntervals` SHALL replace the plan's whole interval
set with the supplied ordered rows as one logical operation and SHALL fail loudly when a
supplied interval names a different plan. `FindCurrentPlan` SHALL resolve the profile+night's
single non-superseded plan (null when none) and SHALL fail loudly when more than one exists.
Every operation SHALL compose with a caller-owned transaction and never own one.

#### Scenario: Draft round-trips with its intervals

- **WHEN** a caller upserts a draft plan and replaces its intervals inside one caller
  transaction, commits, and reads back
- **THEN** `FindCurrentPlan` returns the draft and `ReadIntervals` returns the rows in sequence
  order with every field as supplied

#### Scenario: Caller rollback discards the whole authoring write

- **WHEN** a caller upserts a plan and replaces intervals under its transaction and rolls back
- **THEN** the store holds neither the plan nor any interval row

#### Scenario: Two live plans for one night is an integrity violation

- **WHEN** `FindCurrentPlan` finds two non-superseded plans for one profile and night
- **THEN** it throws loudly rather than choosing one
