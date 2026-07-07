# Design: contracts-tests-refresh

## Context

`Astronomy.Contracts.Tests` is the bench behind CONSUMERS.md's "Semantic assumptions" — one numbered
list, every item either covered by a deterministic test or registered in
`NotCleanlyTestableAssumptions.cs` with the reason it resists one. The list currently ends at #18
(retired 2026-07-06, number kept). Since it was written, the consumed surface grew
(`TsEditableSchema`, `ReadPlanEffectiveExposure`/`EffectiveExposure`, `TargetSchedulerWriter` +
`WriteBackPlanner`), and an audit of the bench shows drift: #6 and #10 are neither covered nor
registered, and the registry's #18 note still describes a live contract.

`Astronomy.Catalog.Tests` already unit-tests the new types. The bench's charter is different: it pins
the *consumer-baked-in* assumptions so that a Library change that silently violates one breaks a test
whose name points at CONSUMERS.md. Overlap with unit tests is acceptable; the bench test exists for its
label and its failure message, not for coverage.

## Goals / Non-Goals

**Goals:**
- CONSUMERS.md numbered list extended (#19+) to cover the grown TS surface; append-only numbering.
- Every numbered assumption accounted for in the bench (test or registry entry) — restore the 1:1
  audit property.
- Registry brought current (#18 reworded as retired; orientation list updated).

**Non-Goals:**
- No Library production-code changes. If a test exposes a behavior/doc mismatch, stop and surface it
  for decision (per collaboration rule: explain before fixing).
- No general unit-test expansion — only contract pins traceable to a CONSUMERS.md number.
- No pinning of Catalog.Tests-covered *implementation* details that no consumer bakes in.

## Decisions

### D1 — New numbered assumptions (#19–#23), one per compiler-invisible semantic

| # | Assumption | Why compiler-invisible |
|---|---|---|
| 19 | `EffectiveExposure.Seconds`: plan's own value when set, else template default; TS-side negative exposure = "use template default" sentinel; both-null → 0 (never matches a scanner bucket ≥ 1) | A sign/precedence change compiles and mis-buckets every reconciliation cell |
| 20 | `ReadPlanEffectiveExposure` resolves the sentinel *through the template* (same rule as #19); `Found=false` for unknown key or missing template row | TSM seeds edit UI from it; a raw read would show the sentinel |
| 21 | `TsEditableSchema.EnumValues` **codes are the persisted TS ints**; `Find`/`For` expose exactly the editable columns TSM's editors generate from | An enum-code renumber compiles and writes wrong ints into the TS DB |
| 22 | `TsEditableSchema.IsCadenceBreaking` / `TsField.Clears` classification gates the cadence-clear behavior (clear happens in the same transaction; target-scope clear refused when override-order rows exist) | Misclassification compiles and yields TS's silent-wrong-rotation state |
| 23 | `TargetSchedulerWriter.Execute` updates only existing `exposureplan.acquired`/`accepted` rows (never inserts/deletes rows, never touches journal mode); desired is ratcheted `max(old, new)`; `DiskCount=0` is a real write | TSM's push-as-replay trusts "update-only, ratchet-up" when presenting the diff |

Alternative considered: one umbrella "TS write-back semantics" number. Rejected — the list's value is
one assumption per line with an independent test; umbrella numbers rot the audit property.

### D2 — #6 and #10: pin, don't register

- **#6 (Log lifecycle):** testable — `Log.*` before `Init`/`StartNewSession` must be a silent no-op
  (no throw, no file). Hazard: `Log` is process-global static state, so the test must run before any
  `Init` in the same process — and no other bench test touches `Log`. Keep it that way (a comment in
  the test guards the constraint). Requires adding a `Astronomy.Diagnostics` ProjectReference
  (pure-managed; the bench stays `dotnet test`-able). If Diagnostics' statics make the no-op
  unobservable deterministically, fall back to a registry entry — decide at implementation.
- **#10 (write-back key = `ImportedFromTsGuid`, GUID-or-int-Id via `long.TryParse`):** cleanly
  testable against a temp SQLite DB — same row addressable by its guid string and by its Id as a
  decimal string; a numeric string that is a valid Id must select **by Id** (the disambiguation rule).
  Reuses the existing temp-DB helper pattern in `TargetSchedulerContractTests`.

### D3 — Fixtures mirror the established bench pattern

Temp SQLite via `Microsoft.Data.Sqlite`, `ClearAllPools()` after setup, `Cleanup` of `-wal/-shm/-journal`
— copied from `TargetSchedulerContractTests.cs` (which mirrors Catalog.Tests). New files, one per
surface: `EffectiveExposureContractTests`, `TsEditableSchemaContractTests`,
`TargetSchedulerWriterContractTests`; #10 joins `TargetSchedulerContractTests`; #6 gets
`LogLifecycleContractTests`. Schema-only tests (#21/#22 except the clear-transaction part) need no DB.

### D4 — Known nuance to adjudicate, not paper over

`ReadPlanEffectiveExposure` SQL uses `exposure > 0` (0 defers to template) while
`EffectiveExposure.Seconds(TsExposurePlan, …)` uses `< 0` (0 is taken literally). For exposure = 0 the
two disagree. The pin tests will make this visible; per Non-Goals, surface it to the user as a
behavior/doc question (which rule is TS's actual sentinel semantics?) rather than choosing silently.
Until adjudicated, the tests pin each method's *current* behavior with a cross-reference comment.

### D5 — Doc edits ride in the same commit

CONSUMERS.md (#19–#23 added; #6/#10 annotated as covered) and the registry rewording land in the same
commit as the tests (pinout revision = doc + tests in the same breath, per the CONSUMERS.md charter and
the user's docs-with-code rule).

## Risks / Trade-offs

- [Pinning current behavior freezes a bug (D4's 0-exposure divergence)] → tests carry an explicit
  "pins current behavior; divergence flagged" comment and the divergence is surfaced for decision in
  the apply summary.
- [#6's process-global static defeats a clean assertion] → pre-authorized fallback: registry entry
  with the reason (mirrors #2/#11 precedent); no scope creep into Diagnostics refactoring.
- [Writer tests touch transaction/journal behavior — flaky on temp-file cleanup] → same best-effort
  `Cleanup` pattern already proven in the bench; assertions read via a fresh read-only connection.
- [Numbering drift between CONSUMERS.md and test comments] → every new test's header cites its number
  and quotes the assumption line, same convention as the existing files.

## Open Questions

- D4's adjudication (exposure = 0 semantics) — flagged during apply; does not block the refresh.
