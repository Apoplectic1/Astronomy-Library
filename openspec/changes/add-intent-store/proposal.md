# Proposal: add-intent-store

> **Seeded 2026-08-12 by ISM's `add-catalog-db-schema` change** (proposal only — AL sessions own
> specs/design/tasks under AL's conventions). **Schema authority:**
> `..\IntervalSchedulerManager\docs\design\catalog-db-schema.md` (+
> `catalog-inbox-contract.md`); architecture authority: the 2026-08-12 who-plans decision
> (`..\IntervalScheduler\docs\2026-08-12-who-plans-decision.md`). The behavior contracts are the
> ISM change's `catalog-db` and `ts-import` specs — implement against those, don't re-derive.

## Why

The who-plans decision made ISM the planning app and `Catalog.db` the system's sole durable truth
(authored intent: targets, desired counts, membership, policy, blessed plans — permanent,
user-owned, local, single-writer). The access layer is chartered to AL (IS ROADMAP retargeted
gap 4; precedent: portfolio store libraries live in AL, TSM already consumes `CatalogStore`), so
ISM, future stage-1 IS, and non-app harnesses (simulation, shadow-mode replay) can consume intent
without referencing an app. Nothing implements it yet; ISM's stage-0 build order is blocked on it.

## What Changes

- **New intent-store area beside/within `Astronomy.Catalog`** (exact namespace vs sibling-project
  call at implementation): embedded idempotent DDL for the schema doc's intent plane + minimal
  plan plane, enum lookups with companion CHECKs, all-FKs-indexed (`ix_` naming) — the schema
  doc's three gap closures over the shipped derived-catalog reference.
- **Migration framework** (R8–R10, first in the portfolio — the store is migrated, never rebuilt):
  `schema_migration` log + `user_version`, transactional `NNNN_name.sql` scripts, newer-than-app
  aborts.
- **Store API**: open/close with WAL checkpoint (TRUNCATE) on close (sync-safe at rest),
  local-path guard (reject UNC — fail fast), loud busy-failure (no second-writer interleave).
- **One-time TS importer**: Option-B lift per the schema doc §5 scope table — read-only source,
  all-or-nothing into an empty store, `imported_from_ts_guid` provenance on every row, R13
  translation maps (state, priority, the NINA→AL epoch map, twilight level) with abort-on-unmapped,
  sentinel→NULL translations, rule-#16 aborts on contract violations. Start from the shipped
  prior art (`TargetSchedulerReader` + the derived catalog's TS import).
- **Tests (xunit.v3)**: translation-map pinning (esp. the epoch map — SafeEpoch precedent),
  round-trip/count verification against the TS snapshot at
  `E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite`,
  migration-framework coverage, checkpoint-on-close/sync-safety.

## Capabilities

### New Capabilities

- `intent-store`: AL's Catalog.db access surface — schema stand-up, migration policy, store
  open/close invariants (consumer-agnostic wording; ISM is one caller).
- `intent-store-ts-import`: the importer's AL-side contract (mirrors ISM's `ts-import` spec from
  the library's perspective).

*(Refine at spec time — AL sessions may fold these into one capability or align names with AL's
existing spec organization.)*

### Modified Capabilities

*(none expected — the derived-catalog surface is untouched; recheck at spec time.)*

## Impact

- `Astronomy.Catalog` (new area; existing scan/reconcile surface untouched), AL test tree.
- Consumers: ISM (first), stage-1 IS and simulation/shadow harnesses (later), TSM only via the
  inbox file — **TSM never links this store API** (inbox decision D1, ISM change).
- Release: normal AL-releases-first flow; no app ships against this until ISM's app exists, so the
  gate is quiet.
- Unblocks: ISM `add-catalog-db-schema` tasks group 3 (one-time import + orphan cleanup) waits on
  this change shipping.
