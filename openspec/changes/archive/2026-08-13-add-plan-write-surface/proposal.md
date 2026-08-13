# Proposal: add-plan-write-surface

## Why

ISM's stage-0 planner (`add-planner-surface`, its design D6) authors draft interval plans into
the intent store's plan plane — but the plan tables (`plan`, `plan_interval`, shipped in
migration 0001) have no write surface: `IntentWriter` covers the four intent-plane entities
only, and no caller may hand-write SQL against the schema. Same shape as
`add-intent-write-surface` was for the ingest: a small, consumer-agnostic AL surface the
consumer change is gated on.

## What Changes

- **Schema-mirror records**: `PlanIntent` (profile, night_of, state, authorship,
  switch_immediately, created/blessed stamps) and `PlanIntervalIntent` (plan, sequence number,
  target, exposure plan, start/end, amended-by-user) in `Astronomy.Catalog.Intent`.
- **`PlanWriter`** over an open `IntentStore`, sibling to `IntentWriter` (kept separate — the
  plan plane's access shapes differ: by-night reads, ordered whole-set interval replace):
  - `UpsertPlan` — full-value `ON CONFLICT(id)` upsert, `created_at` create-only, same contract
    as the intent-plane upserts.
  - `ReplaceIntervals(planId, intervals)` — the authoring write: delete the plan's interval rows
    and insert the supplied ordered set, one logical operation (composes with the caller's
    transaction); a supplied interval naming a different plan fails loudly.
  - `FindCurrentPlan(profileId, nightOf)` — the night's non-superseded plan or null; two
    non-superseded plans for one night is a data-integrity violation, thrown loudly.
  - `ReadIntervals(planId)` — the plan's intervals ordered by sequence number.
- Every operation takes `SqliteTransaction? transaction = null`; the writer never owns a
  transaction. No migration — the tables exist; a column gap found at implementation would
  arrive as a normal additive migration (none expected).

## Capabilities

### Modified Capabilities

- `intent-store`: gains the plan-plane write/read surface.

## Impact

- **AL**: `Astronomy.Catalog` (+ tests). No schema change, no consumer breakage — additive.
- **ISM**: unblocks `add-planner-surface` task 4 (draft persistence); local `ProjectReference`
  picks it up on rebuild.
- **Publishing**: rides the next AL publish per cross-repo release ordering when an app release
  needs it; ISM consumes the working tree meanwhile.
