# contract-assumption-pinning

## Purpose

Pin every numbered CONSUMERS.md "Semantic assumptions" entry in the `Astronomy.Contracts.Tests` bench — each assumption is either covered by a deterministic contract test that cites its number, or registered in `NotCleanlyTestableAssumptions.cs` with the reason it resists a deterministic assertion. Retirements keep their numbers in both the doc and the registry.

## Requirements

### Requirement: Every numbered assumption is accounted for in the bench
Every numbered entry in CONSUMERS.md "Semantic assumptions" SHALL be accounted for in
`Astronomy.Contracts.Tests` — either covered by at least one deterministic contract test whose header
cites the number, or listed in `NotCleanlyTestableAssumptions.cs` with the reason it resists a
deterministic assertion. Retired assumptions SHALL keep their number and be marked retired in both the
doc and the registry.

#### Scenario: Audit finds no orphan numbers
- **WHEN** the CONSUMERS.md numbered list is compared against the bench (test headers + registry)
- **THEN** every number 1..N appears exactly once as covered, registered, or retired — no number is
  absent from the bench

#### Scenario: Retired assumption stays numbered
- **WHEN** an assumption is retired (e.g. #18, 2026-07-06)
- **THEN** its number is not reused and the registry entry describes it as retired, matching CONSUMERS.md

### Requirement: Effective-exposure rule is pinned (#19)
The bench SHALL pin `EffectiveExposure.Seconds`: a plan's whole-second effective exposure is its own
value when set, else its template's default; on the raw TS side a negative exposure is the
"use template default" sentinel; when both plan and template values are null the result is 0.

#### Scenario: Plan override wins
- **WHEN** a plan has its own exposure value and the template has a different default
- **THEN** `Seconds` returns the plan's value (rounded to whole seconds)

#### Scenario: Sentinel defers to template
- **WHEN** a raw TS plan's exposure is negative
- **THEN** `Seconds` returns the template's default exposure (rounded)

#### Scenario: Both null yields the never-matching bucket
- **WHEN** a catalog-side plan and template both carry null exposure
- **THEN** `Seconds` returns 0 (which can never equal a scanner bucket, all ≥ 1)

### Requirement: Editor effective-exposure read resolves through the template (#20)
`TargetSchedulerEditor.ReadPlanEffectiveExposure` SHALL return the plan's effective exposure resolved
through its template (same rule family as #19) keyed by guid-or-Id, and SHALL report `Found = false`
for an unknown key or a plan whose template row is missing.

#### Scenario: Sentinel resolved through template
- **WHEN** a plan row carries the defer-to-template sentinel and its template row has a default
- **THEN** the read returns `Found = true` with the template's default, not the sentinel

#### Scenario: Unknown key
- **WHEN** the key matches no exposure plan
- **THEN** the read returns `Found = false`

### Requirement: Editable-schema enum codes and field set are load-bearing (#21)
`TsEditableSchema` SHALL expose, per table, exactly the editable columns consumers generate editors
from (`For`/`Find`), and every `TsEnumValue.Code` SHALL equal the integer the TS database persists for
that label.

#### Scenario: Enum codes match persisted ints
- **WHEN** an enum-typed field's `EnumValues` are enumerated
- **THEN** each code equals the TS-persisted integer for that label (pinned per enum against known TS
  values)

#### Scenario: Find agrees with For
- **WHEN** `Find(table, column)` is called for every column in `For(table)`
- **THEN** it returns that same field, and returns null for a column not in the set

### Requirement: Cadence-breaking classification gates clears (#22)
`TsEditableSchema.IsCadenceBreaking` SHALL agree with the field's `Clears` scope, and a cadence-clearing
edit through `TargetSchedulerEditor` SHALL clear the invalidated cadence rows in the same transaction as
the column write; a target-scope clear SHALL be refused (`RefusalReason.HasOverrideOrder`) when the
target has hand-authored override-order rows.

#### Scenario: Classification agrees with Clears scope
- **WHEN** `IsCadenceBreaking(table, column)` is compared with `Find(table, column).Clears` for all fields
- **THEN** they agree (breaking ⇔ scope ≠ None)

#### Scenario: Override-order rows refuse a target-scope clear
- **WHEN** a target-scope cadence-clearing edit targets a row whose target has override-order rows
- **THEN** the edit is refused with `HasOverrideOrder` and the DB is untouched

### Requirement: Write-back is update-only with a ratcheted goal (#23)
`TargetSchedulerWriter.Execute` SHALL update only existing `exposureplan` rows' `acquired`/`accepted`
(never inserting or deleting rows, never altering the journal mode), SHALL ratchet `desired` to
`max(old desired, new count)` (raised, never lowered), and SHALL treat a zero disk count as a real
write.

#### Scenario: No row creation or deletion
- **WHEN** a write-back plan is executed against a db
- **THEN** the `exposureplan` row count is unchanged and only `acquired`/`accepted`/`desired` values
  differ

#### Scenario: Desired ratchets up only
- **WHEN** the new count exceeds the old desired
- **THEN** desired becomes the new count; **WHEN** the new count is below the old desired **THEN**
  desired is unchanged

#### Scenario: Zero disk count writes zero
- **WHEN** a planned write carries `DiskCount = 0`
- **THEN** the row's acquired/accepted become 0 (not skipped)

### Requirement: Old-list gaps #6 and #10 are closed
The bench SHALL account for assumption #6 (`Log.Init` → `StartNewSession` must precede any `Log.*`,
else silent no-op) and #10 (editor write-back key is `ImportedFromTsGuid` — GUID string or TS int Id as
decimal string, disambiguated by `long.TryParse`) — by test where deterministic, else by registry entry
with the reason.

#### Scenario: Pre-init logging is a silent no-op
- **WHEN** a `Log.*` call is made before `Init`/`StartNewSession` in a fresh process
- **THEN** it neither throws nor produces output (or, if not deterministically assertable, #6 is
  registered with the reason)

#### Scenario: Numeric key selects by Id
- **WHEN** an editor write keys a row with a string that parses as a long
- **THEN** the row is selected by `Id`, not by `guid` — and a GUID string selects by `guid`

### Requirement: Registry reflects retirements and current coverage
`NotCleanlyTestableAssumptions.cs` SHALL describe #18 as retired (matching CONSUMERS.md 2026-07-06) and
its orientation list SHALL name the covering test class for every covered assumption, including the new
#19–#23 pins.

#### Scenario: Registry orientation list is current
- **WHEN** the registry's "covered elsewhere" list is compared with the bench's test files
- **THEN** every covered assumption maps to an existing test class and no entry describes a retired
  contract as live
