# Proposal: contracts-tests-refresh

## Why

The pinned contract grew: TSM's TS-editing and write-back work (shipped 2026-07-05/06) added a
consumed surface — `TsEditableSchema` (schema-driven field editors), `TargetSchedulerEditor.ReadPlanEffectiveExposure`
/ `EffectiveExposure` (the effective sub-length rule), and `TargetSchedulerWriter` + `WriteBackPlanner`
(write-back) — whose compiler-invisible semantics are exactly the class of assumption the contract bench
(`Astronomy.Contracts.Tests`) exists to pin, but none are pinned. The bench also drifted from the old
list: the #18 registry entry still describes `ClearAllPools()` as a live contract (retired 2026-07-06),
and #6 (Log lifecycle) and #10 (write-back key disambiguation) are neither covered nor registered.

## What Changes

- **Extend CONSUMERS.md's numbered semantic-assumptions list** with new entries (#19+) for the grown
  surface — numbering stays append-only/stable (existing numbers never shift; retirements keep their
  number, per the #18 precedent).
- **Add contract tests pinning the new surface** (silent-wrong-result class first):
  - `EffectiveExposure` rule: plan's own exposure when set, else template default; TS negative-value
    sentinel means "use template default"; both-null → 0 (never matches a scanner bucket).
  - `ReadPlanEffectiveExposure` resolves the sentinel through the template and reports `Found=false`
    for an unknown plan key.
  - `TsEditableSchema`: enum **codes** are the load-bearing persisted ints (a renumber compiles but
    writes wrong values to the TS DB); cadence-breaking classification gates TSM's Clears-scope UX;
    `For`/`Find` expose the columns TSM's editors are generated from.
  - Write-back: `TargetSchedulerWriter` updates existing exposure-plan rows only (never inserts/deletes);
    `PlannedWrite.DiskCount = 0` when no frames match is a real write, not a skip.
- **Close the two old-list gaps**: pin #6 (`Log.*` before `Init`/`StartNewSession` is a silent no-op)
  and #10 (editor write-back key = `ImportedFromTsGuid`, GUID string *or* TS int Id as decimal string,
  disambiguated by `long.TryParse`) — or, where a clean deterministic assertion doesn't exist, register
  them in `NotCleanlyTestableAssumptions` with the reason.
- **Refresh the registry** (`NotCleanlyTestableAssumptions.cs`): reword #18 as *retired* (matching
  CONSUMERS.md), and update the "covered elsewhere" orientation list to include the new tests.

## Capabilities

### New Capabilities
- `contract-assumption-pinning`: the invariant that every numbered semantic assumption in CONSUMERS.md
  is accounted for in the bench — either covered by a deterministic contract test or explicitly
  registered as not-cleanly-testable with the reason — and that the numbered list itself is append-only
  (retired items keep their number).

### Modified Capabilities
_None — no existing specs._

## Impact

- **`Astronomy.Contracts.Tests`** — new test files for the TS surface (effective exposure, editable
  schema, writer/write-back), possible additions for #6/#10, registry file reworded. No production
  code changes.
- **`CONSUMERS.md`** — semantic-assumptions list extended (#19+); orientation notes updated.
- **No Library behavior changes** — tests pin current behavior only. If a test exposes a behavior/doc
  mismatch, that's surfaced for decision, not silently patched.
- Build/test: the bench is pure-managed → `dotnet build`/`dotnet test` on the project is fine
  (per VERIFICATION.md; no vcxproj in this graph).
