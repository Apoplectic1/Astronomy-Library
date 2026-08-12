# Tasks: add-intent-store

## 1. Schema + migration framework

- [x] 1.1 Author `Intent\Migrations\0001_initial.sql` — intent plane + minimal plan plane per the
  ISM schema doc's table inventory, with the three gap closures (companion enum CHECKs, per-FK
  `ix_` indexes incl. enum-lookup FKs, snake_case/GUID-BLOB/NULL-not-sentinel rules); embed as
  resource
- [x] 1.2 Implement the migration framework: embedded-script discovery (`NNNN_name.sql`, ordered),
  per-script transactions, `schema_migration` log writes, `user_version` sync, newer-than-library
  abort
- [x] 1.3 Framework tests (xunit.v3): fresh file migrates 0→latest; in-order apply with log rows;
  mid-script failure rolls back leaving prior version; newer store aborts without write;
  `user_version` matches the log after every path

## 2. Store API

- [x] 2.1 Implement `IntentStore : IDisposable` — `Open(path)` = local-path guard (UNC + network
  drive, fail-fast before file creation) → version gate/migrate → configure (foreign_keys, WAL,
  synchronous=NORMAL, short busy_timeout); `Dispose` = WAL checkpoint TRUNCATE + close;
  consumer-agnostic XML docs, no default path constant
- [x] 2.2 Store tests: authored rows round-trip across close/reopen; UNC/network path refused with
  path + rule named, no file created; second-writer conflict surfaces loudly; closed store's `.db`
  alone copies as a complete consistent database (zero-length WAL); schema enumeration shows no
  actuals tables/columns

## 3. TS importer

- [x] 3.1 Enumerate TS enum domains (project state, priority, twilight level, NINA epoch) from the
  TS reference clone; implement the R13 translation maps + sentinel→NULL translations with
  abort-on-unmapped
- [x] 3.2 Implement the import read layer (`Intent\TsImport\`): read-only open, explicit column
  lists for the full in-scope field set from the schema doc §5 scope table — shipped
  `TargetSchedulerReader` untouched
- [x] 3.3 Implement the lift: empty-store precondition, single transaction, per-entity mapping with
  `imported_from_ts_guid` provenance on every row, mosaic parent/child reconstruction, rule-#16
  exceptions naming entity/field/value/expectation
- [x] 3.4 Importer tests: translation maps pinned by name (epoch map esp. — SafeEpoch precedent);
  unmapped enum aborts naming table/column/value/row; non-empty store refused; abort leaves store
  row-free and source byte-identical; sentinel→NULL cases
- [x] 3.5 Snapshot round-trip test against the TS working db (existing shared-path convention in
  `Astronomy.Catalog.Tests`): per-entity counts match direct source queries; provenance GUIDs
  resolve; spot-check field translations

## 4. Operational driver + wrap-up

- [x] 4.1 Env-var-gated lift→verify driver in `Astronomy.Catalog.Tests` (`ImageLibrarySmokeTest`
  pattern; `INTENT_IMPORT_TS_DB` / `INTENT_IMPORT_STORE`): runs the import against real paths,
  prints counts/provenance/spot-check verification report — the runnable home for ISM's
  operational step
- [x] 4.2 Full x64 MSBuild + test run green, zero warnings; confirm additive-only (no diff to
  shipped derived-catalog surface; Contracts.Tests + `..\build-all.ps1` DRC green)
- [x] 4.3 Docs ride the ship commit: `docs/architecture/catalog.md` intent-store section,
  CHANGELOG entry, ROADMAP/CONSUMERS touch-ups as warranted
