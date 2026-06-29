# Astronomy Library — Roadmap

**Charter.** Forward-looking design and a short "Recently shipped" digest for the `Astronomy` library — *where the library is going and what just landed*. How current modules work lives in `ARCHITECTURE.md`; PCL-wrapper-specific forward scope lives in `PCL-WrapperRoadmap.md`.

## Recently shipped (2026-06-28): CONSUMERS.md datasheet + Astronomy.Contracts.Tests bench

`CONSUMERS.md` stakes out the Library's **de-facto public contract** — the "pinned pinout" derived from grep-verified real usage: who consumes the Library and how (only **TP + TSM**, by `ProjectReference`/source), the surface each depends on, 18 semantic assumptions (contract-test candidates), fragility flags, and a design-review decision on the large dead/speculative public surface (**keep, don't prune** — it's API ahead of its planned consumers: the ISP plugin, the TSM write-back action, XFM's Library migration; not cruft).

`Astronomy.Contracts.Tests` (13th buildable project) is the pure-managed xUnit-v3 **bench that pins those assumptions** — a red test = a violated contract. 17 testable assumptions green / 6 documented-not-cleanly-testable: e.g. `RaDegrees`=degrees, `LunarAge.DaysAt` UTC-guard, `MoonEphemeris.Sample` exact count, `MoonSeparation.ObserveAt` geometric-not-apparent, `CatalogGraph` FK-order + mosaic-panels-after-parent, `ScanAsync` throws on missing root, the editor's required-columns refusal (DB untouched), `SkyBrightness.KsAt` pinned golden value (locks the 10-param order). Run by the constellation DRC `..\build-all.ps1`. (The same pass also restructured the docs into the lean-router `CLAUDE.md` + per-module `ARCHITECTURE.md` / `VERIFICATION.md` set.)

## Recently shipped (2026-06-26): TargetSchedulerEditor — guarded TS edit path

A write layer for the catalog → TS edit story, sibling to the Reader/Writer, built up 2026-06-11 → 06-26. `TargetSchedulerEditor.TrySetField(table, key, column, value)` is the guarded entry point: it folds four safety predicates (required columns present / file writable / no open sidecar / column present) into one structured-refusal call returning `(FieldEditResult?, RefusalReason)` (`None`/`SchemaIncompatible`/`ReadOnly`/`OpenSidecar`/`ColumnAbsent`) — consumers map the reason to their own wording; the library names none. The editor drives off a declarative `TsEditableSchema` data dictionary naming every user-editable TS column (table / exact SQLite column / label / value type / cadence-safety / enum-range) — which also doubles as the SQL-injection whitelist and an open-time schema-drift guard. It resolves rows by guid-or-Id, UPDATEs one column, and read-back verifies. Cadence-safe columns write plainly; cadence-breakers (`exposureplan.enabled`, `project.filterswitchfrequency`) are flagged, not specially handled — the caller must warn or defer. `ReconciliationProjection` grew the cell-granularity join (lifted from the consumer's grid loader) plus the write-back provenance addresses (`PlanTsKey` / `TemplateTsKey` / `ProjectTsKey`, `TargetCells.Enabled` / `TsTargetKey`).

## Recently shipped (2026-06-21): SQLite CVE pin (CVE-2025-6965)

Direct-pinned `SQLitePCLRaw.bundle_e_sqlite3` to **3.0.3** so it overrides the vulnerable native engine 2.1.11 that `Microsoft.Data.Sqlite` 10.0.9 pulled transitively (CVE-2025-6965 / GHSA-2m69-gcr7-jv3q, high, affects ≤ 2.1.11; no 2.1.12 — the package renumbered to a 3.x line). Native lib 3.50.3 = SQLite ≥ 3.50.2 (patched). NU1903 cleared with no NU1605 downgrade; the fix flows to every `Astronomy.Catalog` consumer. (Paired with a routine `Microsoft.Data.Sqlite` 10.0.3 → 10.0.9 bump.)

## Recently shipped (2026-06-21): xUnit v3 migration + Core-benchmark split

Every test project migrated **xUnit 2.9.3 → xUnit.v3 3.2.2** (`Microsoft.NET.Test.Sdk` → 18.6.0; `xunit.runner.visualstudio` on the `4.0.0-pre` line for VS2026 / .NET 10). Because v3 **generates the assembly entry point**, it collided with `Astronomy.Core.Tests`' custom `BenchmarkSwitcher` `Main` — so the BenchmarkDotNet harness (`Program.cs` + `Benchmarks/`) split out into a new pure-managed **`Astronomy.Core.Benchmarks`** exe (references only `Astronomy.Core`'s public surface, no `PCL.Native` → runs under plain `dotnet run -c Release`, no SLN/MSBuild). With the native graph gone, BDN's default out-of-process toolchain replaced the old `InProcessEmit` config (better-isolated numbers). New trap captured: **never let `xunit.v3` land on a non-test project** — a bulk "apply to all projects" NuGet action sprayed it onto 4 production projects (its `mtp-v1` targets force `OutputType=Exe`; the break only surfaces on a later version bump). 468 Core tests pass.

## Recently shipped (2026-06-11): Astronomy.Diagnostics — shared logging + screen-capture contract

New pure-managed library (11th buildable project) hosting the portfolio's diagnostic **conventions as code**, factored out of per-app copies (TP and TSM had hand-ported duplicates that drifted). `Log`: an append-only `%APPDATA%\<app>\Logs\` trail with a fixed line grammar and two verbosity axes — always-on Info/Warn/Error severity (survives Release) + gated `Diag` channels (default all-in-Debug / none-in-Release, per-channel runtime toggle via the app's env var) — plus session rotation, local-time stamps, and the `USER_OBS_*` observation protocol (START/CAP/END/CANCEL share an id). Configured once per app via `AppLogIdentity` (a shared library compiles once and can't read the consumer's `#if DEBUG`). `ScreenCapture.ToPng`: the System.Drawing `CopyFromScreen` grab, framework-agnostic (WinForms or WinUI). The method surface *is* the convention; the implementation enforces the invariants so an off-convention line can't be emitted. **Open follow-up — `ObservationSession`:** now that two live consumers each duplicate the dialog START/CAP/END/CANCEL wiring, factor it into the library (detail in `ARCHITECTURE.md`).

## Recently shipped (2026-06-10): mosaic-panel resolver + exposure-aware write-back

Two reconciliation deepenings landed across the Catalog this day.

**Mosaic panels became first-class targets.** A mosaic is now one parent `target` row plus one child row per panel (self-FK `parent_target_id`, `ON DELETE CASCADE`; composite directory-name `<mosaic dir>/<panel label>`). After several iterations the model converged on **"a panel is a normal target whose key is composite"**: the scanner retains per-panel `TargetReport.Panels` from the same walk, panels enter the working set as ordinary units, and the **one** standard resolve loop does nearest-coordinate anchoring, duplicate/alias reporting, and Both/Actual/Planned classification. Mosaic-specific logic shrank to coordinate **scope keys** (a panel anchors only among its mosaic's panels; standalone targets never see panels — cross-scope matches impossible by construction) and a panel-token name facet (`Panel 01of16` → `P1`). Aligned claims **outrank** unaligned ones (an unshot panel inside tolerance of a shot neighbour stays planned instead of false-folding — the Witch Head shape). `ManualReason.Mosaic` retired: panels auto-write like any target; the plan-less / inventory-less parent is inert by construction.

**Exposure time joined the identity.** The scanner buckets inventory per `(filter, purpose, whole-second exposure)` instead of folding sub-lengths into one mode value; `inventory_filter` gained `exposure_seconds` in its PK. The write-back key became `(target, filter, purpose, whole-second exposure)` — **the plan's seconds is the spec**: a plan receives the disk count at exactly its `round(ExposureSeconds ?? template default)` bucket (0 = a flagged decrease). Same-purpose plans at different durations now auto-resolve into separate writes instead of routing to manual; disk buckets with no plan surface as `UnplannedFrames` notes (write-back updates existing rows only — plan creation/deletion is a later milestone). Alias-aware dup-folds (option B): ≥2 TS names that each exactly equal a disk identity facet (M27 + Dumbell) are one `AliasTsTarget`, auto-written to every member when plan-count = alias-member-count. `EffectiveExposure` single-sourced across both planners. (Schema change — `Catalog.db` is derived; delete and rebuild, no migration.)

## Shipped: catalog write-back to TS (`TargetSchedulerWriter`) — 2026-06-08

The catalog reconciles disk (actual) ↔ TS (plan) onto one canonical target and computes goal-vs-actual; **Phase 4
write-back shipped**. `TargetSchedulerWriter` + the pure `WriteBackPlanner` write reconciled disk-derived counts
back into a **local copy** of TS's `schedulerdb.sqlite`, mapping catalog → exact TS rows via the retained
`imported_from_ts_guid` provenance (TS's own `acquired_count` was badly stale vs disk — e.g. 0 H frames vs 140 on
disk). A surgical single-target path (`SingleTargetPlanner` + `ImageLibraryScanner.ScanUnitsAsync`, driven by `tcm
writeback --target`) updates one target — **per panel for a mosaic** — without a catalog rebuild. Driven from TCM
(since renamed TSM — `E:\Projects\VisualStudio\Astronomy\TargetSchedulerManager`, ROADMAP Phase 4; the CLI verbs
retired 2026-06-11, engine resurfaces as an app action); operates on a local copy (the live TS
DB lives on the imaging PC — cross-machine WAL caveat). Open: alias-vs-duplicate handling for `M27`/`Dumbell`.

## Recently shipped (2026-06): Astronomy.Catalog — goal-vs-actual reconciliation

`Reconcile/` joins TS goals to disk actuals so consumers can answer "how close is each target/filter to its
goal." Goals = `exposure_plan.desired_count` (summed per filter via the template); actuals = disk
`inventory_filter` — **not** TS's own `acquired_count`, which is often badly stale (real Wizard example: TS said
0 H frames, disk had 140). Join key is `(target, filter_name)` — both planes already use the same single-letter
names, so no normalization. `ReconcilePolicy.Combined` (default) counts Light + Stars toward a goal (fits
shooting RGB only as Stars for star colour); `LightOnly` excludes Stars. `Reconciler` (pure) +
`CatalogStore.GetReconciliation` → per-(target,filter) desired/acquired/remaining/% + a target rollup status
(NotStarted/InProgress/Complete/Unplanned). First real run: 101 planned targets (6 complete / 33 in-progress /
62 not-started), 8221/30088 frames done; the TCM host prints the summary. 42 Catalog tests pass.

## Recently shipped (2026-06): Astronomy.Catalog — disk(actual) + TS(plan) reconciled onto one canonical target

Follow-on to the Catalog/scanner entry below. The two parallel target tables collapsed into **one canonical
`target`** carrying both facets (disk identity + plan attributes), discriminated by `source_id` — `Actual`
(on disk only), `Planned` (in TS only / not yet shot), `Both` (merged). The disk library is ACTUAL (truth);
TS is the PLAN; the catalog re-organizes the plan clean and anchored to actual. `inventory_filter` (actuals)
and `exposure_plan` (goals) both hang off the one target.

- **`Build/TargetResolver`** (pure, unit-tested): **coordinate-primary** match — each TS target anchors to the
  nearest disk target within a tolerance (default 0.5° haversine); name only validates; disk plate-solved coords
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

## Recently shipped (2026-06): Astronomy.Catalog — catalog DB + library-scanner home

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

## Recently shipped (2026-06): Astronomy.XISF.Compression — shared zlib+sh + SHA-1 codec

Ported XFM's image-block codec into `Astronomy.XISF` (`Compression/`): byte-shuffle + zlib
(max level) + SHA-1, symmetric `Compress`/`Decompress`, plus `BlockCompressionInfo`
(compression/checksum attribute parse/format) — the Tier-4 "compression + checksum"
foundation. XFM now consumes it instead of its own copy. 8 codec tests (34 XISF total).

## Recently shipped (2026-05-28): MoonEphemeris + AltitudeCurve.Sample reshape

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

## Recently shipped (2026-05-27): drop `FilterKind` -- center/bandwidth is the spectral fact

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

## Recently shipped (2026-05-27): Filter rename + center/bandwidth fill

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

## Recently shipped (2026-05-27): canonical-singleton factories -> `static readonly` (Library sweep)

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

Now: all 12 are `public static readonly` fields. Every owning type is
immutable (mutations produce new instances via `With(...)` / structural
record equality), so a shared singleton is risk-free. Side benefits:
zero per-access allocation in cold-path callers (e.g. TP's
`MainForm.CoordinatePresenter` calls `Target.Default` on every D/M/S
coordinate edit), plus callers can rely on reference identity if useful.

Converted (12 total):

- `Astronomy.Core/Targets/Target.cs` — `Target.Default` (M31).
- `Astronomy.Core/Locations/Location.cs` — `Location.Default` (40°N/75°W placeholder).
- `Astronomy.Core/Moon/MoonAvoidance.cs` — `MoonAvoidanceProfile.Disabled` / `Narrowband` (60°/7d) / `Broadband` (120°/14d).
- `Astronomy.NINA/Filter.cs` — `Filter.Ha` / `OIII` / `SII` / `L` / `R` / `G` / `B` (the standard astronomical narrowband + LRGB set).
- `Astronomy.Core.Tests/Tests/TestLocations.cs` — `PennsPark` / `Sydney` / `Equator` / `Reykjavik` / `Antarctic` (test-only fixtures; 5 sites).

No production behaviour change. All 553 Library tests pass (460 Core +
26 XISF + 67 NINA, 1 intentional skip); TP's 152 tests pass. TP-side
comments in `TargetPlanner.Tests/Tests/Support/TestLocations.cs` and
`ChartCacheStoreTests.cs` flagging the now-resolved divergence were
trimmed in a paired TP commit.

## Recently shipped (2026-05-18): Astronomy.XISF — Tier 1 extraction

The XISF reading primitives moved from `Astronomy.NINA/Xisf/` into a dedicated `Astronomy.XISF` library (7th and 8th buildable projects added: `Astronomy.XISF` + `Astronomy.XISF.Tests`). Rationale: the XISF file format is NINA-independent (PixInsight defines it); separating the reader from the planning layer makes it sharable across XFM, TP, ISP, and the user's other apps without dragging the planning model.

What landed:

- `Astronomy.XISF/XisfHeader.cs` — typed FITS-keyword accessors carrying value + comment per keyword (subset of `XisfFileManager/Keyword/KeywordList.cs`'s ~50+ accessors — only the ones currently consumed by the scanner are ported; rest stay in XFM for now and migrate when consumers need them).
- `Astronomy.XISF/XisfHeaderReader.cs` — header-only XISF parser; `XDocument.Parse()` on the embedded XML section. Pure managed, no native dep.
- `Astronomy.XISF.Tests` — 26 unit tests with synthetic XISF fixtures.

`Astronomy.NINA` now ProjectReferences `Astronomy.XISF`; the scanner (`Astronomy.NINA/Xisf/ImageLibraryScanner.cs`) consumes XisfHeader via `using Astronomy.XISF;`. AL.NINA tests unchanged (61 still pass); XISF-specific tests moved to the new test project (26 there).

**Why not NINA's own XISF code?** NINA.Image.FileFormat.XISF is coupled to `IImageData` / `IImageDataFactory` / `NINA.Profile.FileSaveInfo` / WPF, forces a full pixel decode on every read (`XISF.Load()` has no header-only path), and exposes FITS keywords as a weak `TryGetFITSProperty(key, out value)` dictionary. The user's existing XFM approach (XDocument + strongly-typed accessors, header-only by design) is the better fit for shared consumption across non-plugin apps.

**Tiers 2-4 — future work** (added when a real consumer needs them; no eager design):

- **Tier 2** — header write-back. Modify FITS keywords in place, preserving the image-attachment block. Required for XFM migration (XFM does rename / normalization / accept-reject prefix writes) and a future TPS grade-state keyword write.
- **Tier 3** — full image read. Pixel data decode for uncompressed + LZ4 + zlib + zstd. Borrow compression algorithm strategies from NINA's `XISFData`; don't pull NINA's classes (decouple). Required by any consumer that does actual image processing.
- **Tier 4** — full image write. Image data composition + compression + checksum (SHA-256). Required for XFM's writes and any future image-save pipeline.

When XFM eventually migrates to Astronomy.XISF as its sole reader, the additional `KeywordList` accessors (FocalLength, Camera, EGAIN, MasterFrame metadata, weight keywords, etc.) port over alongside Tier 2.

## Recently shipped (2026-05-18): Astronomy.NINA — Phase B Target shape

Rich `Target` class + composition types in `Astronomy.NINA` root namespace, plus `Xisf/ReportToTargetAdapter` bridging Phase A output. What landed:

- **`Target`** — wraps `Astronomy.Core.Targets.Target` geometry; composes `IReadOnlyList<FilterHistory>` (empty when never imaged), `IReadOnlyList<PlannedExposure>?` (null when no plan source covers this target), optional `IHorizonProfile`, and `RotationDeg` (mod-360 normalized for lenient user input).
- **`Filter`** — sealed, immutable; `FilterKind` enum (Narrowband / Broadband / Luminance / RGB / Unknown); static factory presets (`Ha`/`OIII`/`SII`/`L`/`R`/`G`/`B`) with conventional center/bandwidth values.
- **`FilterHistory`** — per-(filter, purpose) rich history with count + integration + first/last imaged + typical settings. Carries `FilterPurpose` (Light vs Stars) so star-recombination captures don't muddy primary integration totals.
- **`ExposureSettings`** + **`PlannedExposure`** — leaf records for camera config + forward-looking sequence-plan entries.
- **`Xisf/ReportToTargetAdapter`** — extension methods `ImageLibraryReport.ToTargets()` / `TargetReport.ToTarget()`; maps XFM single-letter filter codes to standard `Filter` presets, preserves `FilterPurpose`, hands signed declination to Core's normalizing ctor (passing a pre-derived `north` flag would double-flip and silently land southern targets in the wrong hemisphere — caught by an early test).

All sealed + immutable + `With(...)` per AL convention. 87 unit tests pass cumulative (Phase A 48 + Phase B 39).

## Recently shipped (2026-05-18): Astronomy.NINA — Phase A foundation

Sixth and seventh buildable projects added: `Astronomy.NINA` + `Astronomy.NINA.Tests`. Phase A of the multi-phase plan is complete. What landed:

- **`Xisf/XisfHeaderReader`** — pure-managed XISF header parser, ported from `XisfFileManager/Files/XisfXmlReader.cs` (XDocument-based, no native dependency). Read-only; no XFM mutation logic.
- **`Xisf/XisfHeader`** — typed FITS-keyword accessors. Required-for-aggregation keywords (OBJECT, RA, DEC, DATE-OBS, EXPTIME with legacy EXPOSURE fallback, FILTER, GAIN, OFFSET with per-camera normalization, SET-TEMP, CCD-TEMP, X/YBINNING, IMAGETYP, INSTRUME) plus capture-only (SSWEIGHT/W_SNR/W_FWHM/W_ECC, FOCALLEN, focuser/rotator, etc.) for future quality-summary work.
- **`Xisf/ImageLibraryScanner`** — walks user's standardized image library (`<Target>/Captures/<Camera>/<Filter>/*.xisf`), groups by OBJECT FITS keyword + directory-derived filter+purpose, aggregates per-target / per-filter counts, integration totals, first/last imaged UTC, mode-based typical settings. Parallel per-target scan; .xisf parse failures recorded in `SkippedFiles` rather than aborting.
- **Output records**: `ImageLibraryReport`, `TargetReport` (DirectoryName/Catalog/CommonName/ObjectName/RaHours/DecDegrees/Filters), `FilterAggregate`, `TypicalSettings`, `FilterPurpose` enum (Light/Stars). Sealed + immutable per AL convention.
- **36 unit tests** + **smoke test** gated on `TP_SMOKE_IMAGE_LIBRARY` env var. Smoke run against Dan's `E:\Photography\Astro Photography\Processing` library: **70 targets, 14,015 frames, 1,228 hours of integration** in ~1s — sane data flowing end-to-end.

**Forward roadmap (next-up phases, separate commits):**

- **Phase C** — TargetPlanner migrates from `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target`; image library becomes a new TP target source; Sky chart surfaces per-target Filter (color tint + badge + per-target K-S filter bandwidth).
- **Phase D** — `InputTargetAdapter` (bidirectional `Astronomy.NINA.Target ↔ NINA.InputTarget`); unblocks future NINA-sequence-JSON export from TP. Phase D introduces the `NINA.Plugin` NuGet dependency.

**Resolved (2026-05-18):** `Astronomy.XISF` extraction landed (see top "Recently shipped" entry). Tier 1 (header-only read) shipped; Tiers 2-4 tracked above.

## Open: SIMD / FMA deep dive

Captured 2026-05-12. The FMA hygiene pass landed in `b83a0d8` (Meeus +
TargetGeometry + SkyBrightness, 1.5-4.2% on Sun / Simpson paths, noise
on the transcendental-dominated moon path). The user wants to follow
up with a proper SIMD / vectorization investigation when time allows.

Full field notes — toolchain answers, runtime knobs, performance model,
microbench design lessons, the HotPathBenchmarks before/after table,
and four open directions ranked by impact — live in
**[docs/2026-06-21-simd-investigation.md](docs/2026-06-21-simd-investigation.md)**.

The four open directions in summary:

1. **Specialized non-`params` `Horner` overloads.** Eliminates the
   `double[]` allocation that shows up in `Sun_AltAzAt` (144 B/call)
   and `BestSession_For_MoonBlind` (168 B/call). Low risk, modest win.
2. **`Vector<double>` SIMD on `MoonPosition.ApparentEcliptic` 60-term
   table loops.** Biggest perf upside in the portfolio; needs a
   vectorised sin/cos (hand-rolled polynomial approximation or lookup
   table) since .NET doesn't auto-vectorise scalar `Math.Sin`/`Cos`.
3. **Estrin's-scheme polynomial parallelisation.** Drop-in alternative
   to Horner for longer chains; modest win on 4-term polynomials, grows
   logarithmically with length.
4. **Explicit `System.Runtime.Intrinsics.X86.{Fma,Avx2,Avx512F}`
   intrinsics.** Reserve for cases `Vector<T>` doesn't express. Niche.

Not gating any active work — recorded here so the investigation isn't
re-derived next time.

## Open: publish to GitHub

Captured 2026-05-08. The library currently lives only on disk; sibling
TargetPlanner consumes it via local `ProjectReference`. At some point the user
wants the astronomy code in the open. Not gating any active work — recorded
here so it doesn't drift out of memory.

### Three scope options

- **Option A — Core only, public** *(recommended)*. Spin out `Astronomy.Core`
  + `Astronomy.Core.Tests` + `Astronomy.Core.Benchmarks` into its own public repo. Leave
  `Astronomy.PCL` / `Astronomy.PCL.Native` in the existing private layout (or
  a separate private sibling). Smallest scope, no PCL-license entanglement,
  gets the pure-Meeus astronomy code into the open. Estimated 1–2 sessions.
- **Option B — whole Library, public**. One public repo with all thirteen
  projects (scope/effort estimate needs revisiting at this count). PCL adds friction: third-party SDK dependency, build docs,
  license-compatibility check (PCL Open License vs. whichever license is
  picked in step 1). Estimated 2–4 sessions.
- **Option C — public mirror, dev stays private**. Keep working in the
  current `E:\…\Astronomy\Library\` and publish a periodic snapshot to a
  public repo (e.g. `git push public main`). Lowest one-time cost; ongoing
  maintenance burden of remembering to push.

### Prep checklist (applies to A or B)

1. **License.** Pick one and add a `LICENSE` file at the repo root. Typical
   for libraries: MIT, Apache 2.0, BSD-3. For Option B, verify chosen
   license is compatible with PCL Open License (see `PCL/COPYING.md`).
2. **Personal-data scrub.** Same kind of pass as TargetPlanner's
   2026-05-08 scrub:
   - `Astronomy.Core.Tests/Tests/Astrometry/ParityFixtures.cs` has inline
     Penns Park lat/lon (`40.282835`, `74.997369`) in named DST regression
     cases (`PennsParkSpring`, `PennsParkDstFall`, `PennsParkDstSpring`,
     `PennsParkSummerSolstice`). Parameterize them: rename to neutral
     names (e.g. `MidLatNorthSpring`) or move the personal coordinates
     into the test's `TestLocations.PennsPark` fixture (which already
     exists for the rest of the suite as of 2026-05-08).
   - ~14 test comments mention "Penns Park" / "M31 at Penns Park" — keep
     them or rephrase as "the 40°N test fixture"; either is defensible.
   - Audit `CLAUDE.md` for personal paths, machine names, or Windows-user
     specifics that won't make sense to a public reader.
3. **README.** New `README.md` at repo root: one-paragraph "what this is"
   (pure-managed Meeus + closed-form session placement + K-S sky brightness
   + optional XISF read via PCL P/Invoke), build/test instructions, link
   to existing CLAUDE.md as the deeper reference. ~80 lines.
4. **Build prerequisites.** Document MSBuild + VS2026 (build 18.x) for the C++/C#
   mixed solution; `dotnet build` for `Astronomy.Core` alone. For Option B,
   document where to drop the PCL SDK
   (`Library\PCL\` snapshot from `PCL-master.zip`, pinned 2025-02-22) so
   `Astronomy.PCL.Native.vcxproj` can find its static libs.
5. **Git history.** `git log -p` against the lib's history for personal
   paths in commit diffs. The library was extracted from TargetPlanner
   (2026-04-23), so the surface area to audit is small.
   `git filter-repo` if anything sensitive turns up.
6. **CI** *(optional, defer for v1)*. GitHub Actions workflow that runs
   `dotnet test Astronomy.Core.Tests` on push. Skippable if Option A
   ships without `Astronomy.PCL` / `Astronomy.PCL.Native` (no native
   build needed → trivial CI).
7. **NuGet** *(optional, defer for v1)*. `Astronomy.Core` could become a
   published NuGet for downstream consumption. Adds versioning discipline;
   skippable for an initial public-source release.

### TargetPlanner downstream impact

The user's workflow treats local disk as source of truth and GitHub as a
distribution mirror, so publishing the Library doesn't change anything
about *the user's* dev experience — TP keeps consuming the local sibling
checkout exactly as it does today. The question is what *public TP
consumers* would do, since they don't have the user's local layout. Two
paths:

- Keep the `ProjectReference` and document "clone the Library repo next
  to TargetPlanner" in `TargetPlanner/CLAUDE.md` (already partially
  there). Public TP consumers clone two repos.
- Switch TP to a `PackageReference` against a published NuGet (requires
  step 7 above). Public TP consumers clone one repo; NuGet handles the
  rest. Cleaner long-term; no work needed if Option A skips NuGet for v1.

## Open: K-S unphysical extinction-overdrive at low altitudes (urban regime)

Captured 2026-05-24 from TP visual testing. `SkyBrightness.KsAt`'s
dark-sky baseline formula `vDark = v0 − 2.5·log₁₀(X) + k·(X−1)` has
the extinction term `k·(X−1)` growing linearly with airmass. For
high-k sites (Bortle 8–9, k₅₀₀ ≥ 0.4) at target altitudes below
~10°, this term dominates and predicts a sky that gets darker than
zenith from extinction alone — physically wrong for urban regimes
where off-axis light pollution actually brightens the horizon via
in-scattering. K-S 1991 was calibrated for moderate-to-dark sites
and doesn't model artificial-light in-scatter.

Concrete example (TP test on 2026-05-24): Markarian's Chain in
Denver Bortle 9 (v0=16.5, k=0.55) at target altitude 0.79°
(airmass ~28.6): K-S predicts vDark = 27.95 mag → after V-band BW
scaling, ~28 mag/arcsec². For narrowband H/O filters the prediction
extends to mag 21–31, well off the Sky chart's `[16, 22]` axis.

**Real fix:** adopt Garstang 1986 / Falchi 2016 framework that models
artificial-light scattering INTO the line of sight from off-axis city
sources. Requires per-site inputs the Library doesn't currently carry
(city positions, brightnesses, distances, azimuths) and substantially
more compute. Not a v2 lift — more like "TP/Library becomes a research
tool" lift. On-ramp: pull VIIRS satellite data + model the largest
city near each site + run a simplified single-scatter calc per-azimuth.

**Interim consumer policy:** until Garstang/Falchi adoption, callers
should null-gate K-S display below ~10° target altitude at urban sites.
TP's `AltitudeSubChart_Sky` does this via the `KsLowAltitudeGateDeg`
constant — see TP ROADMAP §Future-flagged TP-side work for the removal
condition.

Caveat documented in `SkyBrightness.cs` class remarks alongside the
near-moon (separation < ~10°) and narrow-airglow-overlap regimes.

## Open: refraction asymmetry between K-S call and Lorentzian moon gate

Captured 2026-05-24 from cross-component review. Two TP-side paths
currently apply the moon-altitude horizon at different conventions:

- **K-S Sky chart** (TP `AltitudeSubChart_Sky`, after the 2026-05-24
  `bec0d6c` refraction fix): `moonAltApparent = m.MoonAltDeg +
  Refraction.SaemundssonDeg(m.MoonAltDeg)`, then `SkyBrightness.KsAt`
  clamps moon contribution at `moonAltApparent > 0`. Cutoff aligns with
  visually-observed moonset (~34' / ~2 min later than geometric).
- **Lorentzian placement gate** (`BestSession.MoonClearIntersect` →
  `MoonAvoidance.RequiredSepWithRelax`): consumes **geometric** moon
  altitude from `MoonSeparation.ObserveAt` with no refraction. The
  Relax bounds (`RelaxMinAltDeg`, `RelaxMaxAltDeg`) are in
  geometric-degrees terms.

Net: K-S sky-brightness compute and Lorentzian placement gate apply
moon-altitude thresholds offset by ~34'. A target imaged near the
moonset/moonrise boundary can be K-S-OK while Lorentzian-rejected (or
vice versa) for ~2 min around the transition.

**Auto-resolves when the K-S Δmag gate replaces the Lorentzian** —
see "partial-moon-impact tolerance" below. K-S Δmag inherits the
refraction-aware moon altitude from the K-S call automatically; the
Lorentzian path becomes legacy at that point and no per-path
refraction reconciliation is needed.

For interim consistency without waiting for the K-S Δmag work:
`MoonAvoidance.RequiredSepWithRelax` could refraction-correct its
`moonAltDeg` parameter (one line; matches K-S convention). Small
behavioral shift — moon-clear gate would extend ~2 min later at
moonrise/moonset — but aligns the two consumers.

## Open: partial-moon-impact tolerance in placement primitives

Captured 2026-05-23 (relocated from TP ROADMAP, deferred until much
later). Allowing a session to span moon-blocked time at a quality
penalty rather than rejecting outright. The current placement
primitives are designed so they don't preclude this — the moon profile
is optional everywhere; mask computation is behind an internal helper
in `BestSession` / `VisibilityWindows`. Implementation would add a
quality-weighted penalty path alongside the current hard-reject path,
chosen by an opt-in parameter so existing callers stay on the current
behavior.

Not actively scoped — recorded here so the design considerations
aren't re-derived.

### Design notes (from 2026-05-24 discussion)

The eventual K-S Δmag gate **replaces** the Lorentzian entirely — it
doesn't just relax it. K-S takes phase angle (moon disc illumination
intensity), moon altitude (airmass attenuation), target altitude, and
target-moon separation all as inputs; the Lorentzian crudely
approximates these dimensions with `SeparationDeg` + `WidthDays` +
altitude-relax scalars. K-S Δmag becomes "accept this minute if the
K-S-predicted brightness is within X mag of the moonless baseline."
One scalar tolerance, full physics.

- **Lorentzian parameterization side question (days vs %illumination):**
  %illumination is physically more meaningful (tracks moon disc
  brightness contribution, not synodic-cycle calendar position) and
  more intuitive ("50% moon" vs "7 days from full"). But this is a
  placeholder question — K-S Δmag makes it moot. Switching the
  Lorentzian's parameterization mid-flight before K-S Δmag would be
  wasted work.

- **TS-style altitude relaxation (`RelaxMinAlt` / `RelaxMaxAlt` /
  `RelaxScale`):** addresses the moon-below-horizon and moon-near-
  horizon regimes that K-S handles automatically via airmass
  attenuation (moon at apparent alt 0 → contribution clamped to 0;
  moon at alt 1° → tiny contribution from extincted moonlight). K-S
  Δmag gates inherit this behavior for free.

- **One thing K-S 1991 doesn't model that TS relax can approximate:**
  the lunar-twilight-glow regime ~10-15 min after moonset (analogous
  to solar civil twilight). Negligible for amateur planning purposes;
  a v3 K-S extension if anyone needs it.

- **Refraction asymmetry** (see preceding entry) is the third thing
  that auto-resolves under K-S Δmag — the gate inherits the K-S
  call's apparent-altitude moon convention; the Lorentzian's
  geometric-altitude inconsistency disappears with the Lorentzian.
