# Design: add-intent-store

## Context

See `proposal.md` — Why. Authorities this design implements against (not re-derives):

- **Schema**: `..\..\..\..\IntervalSchedulerManager\docs\design\catalog-db-schema.md` — R1–R13,
  table inventory (intent plane + minimal plan plane), the three gap closures, import scope §5.
- **Behavior**: ISM change `add-catalog-db-schema` specs `catalog-db` / `ts-import`; mirrored here
  as this change's `intent-store` / `intent-store-ts-import` delta specs from the library's
  perspective.
- **Shipped prior art in this repo**: `Astronomy.Catalog\Schema\schema.sql` (rules down to column
  names), `GuidBlob` (big-endian RFC 4122), `SchemaManager` (open/configure pattern),
  `TargetSchedulerReader` (read-only TS access, explicit column lists),
  `ImageLibrarySmokeTest` (env-var-gated operational test pattern).

Constraints: **additive-only** beside the derived-catalog surface — TSM consumes
`Astronomy.Catalog` today and must not feel this land (no signature changes to shipped types).
**Consumer-agnostic** (portfolio rule #5): no downstream app names in public XML docs (say "the
caller" / "the owning application"), and no hardcoded imaging-tree paths — the store path is
always caller configuration (precedent: path defaults live consumer-side, e.g. TSM's
`DevDefaults.cs`). The one path-shaped rule that *does* belong here is behavioral: open rejects
non-local paths, fail-fast.

## Goals / Non-Goals

**Goals:**

- Land the intent-store area, migration framework, store API, and one-time TS importer in
  `Astronomy.Catalog`, fully tested, with the existing derived-catalog surface untouched.
- Give the one-time import a runnable home in AL — the operational lift (ISM D4) runs from a
  test-host driver before any consuming app exists.

**Non-Goals:**

- Executing the operational import itself (an ISM-side operational step inside a paused-GoodSync
  window — ISM change, migration plan step 3).
- Inbox-ingest plumbing, solver/plan authoring, back-projection to TS — later changes.
- Any change to the derived catalog's schema, `CatalogStore`, or the "shrinks to actuals cache"
  question.

## Decisions

### D1 — Home: `Astronomy.Catalog`, new `Intent` namespace (folder `Intent\`)

Per ISM D2's working shape: cohabitation, not a sibling project. The area reuses `GuidBlob`,
`Data\` helpers, and the TS-access idioms in place; a sibling project would duplicate or force
public-surface promotions of internals. Promote later only if the area outgrows cohabitation —
that promotion is a rename, cheap under rule #15.
*Alternative rejected:* `Astronomy.Catalog.Intent` sibling project — clean isolation, but pays a
project split before there is anything to isolate from.

### D2 — Schema ships as migration `0001_initial.sql`, applied only through the framework

Unlike the derived catalog's ensure-on-every-open idempotent DDL, the intent store's entire
baseline schema is migration 0001: embedded resources `Intent\Migrations\NNNN_name.sql`, applied
in order inside per-script transactions, each recording a `schema_migration` row (version, name,
applied_at) with `PRAGMA user_version` synced as the fast check. A fresh file is simply a store at
version 0 that migrates to latest on first open. One code path stands up and evolves the schema —
no drift between "create" and "migrate" DDL (R8–R10 in force from day one).
The DDL closes the reference schema's three gaps: companion `CHECK (col IN (...))` on every enum
column, per-FK `ix_<table>_<col>` indexes including enum-lookup FKs, and the framework itself.
Plan-plane `plan_interval` references its exposure spec as an `exposure_plan_id` FK (the schema
doc's implementation-time call — settled: reference authored intent; explicit override columns
arrive additively if the solver needs them).

### D3 — Store API: `IntentStore : IDisposable`, open = guard → migrate → configure

`IntentStore.Open(path)`:

1. **Local-path guard** — reject UNC paths (`\\` / `\\?\UNC`) and paths whose drive is a network
   drive (`DriveInfo.DriveType`), throwing before any file is created, naming the path and the
   local-only rule.
2. **Version gate** — `user_version` newer than the library's latest migration aborts (no write);
   older applies pending migrations in order; failure rolls back and rethrows naming the script.
3. **Configure** — `foreign_keys=ON`, WAL, `synchronous=NORMAL`, short `busy_timeout`; a busy
   database past the window surfaces the `SqliteException` unwrapped — loud, never a silent wait
   (single-writer is structural per the system invariant; the guard is belt-and-suspenders).

`Dispose` checkpoints WAL in TRUNCATE mode then closes — a closed store is one consistent file
(sync-safe at rest; the backup posture is the caller's concern, the mechanics are ours). No
default path constant anywhere in AL.

### D4 — Importer reads TS through its own read layer; shipped reader untouched

`TargetSchedulerReader`'s records carry the derived catalog's field subset; the import scope needs
more (description, lifecycle dates, meridian window, twilight level, moon-avoidance block, …).
Extending the shipped records would change public constructor signatures — exactly the TSM-felt
ripple this change forbids. So the importer (`Intent\TsImport\`) gets a dedicated read-only
reader with explicit column lists for the full in-scope field set, following the shipped reader's
idioms (read-only + shared cache open, busy timeout, never `SELECT *`).
*Alternative rejected:* widen `TargetSchedulerReader` — one reader, but a breaking record change
on a shipped surface for a one-time lift.

### D5 — Translation maps are code, pinned by tests; violations throw

R13 maps (project state, priority + `−1 → NULL`, twilight level, the NINA→AL epoch map) are
explicit dictionaries in the importer; unmapped values throw. Sentinel→NULL translations
(`minimumaltitude 0.0`, `exposure −1.0`, `readoutmode −1`, `priority −1`) are boundary mappings in
the same layer. Rule-#16 aborts are exceptions carrying entity/field/value/expectation — the
library throws; the *driver* owns console + log emission (a library writing to the console would
bake in a consumer's UX). The whole lift runs in one transaction on the target store; any throw
rolls back. Empty-store precondition checked first (any intent-plane row → refuse).
Enum *domains* are enumerated from the TS source clone (read-only reference) at implementation,
not guessed from the snapshot's observed values.

### D6 — The operational lift's runnable home: env-var-gated test-host driver

Following the `ImageLibrarySmokeTest` precedent: an env-var-gated xunit.v3 driver in
`Astronomy.Catalog.Tests` (`Intent\TsImportDriver`) that no-ops (with a "Skip: env not set" line)
unless `INTENT_IMPORT_TS_DB` / `INTENT_IMPORT_STORE` are set, then runs **lift → verify** against
the real paths: per-entity row counts vs direct source queries, provenance GUIDs resolve in the
source, and a lift-invariant spot-check on sampled field values. This is how ISM's group-3
operational step executes before any consuming app exists. Real paths arrive from the
environment — none are compiled into the library.
*Alternative rejected:* a console driver project — a new sln entry and release-surface questions
for a one-time operation the test host already handles.

## Risks / Trade-offs

- [Two stores answer to "Catalog.db" (derived vs intent)] → different namespaces (`Astronomy.Catalog`
  root vs `.Intent`), XML docs on the intent surface say "authored intent store"; on-disk ambiguity
  is resolved by ISM's orphan-file cleanup (their migration plan, not this change).
- [Migration framework is portfolio-first — untested conventions] → framework behavior gets direct
  tests (in-order apply, rollback-on-failure, newer-aborts, user_version sync) with throwaway
  migration scripts, not just the 0001 happy path.
- [Import mapping errors are silent corruption] → maps pinned by tests (epoch by name, per the
  SafeEpoch precedent), abort-on-unmapped, all-or-nothing transaction, driver verifies against the
  real snapshot.
- [Busy-failure semantics vs "fail loudly"] → short busy_timeout tolerates checkpoint-scale blips;
  past it the exception propagates unwrapped. Tested with a deliberate second writer.

## Migration Plan

Library-only change: ships in AL `main` per the normal AL-releases-first flow; nothing consumes it
until ISM's group 3, so rollback is `git revert` with no data in flight. The operational import
(paused GoodSync → orphan cleanup → lift → verify → resume) is ISM's step 3 and runs via D6's
driver.

## Open Questions

- None blocking. Plan-plane column growth on solver arrival is additive by construction (D2);
  namespace→sibling-project promotion (D1) is a later structural call that changes no behavior.
