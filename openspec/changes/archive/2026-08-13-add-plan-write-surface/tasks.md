# Tasks — add-plan-write-surface

## 1. Surface

- [x] 1.1 `PlanIntent` / `PlanIntervalIntent` schema-mirror records (required where DDL demands
      a value with no default; DDL defaults mirrored; raw UNIX-seconds timestamps; `night_of`
      the DDL's ISO-8601 local date string).
- [x] 1.2 `PlanWriter`: `UpsertPlan` (full-value, `created_at` create-only), `ReplaceIntervals`
      (delete + ordered insert, plan-id mismatch loud), `FindCurrentPlan` (non-superseded by
      profile+night; duplicate loud), `ReadIntervals` (sequence order) — every operation with
      `SqliteTransaction? transaction = null`.

## 2. Tests

- [x] 2.1 Plan round-trip + full-value update preserving `created_at`; state transitions via
      upsert (draft → superseded / blessed with stamp).
- [x] 2.2 `ReplaceIntervals`: whole-set replace (grow/shrink/reorder), FK chain resolves,
      plan-id mismatch throws, caller-transaction rollback discards plan + intervals together.
- [x] 2.3 `FindCurrentPlan`: none → null; draft found; superseded excluded; two non-superseded
      for one night throws.

## 3. Verification + docs (same commit)

- [x] 3.1 `dotnet build` zero warnings; `dotnet test` green on the full Catalog suite;
      `openspec validate add-plan-write-surface --strict`.
- [x] 3.2 `CHANGELOG.md` entry; `docs/architecture/catalog.md` Intent bullet gains the plan
      surface; `ROADMAP.md` digest updated.
