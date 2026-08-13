# Design — add-intent-write-surface

## Context

Motivation: see `proposal.md` § Why. Current state: `IntentStore` owns open/close invariants only;
the sole writer is `TsIntentImporter` (raw SQL, internal to AL, empty-store all-or-nothing).
Schema authority is ISM `docs\design\catalog-db-schema.md`; the live DDL is
`Intent/Migrations/0001_initial.sql`, applied through `IntentMigrations` (embedded
`NNNN_name.sql` scripts, per-script transactions, `schema_migration` log + `user_version`).
Consumer requirements this surface must satisfy without naming the consumer: per-entity upserts
against a live store, provenance-keyed resolution, one caller-owned transaction spanning many
operations, caller-supplied `created_at`, NULL-means-unset (R3).

## Goals / Non-Goals

**Goals:**

- A consumer-agnostic write/lookup surface for the four intent-plane entities
  (project / target / exposure_template / exposure_plan), drift-impossible-by-API.
- Migration `0002_minimum_time_nullable.sql` — the portfolio's first real table-rebuild
  migration — plus the framework posture that makes R10 rebuilds executable at all.

**Non-Goals:**

- No profile write/lookup surface (nothing needs to author profiles yet; the importer creates
  them). No plan-plane writes (`plan`/`plan_interval` — planner-authoring scope, next change).
- No read/query surface beyond provenance resolution — callers with richer read needs come back
  with their own change.
- No delete operations; nothing consumes deletion yet.
- Existing importer untouched (it predates the surface and its empty-store contract is different).

## Decisions

### D1 — Surface shape: `IntentWriter` over an open `IntentStore`, schema-mirror records

A new `IntentWriter` instance type constructed over an open `IntentStore`, with one upsert and one
provenance-lookup method per entity kind, taking schema-mirror parameter records (`ProjectIntent`,
`TargetIntent`, `ExposureTemplateIntent`, `ExposurePlanIntent` — one property per writable
column, `required` where the DDL demands a value with no default). *Alternatives*: methods on
`IntentStore` itself — rejected (the store's charter is open/close invariants; keeping the write
surface a separate type keeps both legible); a static class like `TsIntentImporter` — rejected
(the surface is long-lived per-store, not a one-shot operation; an instance over the store reads
naturally at call sites).

### D2 — Upsert keyed by id; provenance is a resolve step, not a second upsert key

Upserts are `INSERT ... ON CONFLICT(id) DO UPDATE SET ...` keyed on the caller-supplied row id.
"Keyed by provenance or id" is achieved by composition: `Find<Entity>Id(provenanceKey)` resolves
an existing row's id (or nothing), and the caller upserts under the resolved id — or a fresh
`Guid` for a create. *Alternative*: a provenance-keyed upsert built into the statement — rejected
(two key paths in one operation is ambiguous when both are supplied, and the resolve-then-upsert
split is exactly the caller's natural flow). Duplicate provenance at lookup throws
`IntentStoreException` (fail fast — the schema deliberately has no UNIQUE on
`imported_from_ts_guid`, so the lookup is the integrity gate).

### D3 — `created_at` is caller-supplied on create and immutable on update

The records carry `CreatedAt` as a required raw UNIX-seconds `long` (R12 schema-mirror; the
surface invents no conversion layer and no clock — the caller owns the creation instant). The
`ON CONFLICT ... DO UPDATE SET` list deliberately excludes `created_at`, so an update can never
rewrite a row's creation instant even though the full record travels.

### D4 — NULL means unset; DDL defaults mirror as record defaults

Nullable record properties write verbatim — NULL stores as NULL, never coalesced (R3: the API
never invents values). Columns whose DDL carries a default (the boolean policy columns,
`horizon_offset_deg`, `epoch_id`, `enabled`) mirror that same default on the record property —
that is restating authored schema truth in the API, not inventing a value, and it lets a
minimal create supply only what it knows.

### D5 — Caller-owned transactions: optional `SqliteTransaction?` on every operation

Every upsert and lookup takes `SqliteTransaction? transaction = null`. The writer never begins,
commits, or rolls back a transaction of its own — a caller groups any number of operations by
passing its transaction (Microsoft.Data.Sqlite requires commands to carry the connection's
active transaction, so the parameter is load-bearing, not decorative). Single operations outside
any transaction remain valid (autocommit).

### D6 — 0002 is an R10 table rebuild; the framework owns foreign-key suspension

SQLite cannot ALTER a column constraint, so `0002_minimum_time_nullable.sql` rebuilds `project`:
create `project_rebuild` with the target DDL (`minimum_time_minutes INTEGER`, nullable), copy
every row with an explicit column list, `DROP TABLE project`, rename `project_rebuild` →
`project`, recreate the four `ix_project_*` indexes. The live table is never renamed — renaming
it would rewrite child tables' `REFERENCES project` clauses; only the throwaway name is renamed,
which nothing references.

Dropping a referenced parent table is impossible with FK enforcement on, and
`PRAGMA foreign_keys` is a no-op inside a transaction — so the **framework**, not the scripts,
owns the posture (SQLite's documented rebuild procedure): `IntentMigrations.Apply` suspends
`foreign_keys` around the apply loop, gates every script's commit on a whole-store
`PRAGMA foreign_key_check` (any violation → rollback + loud `IntentStoreException`), and
restores enforcement in a `finally`. This generalizes to every future R10 rebuild instead of
being 0002-shaped. *Alternative*: `writable_schema` in-place DDL edit — rejected (R10 mandates
rebuild; hand-editing `sqlite_master` is exactly the hazardous shortcut the rule exists to
forbid). *Alternative*: cascade-rebuilding all child tables under FK ON — rejected (rebuild
fan-out grows with the schema; the documented suspend-and-check procedure is the standard tool).

## Risks / Trade-offs

- [FK enforcement suspended while a script runs] → the pre-commit `foreign_key_check` gate scans
  the whole store; a script that leaves any dangling reference rolls back loudly. Net integrity
  is stronger than before (0001-era scripts ran with enforcement on but no whole-store check).
- [Full-value upsert can NULL a field the caller didn't mean to drop] → deliberate contract
  (full-value update of caller-supplied fields, per the seed); the records make every field
  explicit at the call site, so an accidental partial record is visible in review and covered by
  the round-trip tests.
- [0002 rewrites the live `project` table] → transactional with rollback; rides normal
  migrate-on-open; the store posture doc's bulk-operation guidance (pause file sync) applies
  operationally on the consumer side.

## Migration Plan

`0002` ships embedded (the existing `Intent\Migrations\*.sql` wildcard) and applies via normal
migrate-on-open at the first post-ship open of a v1 store. Failure = transaction rollback, store
intact at v1, open fails loudly. No app-side action; no data transformation (constraint
relaxation only — every v1 row is valid v2 data).
