# Tasks — add-intent-write-surface

## 1. Migration 0002 + framework rebuild posture

- [x] 1.1 Add `Astronomy.Catalog/Intent/Migrations/0002_minimum_time_nullable.sql` — R10 table
      rebuild of `project` relaxing `minimum_time_minutes` to nullable (new table, explicit-column
      copy, drop, rename, recreate the four `ix_project_*` indexes); picked up by the existing
      embedded-resource wildcard.
- [x] 1.2 `IntentMigrations.Apply`: suspend `PRAGMA foreign_keys` around the apply loop, gate each
      script's commit on `PRAGMA foreign_key_check` (violation → rollback + `IntentStoreException`),
      restore enforcement in `finally`.
- [x] 1.3 Tests: populated v1 store (real 0001 script + profile/project/target rows) migrates in
      place via `IntentStore.Open` — data + FK links intact, column accepts NULL, indexes present,
      0002 logged; a throwaway script that leaves a dangling FK rolls back with the store at the
      prior version.

## 2. Write/lookup surface

- [x] 2.1 Schema-mirror records `ProjectIntent` / `TargetIntent` / `ExposureTemplateIntent` /
      `ExposurePlanIntent` (required where DDL demands a value with no default; DDL defaults
      mirrored; raw UNIX-seconds timestamps).
- [x] 2.2 `IntentWriter` over an open `IntentStore`: four full-value `ON CONFLICT(id)` upserts
      (`created_at` excluded from the update set) + four provenance lookups
      (`Find<Entity>Id(importedFromTsGuid)` — none → null, duplicate → loud), every operation
      taking `SqliteTransaction? transaction = null`.
- [x] 2.3 Tests: create round-trips across all four entities, full-value update preserving
      `created_at`, NULL-means-unset, caller-transaction rollback discards grouped writes,
      provenance lookups (known / unknown / duplicate).

## 3. Compatibility + verification

- [x] 3.1 Importer↔surface compatibility test: import a synthetic TS fixture via
      `TsIntentImporter`, resolve rows by provenance through `IntentWriter`, upsert an update under
      the resolved id — no duplicate row, provenance + GuidBlob encoding intact.
- [x] 3.2 `dotnet build Astronomy.Catalog.Tests` with zero warnings; `dotnet test --no-build`
      green on the full Catalog suite.
- [x] 3.3 `openspec validate add-intent-write-surface --strict` passes.

## 4. Docs (same commit)

- [x] 4.1 `CHANGELOG.md` entry; `docs/architecture/catalog.md` Intent bullet gains the write
      surface + 0002 + framework FK posture; `ROADMAP.md` digest updated (latest-three rule).
