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
SHALL roll back leaving the prior version intact and the open failing loudly.

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
