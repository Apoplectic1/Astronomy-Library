# Tasks: contracts-tests-refresh

## 1. Doc — extend the numbered list

- [x] 1.1 Add assumptions #19–#23 to CONSUMERS.md "Semantic assumptions" (wording per design D1;
      group under the existing category headers — #19/#20/#21 units/silent-wrong-result,
      #22/#23 call-order/lifecycle), and annotate #6/#10 as bench-covered once 4.x lands.

## 2. New-surface contract tests

- [x] 2.1 `EffectiveExposureContractTests` (#19): plan-override-wins, negative-sentinel-defers,
      both-null → 0; both overloads (catalog-side and raw-TS-side). No DB needed.
- [x] 2.2 `TargetSchedulerContractTests` additions or new file (#20): `ReadPlanEffectiveExposure`
      resolves sentinel through template; `Found=false` for unknown key and for missing template row.
      Temp-DB fixture per design D3. Pin current `> 0` behavior with the D4 cross-reference comment.
- [x] 2.3 `TsEditableSchemaContractTests` (#21, #22 classification half): enum codes pinned against
      known TS ints; `Find`⇔`For` agreement; `IsCadenceBreaking` ⇔ `Clears != None` for all fields.
      No DB needed.
- [x] 2.4 Editor cadence-clear contract (#22 DB half): same-transaction clear observable after a
      cadence-breaking edit; target-scope clear refused with `HasOverrideOrder` when override-order
      rows exist, DB untouched.
- [x] 2.5 `TargetSchedulerWriterContractTests` (#23): row count unchanged (update-only), desired
      ratchets up / never down, `DiskCount=0` writes zeros, journal mode untouched.

## 3. D4 adjudication flag

- [x] 3.1 While writing 2.1/2.2, document the exposure=0 divergence (`> 0` SQL vs `< 0` C#) in the
      test comments and collect it as a surfaced question for the apply summary — do NOT change
      production code.

## 4. Old-list gaps

- [x] 4.1 #10: add key-disambiguation test to `TargetSchedulerContractTests` — numeric string selects
      by Id, GUID string by guid (temp DB with a row addressable both ways).
- [x] 4.2 #6: add `Astronomy.Diagnostics` ProjectReference to the bench csproj; write
      `LogLifecycleContractTests` asserting pre-init `Log.*` is a silent no-op (no throw, no file).
      If process-global statics defeat a deterministic assertion, register #6 in
      `NotCleanlyTestableAssumptions` with the reason instead (design D2 fallback).

## 5. Registry refresh

- [x] 5.1 Rework `NotCleanlyTestableAssumptions.cs`: #18 entry reworded as retired (2026-07-06,
      sync-model rework); orientation list updated with #6/#10 and #19–#23 coverage; class doc
      comment still states the 1:1 audit property.

## 6. Verify + commit

- [x] 6.1 `dotnet test` on `Astronomy.Contracts.Tests` (pure-managed graph — no MSBuild needed);
      all green, skips limited to intentional registry entries.
- [x] 6.2 Audit pass: every CONSUMERS.md number 1..23 maps to a test header or registry entry
      (the spec's no-orphan scenario).
- [x] 6.3 Update Library CLAUDE.md/ROADMAP "Recently shipped" as warranted and commit docs + tests
      together (one commit, per design D5). Surface the D4 question in the wrap-up.
