# Proposal: add-intent-write-surface

> **Seeded 2026-08-12 by ISM's `add-ism-scaffold-ingest` change** (proposal only — AL sessions
> own specs/design/tasks under AL's conventions). **Authorities:** ISM change
> `..\IntervalSchedulerManager\openspec\changes\add-ism-scaffold-ingest\` design D1 (protocol/
> mutation split) and D5 (minimal-create semantics, migration 0002 rationale); schema reference
> `..\IntervalSchedulerManager\docs\design\catalog-db-schema.md`. Consumer-agnostic by charter:
> no feed vocabulary, no ISM naming — the same surface serves ISM's inbox ingest now and ISM's
> planner authoring (stage-0 change 2) next.

## Why

`IntentStore` ships open/close invariants only (`Open` / `Connection` / `Dispose`); the sole
writer today is `TsIntentImporter`, whose SQL is internal to AL and importer-shaped
(empty-store, all-or-nothing). ISM's inbox ingest needs per-entity upserts against a live store,
and ISM's planner authoring (the very next change) needs the same writes for native intent —
the access layer is chartered to AL (schema change D2, pinned in the inbox contract's posture
table), so consumers must not hand-write SQL against AL's schema. Drift should be
impossible-by-API, not caught-by-test.

## What Changes

- **Intent write/lookup surface** on the store (exact shape an AL-session call): upsert
  operations for `project` / `target` / `exposure_template` / `exposure_plan`, plus lookup by
  optional provenance key (`imported_from_ts_guid`). Semantics the consumer contract needs:
  full-value update of caller-supplied fields; caller-supplied `created_at` on creates; NULL
  means unset (R3 — the API never invents values); transactions compose with a caller-owned
  transaction scope (ISM ingests one file per transaction).
- **Migration `0002_minimum_time_nullable.sql`**: `project.minimum_time_minutes` `NOT NULL` →
  nullable (NULL = no minimum, COALESCE-at-read like the other settings columns). Rationale
  (ISM D5): feed-created projects don't carry it, and an invented 0 would be an R3 sentinel.
  0002 is deliberate over editing 0001: 0001 is applied to the live store, and R8 discipline
  forbids touching an applied script (the `schema_migration` log's correspondence to scripts is
  what makes the framework trustworthy). Opportune, not just necessary — the portfolio's first
  live migration runs while the store is pre-hardening and the recovery card (delete + re-lift)
  is still in hand, giving the framework a field record before migrations become load-bearing.
- **Tests (xunit.v3)**: upsert round-trips (create + update paths), provenance lookup, NULL
  handling, and 0002 migrating an existing populated store in place.

## Capabilities

### Modified Capabilities

- `intent-store`: gains the write/lookup surface requirements (upsert semantics, provenance
  lookup, caller-transaction composition) and the 0002 migration behavior. *(Refine at spec
  time — AL sessions decide whether this is a delta to `intent-store` or a sibling capability
  per AL's spec organization.)*

## Impact

- **Consumers**: ISM `add-ism-scaffold-ingest` task group 3 blocks on this shipping (its ingest
  mutations go through this surface); ISM's stage-0 change 2 (planner authoring) consumes the
  identical API — it pays twice.
- **AL**: `Astronomy.Catalog\Intent\` grows the surface + one migration script; no feed/JSONL
  knowledge enters AL. Existing importer untouched.
- **Live store**: 0002 applies via normal migrate-on-open at ISM's first post-ship open;
  additive (constraint relaxation), transactional, logged in `schema_migration`.
