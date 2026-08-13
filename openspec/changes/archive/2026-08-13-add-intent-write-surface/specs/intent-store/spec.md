# intent-store (delta)

## ADDED Requirements

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

## MODIFIED Requirements

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
