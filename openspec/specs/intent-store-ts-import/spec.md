# intent-store-ts-import

## Purpose

The library-side contract of the one-time Target Scheduler lift: importing a `schedulerdb.sqlite`
inventory into an empty intent store with fixed field scope, per-row provenance, and explicit
translation maps — so authored intent accumulated under TS is never re-entered by hand.

## Requirements

### Requirement: Source is opened read-only

The importer SHALL open the TS database read-only and SHALL NOT modify it under any circumstance,
including on failure.

#### Scenario: Source unchanged by any import outcome

- **WHEN** an import runs to completion or aborts partway
- **THEN** the TS database file is byte-for-byte unchanged

### Requirement: Import is all-or-nothing into an empty store

The importer SHALL refuse a store that already contains authored intent, and SHALL run as a single
transaction: either every in-scope entity lands or the store is left exactly as it was. There is
no merge mode and no partial import.

#### Scenario: Non-empty store is refused

- **WHEN** the import is pointed at a store already holding intent rows
- **THEN** it refuses with a clear error and the store is unchanged

#### Scenario: Aborted import leaves no residue

- **WHEN** the import aborts for any reason
- **THEN** the store contains no rows written by the aborted run

### Requirement: Field scope is fixed by the schema design doc

The importer SHALL lift exactly the in-scope field set enumerated in the schema design doc's
import-scope table (profiles, projects, targets, exposure templates, exposure plans — the fields
in real use), and SHALL NOT silently drop an in-scope field, invent a value for one, or widen the
scope with out-of-scope TS fields (acquisition history, rule weights, cadence state, plugin
preferences are deliberately excluded).

#### Scenario: Entity counts match the source

- **WHEN** the import completes
- **THEN** store entity counts for each in-scope table match a direct query of the source, and
  in-scope field values round-trip per the scope table's translations

#### Scenario: Mosaic structure is reconstructed

- **WHEN** a TS mosaic project's targets are imported
- **THEN** the store holds one parent target row plus one child row per panel linked by the
  self-referencing parent key, per the shipped mosaic invariant

### Requirement: Sentinels become NULL

The importer SHALL translate TS sentinel values meaning "unset" (e.g. `-1` priority, `-1.0`
exposure, `0.0` minimum altitude, `-1` readout mode) to `NULL` per the store's NULL-means-unset
rule. Sentinel translation is boundary mapping, not a fallback: a required field that is missing,
blank, or unparseable still aborts.

#### Scenario: Sentinel translates to NULL

- **WHEN** a source field carries its documented TS sentinel
- **THEN** the store row holds `NULL` for that column

### Requirement: Every imported row carries provenance

Each imported row SHALL record the TS GUID of the row it was lifted from, so lineage is
verifiable per row against the source database.

#### Scenario: Imported row is traceable

- **WHEN** any imported row is inspected
- **THEN** its provenance column identifies the source TS row, and that identifier resolves in the
  source database

### Requirement: Enum codes cross through pinned translation maps

Every enum-coded column imported from TS SHALL pass through an explicit TS→store translation map:
project state, project/target priority, twilight level, and the NINA→library epoch map (NINA
JNOW=0/B1950=1/J2000=2/J2050=3 → library B1950=0/JNow=1/J2000=2). A source value with no mapping
SHALL abort the import — never a raw cast, never a guessed default. The maps SHALL be pinned by
tests.

#### Scenario: Epoch codes are remapped, not cast

- **WHEN** a source target carries a NINA epoch code
- **THEN** the stored epoch id is the library's ordering for that same epoch name, differing from
  the source int where the orderings disagree

#### Scenario: Unmapped enum value aborts

- **WHEN** a source row carries an enum code absent from its translation map
- **THEN** the import aborts, naming the table, column, offending value, and source row

### Requirement: Contract violations abort the import

The importer SHALL fail fast on any input-contract violation: a required source field that is
missing, blank, or unparseable aborts the entire import with a console error and a log entry
naming the entity, field, and expectation. No fallback values, no skip-the-row, no
warn-and-continue.

#### Scenario: Missing required field aborts with diagnostics

- **WHEN** a required source field is missing, blank, or unparseable
- **THEN** the import aborts; the error and log entry name the source entity, the field, and what
  was expected; the store is unchanged
