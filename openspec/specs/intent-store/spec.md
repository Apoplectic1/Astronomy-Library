# intent-store

## Purpose

The library's access surface for an authored intent store — a permanent, user-owned SQLite
database of planning truth (targets, desired counts, membership, policy, blessed plans) — covering
schema stand-up, the migration framework, and store open/close invariants. Consumer-agnostic: any
caller (an app, a test host, a simulation harness) gets the same contract.
## Requirements
### Requirement: Store persists authored intent only

The intent-store schema SHALL persist authored intent — profiles, projects, targets (with authored
coordinates), exposure templates, exposure plans with desired counts, and interval plans with
blessing state — and SHALL NOT contain tables or columns for acquisition history or disk-scan
results. Progress truth lives in the image library on disk and is scanned fresh by callers; the
store never caches it.

#### Scenario: Authored intent round-trips

- **WHEN** a caller writes a target, desired count, or plan row and reopens the store
- **THEN** the authored values read back exactly as written

#### Scenario: No actuals surface in the schema

- **WHEN** the schema's tables and columns are enumerated
- **THEN** no table stores per-image acquisition history or scan output, and `exposure_plan`
  carries no acquired/accepted count columns

### Requirement: Schema follows the adopted rule set with gap closures

The schema SHALL follow the portfolio's adopted store rules: GUID primary keys stored as 16-byte
big-endian BLOBs with no surrogate integer keys; normalized profile scoping; NULL means unset with
sentinels forbidden; enum columns backed by lookup tables **and** companion `CHECK (col IN (...))`
constraints; every foreign key indexed with `ix_<table>_<col>` naming (including enum-lookup FKs);
`snake_case` singular identifiers; booleans as `INTEGER CHECK (col IN (0,1))`; timestamps as UNIX
seconds UTC. The epoch lookup SHALL use the library's canonical ordering (B1950=0, JNow=1,
J2000=2).

#### Scenario: Enum columns are constrained and joinable

- **WHEN** a write attempts an enum-coded value outside the lookup's domain
- **THEN** the database rejects it via the companion CHECK constraint, and valid values join to
  their lookup table for a human-readable name without consulting application source

#### Scenario: Every foreign key is indexed

- **WHEN** the schema's foreign-key columns are enumerated
- **THEN** each has a covering index named `ix_<table>_<col>` (composites allowed for hot joins)

### Requirement: Store is migrated, never rebuilt

The store SHALL ship with a migration framework: an ordered set of transactional migration scripts
(`NNNN_name.sql`), a `schema_migration` log table recording each applied migration (version, name,
applied-at), and `PRAGMA user_version` kept in sync as the fast check. Opening an older store
SHALL apply pending migrations in version order, each in its own transaction; a failed migration
SHALL roll back leaving the prior version intact and the open failing loudly. A migration that
reshapes an existing table (the engine cannot alter a column's constraints in place) SHALL do so
by table rebuild — create the new shape, copy every row, drop the old table, rename, recreate the
table's indexes — preserving all data and referential links; the framework SHALL verify the
store's foreign-key integrity before committing each script, and a violation fails that migration
with rollback.

#### Scenario: Older store migrates in order

- **WHEN** a store whose schema version is older than the library's latest migration is opened
- **THEN** pending migrations apply in version order, each recording its `schema_migration` row,
  and `user_version` ends at the latest version

#### Scenario: Failed migration leaves the prior version intact

- **WHEN** a migration script fails partway
- **THEN** its transaction rolls back, `schema_migration` and `user_version` still show the prior
  version, and the open fails with an error naming the failed migration

#### Scenario: Newer store fails fast

- **WHEN** a store whose schema version is newer than the library understands is opened
- **THEN** the open aborts with a clear error naming both versions; no write occurs

#### Scenario: Table rebuild migrates a populated store in place

- **WHEN** a populated store whose current schema requires a value in a column the latest schema
  makes optional is opened
- **THEN** the pending rebuild migration preserves every row and referential link, the relaxed
  column accepts NULL afterward, the table's indexes exist under their original names, and the
  migration is logged like any other

#### Scenario: Migration breaking referential integrity rolls back

- **WHEN** a migration script leaves a foreign-key reference dangling
- **THEN** the pre-commit integrity check fails that migration, its transaction rolls back, and
  the store remains at the prior version

### Requirement: Open rejects non-local paths

Opening the store SHALL fail fast when the path is not on a local fixed drive — UNC paths and
mapped network shares are rejected with an error naming the path and the local-only rule. No
connection is opened and no file is created for a rejected path.

#### Scenario: UNC path is refused

- **WHEN** a caller opens the store via a UNC path
- **THEN** the open throws with a message naming the path and the local-only rule, and no database
  file is created there

### Requirement: Concurrent second writer fails loudly

The store SHALL be single-writer: while one writable connection holds the store, a second writer's
conflicting operation SHALL surface a loud, unambiguous failure rather than silently interleaving
or waiting indefinitely.

#### Scenario: Second writer is refused

- **WHEN** a write is attempted while another writable connection holds the store's write lock
  beyond the busy window
- **THEN** the attempt fails with an explicit error; no interleaved or partial write occurs

### Requirement: Closed store is sync-safe at rest

Closing the store SHALL checkpoint the WAL in TRUNCATE mode so that the database file alone — with
no `-wal`/`-shm` sidecar content pending — contains every committed write. A file-level copy of a
closed store opens as a complete, consistent database.

#### Scenario: Closed store is a complete single-file copy

- **WHEN** the store is closed and the `.db` file alone is copied elsewhere
- **THEN** the copy opens as a consistent database containing every committed write, and the WAL
  file left behind (if any) is zero-length

### Requirement: Write surface upserts intent entities

The store's access surface SHALL provide upsert operations for the four intent-plane entity
kinds — project, target, exposure template, exposure plan — so that no caller hand-writes SQL
against the schema. Each upsert is keyed by the caller-supplied row id: a row that does not exist
is created; a row that exists is updated as a **full-value write** — every caller-supplied field
overwrites the stored value, including NULL, which stores as NULL (unset; the surface never
substitutes a default or sentinel for a caller-supplied value). Creates SHALL take the
caller-supplied creation timestamp where the schema requires one; updates SHALL never alter a
row's creation timestamp.

#### Scenario: Create round-trips exactly

- **WHEN** a caller upserts an entity under an id not present in the store
- **THEN** the row is created holding exactly the supplied values — including the caller-supplied
  creation timestamp where the schema requires one — and reads back identically

#### Scenario: Update is a full-value write

- **WHEN** a caller upserts an entity under an id already present, supplying changed values and
  NULL for a previously set optional field
- **THEN** every writable field holds the newly supplied value, the optional field reads back
  NULL, and the row's creation timestamp is unchanged

#### Scenario: NULL is stored as unset, never replaced

- **WHEN** a caller supplies NULL for an optional field on create
- **THEN** the stored value is NULL — no default, zero, or other invented value is substituted

### Requirement: Write and lookup operations compose with a caller-owned transaction

Write and lookup operations SHALL accept the caller's own transaction scope, so a caller can
group any number of operations into one atomic unit that commits or rolls back as a whole. Inside
such a scope the surface SHALL NOT commit or roll back on the caller's behalf.

#### Scenario: Caller rollback discards every grouped write

- **WHEN** a caller performs several upserts inside its own transaction and rolls the
  transaction back
- **THEN** none of the writes are visible afterward and the store's prior content is intact

### Requirement: Provenance lookup resolves externally keyed rows

For each of the four intent-plane entity kinds, the surface SHALL resolve a row id from the
optional provenance key (`imported_from_ts_guid`). An unmatched key resolves to nothing (not an
error); a key matching more than one row of the same entity kind SHALL fail loudly — duplicate
provenance is a data-integrity violation, never silently disambiguated.

#### Scenario: Known provenance resolves to the row

- **WHEN** exactly one row of an entity kind carries provenance key K and a caller looks up K
- **THEN** the lookup returns that row's id

#### Scenario: Unknown provenance resolves to nothing

- **WHEN** no row of the entity kind carries provenance key K
- **THEN** the lookup reports no match, without error

#### Scenario: Duplicate provenance fails loudly

- **WHEN** two rows of the same entity kind carry provenance key K and a caller looks up K
- **THEN** the lookup throws an error naming the entity kind and the key

### Requirement: Write-surface rows and imported rows are interoperable

Rows written by the one-time import and rows written through the write surface SHALL share the
same conventions — 16-byte big-endian GUID key encoding and the `imported_from_ts_guid`
provenance column — so either path's rows can be resolved and updated through the other.

#### Scenario: Imported row updates through the surface

- **WHEN** a store is populated by the one-time import, and a caller resolves a row by its
  provenance key and upserts changed values under the resolved id
- **THEN** the update lands on the imported row — no duplicate row appears — and the row reads
  back with its key encoding and provenance intact

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

