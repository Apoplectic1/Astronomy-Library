# CHANGELOG.md

**Charter.** Shipped history for the `Astronomy` library — **append-only, dated, newest first**. One
section per shipped unit: what landed and why it mattered. Read to answer "*when* did X land, and what
shape did it land in?" Forward-looking design and the short recently-shipped digest live in
`ROADMAP.md`; how the code works today lives in `ARCHITECTURE.md`. Git remains the commit-level
backstop — this is the human-legible layer above it.

**Entry format:** `## YYYY-MM-DD — <what landed>` (a month-only `YYYY-MM` is fine when the exact day
wasn't recorded). Newest first; add new entries directly below this charter, never at the bottom.

## 2026-07-24 — UTC contract gate + azimuth `[0, 360)` fold-back

Two defects surfaced by the docs audit, both fixed at the source rather than documented around.

**Azimuth could return exactly `360.0`**, violating the documented half-open `[0, 360)`.
`TargetGeometry.AzimuthAtHourAngle` clamps `cosAz` to `1.0` (needed for near-pole / near-zenith float
overshoot); `Acos(1.0)` is exactly `0.0`, and the eastern-half flip then computed `360.0 - 0.0`.
Nothing downstream renormalized — `AltAz`'s ctor stores verbatim — so the out-of-range value reached
consumers. Not a pole-only curiosity: a sweep found 9,213 hits, and at Penns Park (40.3°N) **M81 and
Polaris at upper transit** both returned `360.0`, i.e. any target north of the zenith at its best
moment. Fixed with a fold-back at the source; pinned by a spot Theory plus a swept invariant test.

**Non-UTC `DateTime` silently produced wrong answers.** `JulianDate.FromUtc` is `ToOADate() + 2415018.5`,
and `ToOADate()` ignores `Kind` entirely — so a `Local`/`Unspecified` instant was reinterpreted as UTC,
an error of the caller's UTC offset (~75° of hour angle at EST). `AltAzCalculator.At` and the whole
`Session/` cluster never normalized; only `AstroUtil`, `MoonEphemeris`, `NightCalculator` and `Sun/*`
called `TimeKindGuard.AsUtc`. Per the fail-fast rule, `FromUtc` is now the **single central contract
gate** and throws `ArgumentException` on a non-`Utc` kind — chosen over per-entry-point guards because
every time-based primitive funnels through it (`SiderealTime.Local` routes there; normalizing callers
arrive as `FromUtc(AsUtc(x))`), so downstream code needs no guards at all. New
`TimeKindGuard.RequireUtc` carries the rejection message. A consumer audit confirmed **TP and TSM both
already satisfied the invariant by construction** (TP funnels through a single `ObservationMoment`; TSM
through `DateTime.UtcNow`), so this landed as a runtime no-op with no consumer change — it converts a
latent silent-wrong-answer class into a loud failure. `NightCache.ComputeYearStartDay` preserves `Kind`,
so a non-UTC seed now fails loudly at first use instead of propagating an offset year-long grid.

780 tests pass across all five projects (+9: azimuth spot/sweep, guard reject/accept/no-convert).
`CONSUMERS.md` assumption **#16 widened** from "`LunarAge.DaysAt` throws" to the library-wide rule.

**Docs audit remediation (same day).** Both defects above came out of a two-round audit of the
reference set (14 workers, ~60 flags, model-diversified — the second round found 39 new flags over the
first, so coverage is not claimed complete). The doc fixes that landed with it:

- **`CONSUMERS.md` caught up with TSM's Core adoption** (2026-07-23, `a48b2fa`): the TSM row had
  claimed "Core not referenced", the graph was missing the edge, and the whole `Catalog.Schema`
  namespace was absent despite ~127 TSM references. The dead-surface list now **names** the genuinely
  uncalled `Session` members — its previous unnamed "several `Session.*`" would have sanctioned
  pruning `SessionSolvers`/`TargetOrdering`, which TP calls from four sites.
- **`VERIFICATION.md` build recipe actually works on a clean clone**: `msbuild` doesn't restore
  implicitly and there's no restore hook, so the "REQUIRED" line needed `-restore`; VS2022 is now
  stated as impossible rather than "likely also works" (the vcxproj pins toolset `v145`, VS2026-only);
  the four missing projects and the PCL native prerequisite chain are documented.
- **Placement**: ARCHITECTURE had become the orphanage for forward-looking plans with no roadmap home
  — `ObservationSession`, NINA Phases C–D, and the XISF Tier 2-4 scope all moved to `ROADMAP.md`, as
  did the public-surface retention decision (which was the only library-level record of the ISP plan,
  invisible to anyone following the router's "forward-looking → ROADMAP" rule).
- **Corrections worth naming**: "altitude is unrefracted" was false as a universal; the hemisphere
  ctors XOR rather than let sign override; `TargetSchedulerWriter` has no dry-run *default*; the
  vendored PCL tree is 19 GB not ~10 GB, its solution builds 46 projects not 8, and the v145 bump
  spans ~46 project files — so the re-snapshot caveat had been under-scoping the re-apply work.
- **AVX-512 caveat corrected** (`ARCHITECTURE.md` § PCL): the claim that "PCL's own AVX-512 paths
  remain runtime-gated" was wrong in both halves — PCL's source contains no AVX-512 paths to gate, and
  `PCL.vcxproj` compiles the static lib with AVX-512 codegen *unconditionally*. The wrapper's AVX2
  setting therefore does not buy the portability it claimed; flagged as an illegal-instruction risk on
  non-AVX-512 hardware, with the remedy (rebuild PCL at AVX2) noted.

Two report-only items were **not** applied: the parent portfolio router's `Catalog.db` description
contradicts this repo (a cross-repo edit), and `archive/PCL-WrapperRoadmap.md`'s Phase A premise was
overtaken by `Astronomy.XISF` — now flagged in `ROADMAP.md` rather than silently rewritten.

## 2026-07-07 — Contracts.Tests refresh: TS surface pinned (#19–#23), #6/#10 gaps closed

The contract bench caught up with the grown pinout. CONSUMERS.md "Semantic assumptions" extended
append-only with **#19–#23** (TS editing / write-back): the `EffectiveExposure` rule (own value else
template default; negative = TS sentinel; both-null → 0), `ReadPlanEffectiveExposure` template
resolution, `TsEditableSchema` enum codes = persisted TS ints + the exact cadence-breaking set,
same-transaction cadence clears + `HasOverrideOrder` refusal, and writer update-only/ratcheted-desired
semantics — each pinned in a new bench test file. Old-list gaps closed: **#6** (pre-init `Log.*` is a
silent no-op — bench gained the `Astronomy.Diagnostics` ref) and **#10** (edit key guid-or-Id via
`long.TryParse`; by-Id wins for digit strings). Registry reworded **#18 as retired**. Bench: 52 pass +
6 intentional skips; every number 1..23 now maps to a test or registry entry. **Adjudicated same day**:
exposure = 0 had diverged — `ReadPlanEffectiveExposure`'s SQL deferred 0 to the template (`> 0`) while
`EffectiveExposure.Seconds`' raw-TS overload took it literally (`< 0`). The TS source rules for literal:
its planner's sentinel test is exactly `!= -1` (`PlanningExposure.cs`; the `<= 0`-as-unset spot is the
sync client, not the planner). The SQL was aligned to `< 0`; all three Library sites (SQL, raw-TS
overload, `TargetResolver`'s import normalization) now agree, tests pin the agreement.

## 2026-07-06 — cadence-safe TS editing: transactional clear + OEO refusal

`TsField.CadenceSafe : bool` → `Clears : TsCadenceClear` (`None`/`Target`/`Project`; breaking, no shim).
`TargetSchedulerEditor` now honors the scope: the column UPDATE and the scoped
`DELETE FROM filtercadenceitem` run in ONE transaction (TS restores cadence rows verbatim and regenerates
only from empty — update-without-clear is the silent-wrong-rotation state this prevents); unchanged values
are verified no-ops (no write, no clear — mirrors TS's own `!=` checks); a target-scope edit refuses with new
`RefusalReason.HasOverrideOrder` when hand-authored `overrideexposureorderitem` rows exist (project scope
passes through, mirroring TS's fsf path). Scopes: `exposureplan.enabled` → Target,
`project.filterswitchfrequency` → Project. 170 tests (+ scoped-clear/no-op/OEO/rollback-atomicity over real
temp dbs — a trigger forces the DELETE to fail and proves the UPDATE rolls back too).

## 2026-07-06 — TsEditableSchema: full exposuretemplate surface

11 new exposuretemplate rows (18 total): `twilightlevel` (new `TwilightLevel` enum map — Nighttime/
Astronomical/Nautical/Civil, codes from the TS source; the column spelling is TS's own EF rename of
`twilightlevel_col`), `minutesoffset` (±720, negatives legal), the moon avoidance suite (`moonavoidanceenabled`,
`moonavoidanceseparation` 0–180°, `moonavoidancewidth` 0–30 d, `moonrelaxscale`,
`moonrelaxmaxaltitude`/`moonrelaxminaltitude` −90–90° — TS ships −15, so the floor must admit negatives), `moondownenabled`, `ditherevery`, `maximumhumidity` (0–100 %, 0 =
disabled). All cadence-safe (template columns are scoring/filter inputs; nothing clears `FilterCadenceItem`).
Reference-driven consumers render them with zero UI code. 163 tests (+ surface/bounds/enum pins).

## 2026-06-28 — CONSUMERS.md datasheet + Astronomy.Contracts.Tests bench

`CONSUMERS.md` stakes out the Library's **de-facto public contract** — the "pinned pinout" derived from grep-verified real usage: who consumes the Library and how (only **TP + TSM**, by `ProjectReference`/source), the surface each depends on, 18 semantic assumptions (contract-test candidates), fragility flags, and a design-review decision on the large dead/speculative public surface (**keep, don't prune** — it's API ahead of its planned consumers: the ISP plugin, the TSM write-back action, XFM's Library migration; not cruft).

`Astronomy.Contracts.Tests` (13th buildable project) is the pure-managed xUnit-v3 **bench that pins those assumptions** — a red test = a violated contract. 17 testable assumptions green / 6 documented-not-cleanly-testable: e.g. `RaDegrees`=degrees, `LunarAge.DaysAt` UTC-guard, `MoonEphemeris.Sample` exact count, `MoonSeparation.ObserveAt` geometric-not-apparent, `CatalogGraph` FK-order + mosaic-panels-after-parent, `ScanAsync` throws on missing root, the editor's required-columns refusal (DB untouched), `SkyBrightness.KsAt` pinned golden value (locks the 10-param order). Run by the constellation DRC `..\build-all.ps1`. (The same pass also restructured the docs into the lean-router `CLAUDE.md` + per-module `ARCHITECTURE.md` / `VERIFICATION.md` set.)

## 2026-06-26 — TargetSchedulerEditor: guarded TS edit path

A write layer for the catalog → TS edit story, sibling to the Reader/Writer, built up 2026-06-11 → 06-26. `TargetSchedulerEditor.TrySetField(table, key, column, value)` is the guarded entry point: it folds four safety predicates (required columns present / file writable / no open sidecar / column present) into one structured-refusal call returning `(FieldEditResult?, RefusalReason)` (`None`/`SchemaIncompatible`/`ReadOnly`/`OpenSidecar`/`ColumnAbsent`) — consumers map the reason to their own wording; the library names none. The editor drives off a declarative `TsEditableSchema` data dictionary naming every user-editable TS column (table / exact SQLite column / label / value type / cadence-safety / enum-range) — which also doubles as the SQL-injection whitelist and an open-time schema-drift guard. It resolves rows by guid-or-Id, UPDATEs one column, and read-back verifies. Cadence-safe columns write plainly; cadence-breakers (`exposureplan.enabled`, `project.filterswitchfrequency`) are flagged, not specially handled — the caller must warn or defer. `ReconciliationProjection` grew the cell-granularity join (lifted from the consumer's grid loader) plus the write-back provenance addresses (`PlanTsKey` / `TemplateTsKey` / `ProjectTsKey`, `TargetCells.Enabled` / `TsTargetKey`). 2026-07-06: the reference also carries declarative enum value maps (`TsEditableSchema.EnumValues` → ordered `TsEnumValue(Code, Label)` per `TsField.EnumName`, incl. `TargetPriority`'s `-1 Default`) so a consumer builds selection controls without hard-coding TS codes.

## 2026-06-21 — SQLite CVE pin (CVE-2025-6965)

Direct-pinned `SQLitePCLRaw.bundle_e_sqlite3` to **3.0.3** so it overrides the vulnerable native engine 2.1.11 that `Microsoft.Data.Sqlite` 10.0.9 pulled transitively (CVE-2025-6965 / GHSA-2m69-gcr7-jv3q, high, affects ≤ 2.1.11; no 2.1.12 — the package renumbered to a 3.x line). Native lib 3.50.3 = SQLite ≥ 3.50.2 (patched). NU1903 cleared with no NU1605 downgrade; the fix flows to every `Astronomy.Catalog` consumer. (Paired with a routine `Microsoft.Data.Sqlite` 10.0.3 → 10.0.9 bump.)

## 2026-06-21 — xUnit v3 migration + Core-benchmark split

Every test project migrated **xUnit 2.9.3 → xUnit.v3 3.2.2** (`Microsoft.NET.Test.Sdk` → 18.6.0; `xunit.runner.visualstudio` on the `4.0.0-pre` line for VS2026 / .NET 10). Because v3 **generates the assembly entry point**, it collided with `Astronomy.Core.Tests`' custom `BenchmarkSwitcher` `Main` — so the BenchmarkDotNet harness (`Program.cs` + `Benchmarks/`) split out into a new pure-managed **`Astronomy.Core.Benchmarks`** exe (references only `Astronomy.Core`'s public surface, no `PCL.Native` → runs under plain `dotnet run -c Release`, no SLN/MSBuild). With the native graph gone, BDN's default out-of-process toolchain replaced the old `InProcessEmit` config (better-isolated numbers). New trap captured: **never let `xunit.v3` land on a non-test project** — a bulk "apply to all projects" NuGet action sprayed it onto 4 production projects (its `mtp-v1` targets force `OutputType=Exe`; the break only surfaces on a later version bump). 468 Core tests pass.

## 2026-06-11 — Astronomy.Diagnostics: shared logging + screen-capture contract

New pure-managed library (11th buildable project) hosting the portfolio's diagnostic **conventions as code**, factored out of per-app copies (TP and TSM had hand-ported duplicates that drifted). `Log`: an append-only `%APPDATA%\<app>\Logs\` trail with a fixed line grammar and two verbosity axes — always-on Info/Warn/Error severity (survives Release) + gated `Diag` channels (default all-in-Debug / none-in-Release, per-channel runtime toggle via the app's env var) — plus session rotation, local-time stamps, and the `USER_OBS_*` observation protocol (START/CAP/END/CANCEL share an id). Configured once per app via `AppLogIdentity` (a shared library compiles once and can't read the consumer's `#if DEBUG`). `ScreenCapture.ToPng`: the System.Drawing `CopyFromScreen` grab, framework-agnostic (WinForms or WinUI). The method surface *is* the convention; the implementation enforces the invariants so an off-convention line can't be emitted. **Open follow-up — `ObservationSession`:** now that two live consumers each duplicate the dialog START/CAP/END/CANCEL wiring, factor it into the library (detail in `ARCHITECTURE.md`).

## 2026-06-10 — mosaic-panel resolver + exposure-aware write-back

Two reconciliation deepenings landed across the Catalog this day.

**Mosaic panels became first-class targets.** A mosaic is now one parent `target` row plus one child row per panel (self-FK `parent_target_id`, `ON DELETE CASCADE`; composite directory-name `<mosaic dir>/<panel label>`). After several iterations the model converged on **"a panel is a normal target whose key is composite"**: the scanner retains per-panel `TargetReport.Panels` from the same walk, panels enter the working set as ordinary units, and the **one** standard resolve loop does nearest-coordinate anchoring, duplicate/alias reporting, and Both/Actual/Planned classification. Mosaic-specific logic shrank to coordinate **scope keys** (a panel anchors only among its mosaic's panels; standalone targets never see panels — cross-scope matches impossible by construction) and a panel-token name facet (`Panel 01of16` → `P1`). Aligned claims **outrank** unaligned ones (an unshot panel inside tolerance of a shot neighbour stays planned instead of false-folding — the Witch Head shape). `ManualReason.Mosaic` retired: panels auto-write like any target; the plan-less / inventory-less parent is inert by construction.

**Exposure time joined the identity.** The scanner buckets inventory per `(filter, purpose, whole-second exposure)` instead of folding sub-lengths into one mode value; `inventory_filter` gained `exposure_seconds` in its PK. The write-back key became `(target, filter, purpose, whole-second exposure)` — **the plan's seconds is the spec**: a plan receives the disk count at exactly its `round(ExposureSeconds ?? template default)` bucket (0 = a flagged decrease). Same-purpose plans at different durations now auto-resolve into separate writes instead of routing to manual; disk buckets with no plan surface as `UnplannedFrames` notes (write-back updates existing rows only — plan creation/deletion is a later milestone). Alias-aware dup-folds (option B): ≥2 TS names that each exactly equal a disk identity facet (M27 + Dumbell) are one `AliasTsTarget`, auto-written to every member when plan-count = alias-member-count. `EffectiveExposure` single-sourced across both planners. (Schema change — `Catalog.db` is derived; delete and rebuild, no migration.)

## 2026-06-08 — catalog write-back to TS (`TargetSchedulerWriter`)

The catalog reconciles disk (actual) ↔ TS (plan) onto one canonical target and computes goal-vs-actual; **Phase 4
write-back shipped**. `TargetSchedulerWriter` + the pure `WriteBackPlanner` write reconciled disk-derived counts
back into a **local copy** of TS's `schedulerdb.sqlite`, mapping catalog → exact TS rows via the retained
`imported_from_ts_guid` provenance (TS's own `acquired_count` was badly stale vs disk — e.g. 0 H frames vs 140 on
disk). A surgical single-target path (`SingleTargetPlanner` + `ImageLibraryScanner.ScanUnitsAsync`, driven by `tcm
writeback --target`) updates one target — **per panel for a mosaic** — without a catalog rebuild. Driven from TCM
(since renamed TSM — `E:\Projects\VisualStudio\Astronomy\TargetSchedulerManager`, ROADMAP Phase 4; the CLI verbs
retired 2026-06-11, engine resurfaces as an app action); operates on a local copy (the live TS
DB lives on the imaging PC — cross-machine WAL caveat). ~~Open: alias-vs-duplicate handling for `M27`/`Dumbell`.~~
**Resolved 2026-07-23:** the alias-fold mechanism was removed in full (`AliasTsTarget` / `AliasMemberCount` /
`TargetMatchIssues.Alias` / the planner's member-count exemption) — the M27/Dumbell twin it waved through was
adjudicated unintentional, so a multi-claim is always a flagged `DuplicateTsTarget` and its multi-plan cells hold
as `ManualGroup(DuplicateFold)`; one TS row per position, no exceptions (TSM `NOTEBOOK.md` 2026-07-08 correction).

## 2026-06 — Astronomy.Catalog: goal-vs-actual reconciliation

`Reconcile/` joins TS goals to disk actuals so consumers can answer "how close is each target/filter to its
goal." Goals = `exposure_plan.desired_count` (summed per filter via the template); actuals = disk
`inventory_filter` — **not** TS's own `acquired_count`, which is often badly stale (real Wizard example: TS said
0 H frames, disk had 140). Join key is `(target, filter_name)` — both planes already use the same single-letter
names, so no normalization. `ReconcilePolicy.Combined` (default) counts Light + Stars toward a goal (fits
shooting RGB only as Stars for star colour); `LightOnly` excludes Stars. `Reconciler` (pure) +
`CatalogStore.GetReconciliation` → per-(target,filter) desired/acquired/remaining/% + a target rollup status
(NotStarted/InProgress/Complete/Unplanned). First real run: 101 planned targets (6 complete / 33 in-progress /
62 not-started), 8221/30088 frames done; the TCM host prints the summary. 42 Catalog tests pass.

## 2026-06 — Astronomy.Catalog: disk(actual) + TS(plan) reconciled onto one canonical target

Follow-on to the Catalog/scanner entry below. The two parallel target tables collapsed into **one canonical
`target`** carrying both facets (disk identity + plan attributes), discriminated by `source_id` — `Actual`
(on disk only), `Planned` (in TS only / not yet shot), `Both` (merged). The disk library is ACTUAL (truth);
TS is the PLAN; the catalog re-organizes the plan clean and anchored to actual. `inventory_filter` (actuals)
and `exposure_plan` (goals) both hang off the one target.

- **`Build/TargetResolver`** (pure, unit-tested): **coordinate-primary** match — each TS target anchors to the
  nearest disk target within a tolerance (default 0.5° haversine; unaligned mosaic-panel claims 0.1°); name only validates; disk plate-solved coords
  win on merge; **the TS guid is retained on `Both` for the planned write-back-to-TS path**; TS duplicates fold
  onto one canonical, and name-mismatch / ambiguous / unanchored / out-of-range rows are reported in
  `CatalogBuildReport` (surfacing TS's "problems and errors"), not dropped.
- **`Build/CatalogBuilder.BuildAsync`**: full rebuild = scan disk + read TS → resolve → `WriteCatalog` in one
  transaction. Either source may be omitted (library-only → actuals-only; TS-only → planned-only).
- **`CatalogStore.WriteCatalog(graph)`** replaces `ImportPlan`/`ReplaceInventory`; `GetShotTargets()`
  (source `Actual`|`Both`) is XFM's actual-only view (a `Both` target has frames on disk, so it belongs;
  planned-only excluded).
- **Harden:** never pass a raw TS integer into a CHECK/FK column — unknown epoch/state/priority codes coerce to a
  safe default and planned RA/Dec normalize/clamp, so one bad external TS row can't abort the rebuild. (From an
  adversarial review: 3 confirmed of 13 raised, all this root cause.)
- Dropped `inventory_target` (folded into the canonical `target`) and `TsCatalogImporter` (absorbed into the resolver).

Astronomy.Catalog.Tests 36 + Astronomy.NINA.Tests 45 pass, 0 warnings.

## 2026-06 — Astronomy.Catalog: catalog DB + library-scanner home

New `Astronomy.Catalog` library (9th + 10th projects) owns `Catalog.db` and the shared
`.xisf`-library scanner moved out of `Astronomy.NINA`.

- **Scanner moved** `Astronomy.NINA.Xisf` → `Astronomy.Catalog.Scan` (`ImageLibraryScanner`
  + `ImageLibraryReport`/`TargetReport`/`FilterAggregate`/`TypicalSettings`/`FilterPurpose`);
  it depended only on `Astronomy.XISF`. `ReportToTargetAdapter` stays in NINA as the
  scan→`Target` bridge, so NINA now references `Astronomy.Catalog` (planning consumes
  inventory; no cycle). TP unaffected (it has its own scanner).
- **No migration framework**: the catalog is fully derived (scan + TS import; goals live in
  the scheduler DB) and rebuildable; `SchemaManager` applies one idempotent `schema.sql`.
- **Aggregate inventory**: `inventory_target` + `inventory_filter` (1:1 of `TargetReport`/
  `FilterAggregate`); `CatalogStore.ReplaceInventory(report)` persists a scan transactionally.
  *(Superseded — `inventory_target` later folded into the canonical `target` and `ReplaceInventory` → `WriteCatalog`; see top entry.)*
- Plan plane (profile/project/target/exposure_template/exposure_plan) + read-only
  `TargetSchedulerReader` for TS's `schedulerdb.sqlite`. *(TS→Catalog import + disk reconciliation shipped — see top entry.)*

Astronomy.Catalog.Tests 28 + Astronomy.NINA.Tests 45 pass; TargetPlanner builds.

## 2026-06 — Astronomy.XISF.Compression: shared zlib+sh + SHA-1 codec

Ported XFM's image-block codec into `Astronomy.XISF` (`Compression/`): byte-shuffle + zlib
(max level) + SHA-1, symmetric `Compress`/`Decompress`, plus `BlockCompressionInfo`
(compression/checksum attribute parse/format) — the Tier-4 "compression + checksum"
foundation. XFM consumed it briefly (`e1cd34a`) but the adoption was reverted (XFM `2cd23fc`,
2026-06-08) — XFM still runs its local copy; the migration stays planned. 8 codec tests (34 XISF total).

## 2026-05-28 — MoonEphemeris + AltitudeCurve.Sample reshape

Extracted the per-minute observational compute that TargetPlanner rolled
inline into pure-function AL primitives. Designed so the planned
IntervalScheduler Plugin (ISP) cache can consume the same surface
without re-porting. No AL-side caching (pure functions per the "no
static mutable state in Core" contract); consumers memoize at their own
scope.

**New: `Astronomy.Core.Moon.MoonEphemeris`**

```csharp
public static IReadOnlyList<MoonSample> Sample(
    Location location, DateTime startUtc, TimeSpan step, int count);
```

Each `MoonSample` carries topocentric `AltDegGeometric` +
`AltDegApparent` + `AzDeg` + `DistanceKm` + `AgeDays` + `PhaseAngleDeg`
+ `IlluminatedFrac`. All positions parallax-aware via
`MoonPosition.Topocentric`; apparent altitude via
`Refraction.SaemundssonDeg`; age via `LunarAge.DaysAt`; phase via
`SkyBrightness.PhaseAngleDegFromAgeDays`; illumination via
`MoonIllumination.Fraction`.

**Reshaped: `Astronomy.Core.Session.AltitudeCurve.Sample`**

```csharp
// Before:
public static IReadOnlyList<double> Sample(
    Target target, Location location, DateTime startUtc, TimeSpan step, int count);

// After:
public static IReadOnlyList<AltAzSample> Sample(
    Target target, Location location, DateTime startUtc, TimeSpan step, int count);
```

Each `AltAzSample` carries `AltDegGeometric` + `AltDegApparent` + `AzDeg`.
Internal linear LST advance optimization preserved (~2.6× faster than
per-sample `AltAzCalculator.At`); the only delta is the output struct.

**Files:**

- New: `Astronomy.Core/Moon/MoonEphemeris.cs`, `Moon/MoonSample.cs`,
  `Session/AltAzSample.cs`.
- Modified: `Astronomy.Core/Session/AltitudeCurve.cs`.
- Tests: `Astronomy.Core.Tests/Tests/MoonEphemerisTests.cs` (new),
  `Tests/AltitudeCurveTests.cs` (updated to consume `AltAzSample`).
- Benchmark: `Astronomy.Core.Benchmarks/AltitudeCurveBenchmark.cs`
  (updated to call new shape).

468 Core tests + 26 XISF + 67 NINA tests pass. TP rekeys its
`mDayAxis` / `mMoonAxis` from `DayWindowKey` to `NightDate` against
this surface (paired commit `TP c3ca26b`).

## 2026-05-27 — drop `FilterKind`: center/bandwidth is the spectral fact

`Filter.Kind` and the `FilterKind` enum deleted. Carried over from before
center/bandwidth metadata was complete on every preset; with that
metadata now present, Kind was stored on every Filter instance but
never branched on by production code (only asserted by tests). TP's own
filter type never had a Kind field, so the Library was carrying an
unused-by-consumers classification.

What K-S / moon avoidance actually use:

- K-S sky brightness: `CenterNm` (Rayleigh λ⁻⁴ scaling).
- Moon avoidance (Lorentzian): TP's own `Filters.Filter` Lorentzian
  params -- the Library `Filter` never carried these.

So `Kind` was metadata-with-no-readers. Deleted: enum file, ctor
parameter, property, `With(kind: ...)` arg, all test references.
`FilterFromCode`'s unknown-code branch became
`new Filter(code)` (null center+bandwidth) instead of
`new Filter(code, FilterKind.Unknown)`. The "unknown" signal is now
`Filter.CenterNm == null` or `Filter.Name` not matching a preset.

Files: `Astronomy.NINA/FilterKind.cs` (deleted), `Astronomy.NINA/Filter.cs`,
`Astronomy.NINA/Xisf/ReportToTargetAdapter.cs`,
`Astronomy.NINA.Tests/CompositionTypeTests.cs`,
`Astronomy.NINA.Tests/Xisf/ReportToTargetAdapterTests.cs`.

553 Library tests pass (460 Core + 26 XISF + 67 NINA); TP's 152 pass.

## 2026-05-27 — Filter rename + center/bandwidth fill

Standard filter presets in `Astronomy.NINA/Filter.cs` renamed to match
TargetPlanner's FilterLibrary canonical set: `Ha` -> `H`, `OIII` -> `O`,
`SII` -> `S`. The Filter.Name string carries through to the rename too
("H" not "Ha"). LRGB names unchanged.

L, R, G, B presets now carry CenterNm + BandwidthNm metadata (previously
null). Center/bandwidth values calibrated to the Astrodon E-Series
LRGB datasheet; SII center bumped from 671.6 -> 672.4 (Chroma 3 nm
centered between the 671.6 / 673.1 doublet, not on the spectroscopic
line). Full preset table:

  H  Narrowband  656.3 nm  3 nm     (Astrodon 3nm Hα)
  O  Narrowband  500.7 nm  3 nm     (Astrodon 3nm [O III])
  S  Narrowband  672.4 nm  3 nm     (Chroma 3nm SII, doublet-centered)
  L  Luminance   550 nm    300 nm   (Astrodon E-Series Luminance)
  R  Broadband   650 nm    60 nm    (Astrodon E-Series Red)
  G  Broadband   525 nm    65 nm    (Astrodon E-Series Green)
  B  Broadband   450 nm    100 nm   (Astrodon E-Series Blue)

Adjacent changes that fell out of the rename:

- `Astronomy.NINA/Xisf/ReportToTargetAdapter.FilterFromCode`: case arms
  retargeted from `Filter.Ha` / `Filter.OIII` / `Filter.SII` to the new
  `Filter.H` / `Filter.O` / `Filter.S`. Input single-letter codes
  unchanged.
- `Astronomy.NINA/Xisf/ImageLibraryScanner.NormalizeFilterName`:
  previously expanded single-letter codes to multi-letter canonical
  names ("H" -> "Ha", "L" -> "Luminance", etc). The canonical form is
  now single-letter, so the mapping is identity for the 7 known codes
  plus unchanged-pass-through for unknown codes. Image library
  `FilterAggregate.FilterName` now matches `Filter.Name` end-to-end.

Why now: TargetPlanner's FilterLibrary uses single-letter names + full
center/bandwidth metadata for K-S sky-brightness compute. Library
presets diverged from that shape (multi-letter names, null center/
bandwidth on LRGB), so a TP filter and a Library preset of the "same"
filter carried different values. Single source of truth across both
consumers now.

All 553 Library tests pass (460 Core + 26 XISF + 67 NINA, 1 intentional
skip); TP's 152 tests pass against the rebuilt Library.

## 2026-05-27 — canonical-singleton factories -> `static readonly` (Library sweep)

Every `public static T Name => new T(...)` factory in the Library --
the "default singleton" pattern -- was an expression-bodied property
allocating a fresh instance per access. Discovered while writing
TargetPlanner's Phase 3 cache-axis tests: the cache uses reference
identity on `Target` (per-(target, key) dict keys in `ChartCacheStore`'s
four axes) and `Location` (publish-time
`ReferenceEquals(currentLocation, buildLocation)` discard in
`CacheAxis.TryPublish`), and a naive `Target.Default` /
`TestLocations.PennsPark` twice in a test broke both. TP-side worked
around it initially, then the cleanup propagated to every analogous
factory across the Library for consistency.

Now: all 17 are `public static readonly` fields. Every owning type is
immutable (mutations produce new instances via `With(...)` / structural
record equality), so a shared singleton is risk-free. Side benefits:
zero per-access allocation in cold-path callers (e.g. TP's
`MainForm.CoordinatePresenter` calls `Target.Default` on every D/M/S
coordinate edit), plus callers can rely on reference identity if useful.

Converted (17 total — 12 production + 5 test fixtures):

- `Astronomy.Core/Targets/Target.cs` — `Target.Default` (M31).
- `Astronomy.Core/Locations/Location.cs` — `Location.Default` (40°N/75°W placeholder).
- `Astronomy.Core/Moon/MoonAvoidance.cs` — `MoonAvoidanceProfile.Disabled` / `Narrowband` (60°/7d) / `Broadband` (120°/14d).
- `Astronomy.NINA/Filter.cs` — `Filter.Ha` / `OIII` / `SII` / `L` / `R` / `G` / `B` (the standard astronomical narrowband + LRGB set; the narrowband trio was renamed `H`/`O`/`S` in the same-day Filter-rename entry above).
- `Astronomy.Core.Tests/Tests/TestLocations.cs` — `PennsPark` / `Sydney` / `Equator` / `Reykjavik` / `Antarctic` (test-only fixtures; 5 sites).

No production behaviour change. All 553 Library tests pass (460 Core +
26 XISF + 67 NINA, 1 intentional skip); TP's 152 tests pass. TP-side
comments in `TargetPlanner.Tests/Tests/Support/TestLocations.cs` and
`ChartCacheStoreTests.cs` flagging the now-resolved divergence were
trimmed in a paired TP commit.

## 2026-05-18 — Astronomy.XISF: Tier 1 extraction

The XISF reading primitives moved from `Astronomy.NINA/Xisf/` into a dedicated `Astronomy.XISF` library (7th and 8th buildable projects added: `Astronomy.XISF` + `Astronomy.XISF.Tests`). Rationale: the XISF file format is NINA-independent (PixInsight defines it); separating the reader from the planning layer makes it sharable across XFM, TP, ISP, and the user's other apps without dragging the planning model.

What landed:

- `Astronomy.XISF/XisfHeader.cs` — typed FITS-keyword accessors carrying value + comment per keyword (subset of `XisfFileManager/Keyword/KeywordList.cs`'s ~50+ accessors — only the ones currently consumed by the scanner are ported; rest stay in XFM for now and migrate when consumers need them).
- `Astronomy.XISF/XisfHeaderReader.cs` — header-only XISF parser; `XDocument.Parse()` on the embedded XML section. Pure managed, no native dep.
- `Astronomy.XISF.Tests` — 26 unit tests with synthetic XISF fixtures.

`Astronomy.NINA` now ProjectReferences `Astronomy.XISF`; the scanner (`Astronomy.NINA/Xisf/ImageLibraryScanner.cs` — since moved to `Astronomy.Catalog/Scan/`, 2026-06) consumes XisfHeader via `using Astronomy.XISF;`. AL.NINA tests unchanged (61 still pass); XISF-specific tests moved to the new test project (26 there).

**Why not NINA's own XISF code?** NINA.Image.FileFormat.XISF is coupled to `IImageData` / `IImageDataFactory` / `NINA.Profile.FileSaveInfo` / WPF, forces a full pixel decode on every read (`XISF.Load()` has no header-only path), and exposes FITS keywords as a weak `TryGetFITSProperty(key, out value)` dictionary. The user's existing XFM approach (XDocument + strongly-typed accessors, header-only by design) is the better fit for shared consumption across non-plugin apps.

*(Tier 1 was the shipped scope. The forward scope for Tiers 2-4 has moved to `ROADMAP.md`
§ **Open: Astronomy.XISF Tiers 2-4** — forward-looking work doesn't belong in the shipped history.)*

## 2026-05-18 — Astronomy.NINA: Phase B Target shape

Rich `Target` class + composition types in `Astronomy.NINA` root namespace, plus `Xisf/ReportToTargetAdapter` bridging Phase A output. What landed:

- **`Target`** — wraps `Astronomy.Core.Targets.Target` geometry; composes `IReadOnlyList<FilterHistory>` (empty when never imaged), `IReadOnlyList<PlannedExposure>?` (null when no plan source covers this target), optional `IHorizonProfile`, and `RotationDeg` (mod-360 normalized for lenient user input).
- **`Filter`** — sealed, immutable; `FilterKind` enum (Narrowband / Broadband / Luminance / RGB / Unknown); static factory presets (`Ha`/`OIII`/`SII`/`L`/`R`/`G`/`B`) with conventional center/bandwidth values. *(Superseded 2026-05-27: `FilterKind` dropped, presets renamed `H`/`O`/`S` and made `static readonly` — see those entries above.)*
- **`FilterHistory`** — per-(filter, purpose) rich history with count + integration + first/last imaged + typical settings. Carries `FilterPurpose` (Light vs Stars) so star-recombination captures don't muddy primary integration totals.
- **`ExposureSettings`** + **`PlannedExposure`** — leaf records for camera config + forward-looking sequence-plan entries.
- **`Xisf/ReportToTargetAdapter`** — extension methods `ImageLibraryReport.ToTargets()` / `TargetReport.ToTarget()`; maps XFM single-letter filter codes to standard `Filter` presets, preserves `FilterPurpose`, hands signed declination to Core's normalizing ctor (passing a pre-derived `north` flag would double-flip and silently land southern targets in the wrong hemisphere — caught by an early test).

All sealed + immutable + `With(...)` per AL convention. 87 unit tests pass cumulative (Phase A 48 + Phase B 39).

## 2026-05-18 — Astronomy.NINA: Phase A foundation

Fifth and sixth buildable projects added: `Astronomy.NINA` + `Astronomy.NINA.Tests`. Phase A of the multi-phase plan is complete. *(The scanner + report records described below moved to `Astronomy.Catalog/Scan/` in 2026-06.)* What landed:

- **`Xisf/XisfHeaderReader`** — pure-managed XISF header parser, ported from `XisfFileManager/Files/XisfXmlReader.cs` (XDocument-based, no native dependency). Read-only; no XFM mutation logic.
- **`Xisf/XisfHeader`** — typed FITS-keyword accessors. Required-for-aggregation keywords (OBJECT, RA, DEC, DATE-OBS, EXPTIME with legacy EXPOSURE fallback, FILTER, GAIN, OFFSET with per-camera normalization, SET-TEMP, CCD-TEMP, X/YBINNING, IMAGETYP, INSTRUME) plus capture-only (SSWEIGHT/W_SNR/W_FWHM/W_ECC, FOCALLEN, focuser/rotator, etc.) for future quality-summary work.
- **`Xisf/ImageLibraryScanner`** — walks user's standardized image library (`<Target>/Captures/<Camera>/<Filter>/*.xisf`), groups by OBJECT FITS keyword + directory-derived filter+purpose, aggregates per-target / per-filter counts, integration totals, first/last imaged UTC, mode-based typical settings. Parallel per-target scan; .xisf parse failures recorded in `SkippedFiles` rather than aborting.
- **Output records**: `ImageLibraryReport`, `TargetReport` (DirectoryName/Catalog/CommonName/ObjectName/RaHours/DecDegrees/Filters), `FilterAggregate`, `TypicalSettings`, `FilterPurpose` enum (Light/Stars). Sealed + immutable per AL convention.
- **36 unit tests** + **smoke test** gated on `TP_SMOKE_IMAGE_LIBRARY` env var. Smoke run against Dan's `E:\Photography\Astro Photography\Processing` library: **70 targets, 14,015 frames, 1,228 hours of integration** in ~1s — sane data flowing end-to-end.

**Forward roadmap (next-up phases, separate commits):**

- **Phase C** — TargetPlanner migrates from `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target`; image library becomes a new TP target source; Sky chart surfaces per-target Filter (color tint + badge + per-target K-S filter bandwidth).
- **Phase D** — `InputTargetAdapter` (bidirectional `Astronomy.NINA.Target ↔ NINA.InputTarget`); unblocks future NINA-sequence-JSON export from TP. Phase D introduces the `NINA.Plugin` NuGet dependency.

**Resolved (2026-05-18):** `Astronomy.XISF` extraction landed (see the *2026-05-18 — Astronomy.XISF: Tier 1 extraction* entry above). Tier 1 (header-only read) shipped; Tiers 2-4 are tracked in `ROADMAP.md` § *Open: Astronomy.XISF Tiers 2-4*.

