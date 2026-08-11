# CONSUMERS.md — Astronomy Library datasheet

**Charter / datasheet.** The Library's de-facto public **contract**: what each consumer actually
depends on (the "pinned pinout"), the semantic assumptions beyond signatures, and the dependency
graph. Treat this as the **stable interface** — change it *deliberately* (a pinout revision = update
consumers + tests in the same breath); the implementation behind it can churn freely. Derived from
grep-verified real usage (2026-06-28; refreshed 2026-07-07, re-audited 2026-07-24); keep current as the contract evolves.
**The surface evolves in place** — a shape change replaces the old signature/name/type outright (no
kept overloads, no `[Obsolete]` grace period, no migration shims), with consumers updated in the same
or a paired commit and rebuilt from scratch (recurring precedent: the 2026-05-27/28 `FilterKind`
deletion and `AltitudeCurve.Sample` reshape, each with its paired consumer commit) — so this pinout
describes *current* behavior, never a compatibility promise.

**How this is validated** (docs *describe* the contract; these *verify* it):
- **Structural — free:** every consumer `ProjectReference`s the Library (source), so a breaking API
  change is a **consumer build break**. The cross-repo constellation build is that check — see
  `VERIFICATION.md` § *Cross-repo contract verification*. Its unit is each consumer's **solution
  file**, so a consumer project deliberately kept out of its app's sln (today XFM's
  `tools/CompressionBench`) pins Library surface the DRC never compiles — detail in that section.
- **Semantic — pinned by `Astronomy.Contracts.Tests`:** the compiler can't see the assumptions in
  the *Semantic assumptions* section, so the contract bench pins them. **Covered-or-registered
  rule:** every numbered assumption is either covered by a citing test or registered with a reason
  in `NotCleanlyTestableAssumptions.cs`; retired assumptions keep their numbers (normative spec:
  `openspec/specs/contract-assumption-pinning/`). The bench references **six of the nine shipped
  managed assemblies** — Core, XISF, Catalog, Diagnostics, Diagnostics.Windows, NINA (NINA added
  2026-07-24 — `NamedSitePersistenceContractTests` pins #2's serialization shape). **The boundary
  is deliberate: the bench stops at the platform capture backend.** It wires `ObservationSession`
  exactly as consumers do (core + `.Windows`) and never references the `.WinForms`/`.WinUI` shells —
  their entry points (`DiagnosticsDialog.ShowOrFocus`, `DiagnosticsWindow.ShowOrFocus`,
  `DiagnosticsHotkey.Register`) are unit-test-free by the packaged-host decision (a WinUI test
  project would force a packaged MSTest host), so shell surface is covered only structurally, by the
  consumer builds in `..\build-all.ps1` — never semantically.
- **The bench pins *current behavior* — it does not legislate.** When a contract test exposes a
  behavior-vs-doc mismatch, the mismatch is **surfaced for adjudication, never silently patched**, and
  production code is not changed merely to make a pin pass. The rule has paid off twice: #19/#20's
  `exposure = 0` divergence (adjudicated 2026-07-07 — the doc was wrong, TS's real sentinel is `!= -1`)
  and the K-S calibration table mislabeling the narrowband gibbous cluster as full-moon, caught by the
  pinning test before ship. A pin that "fails" is evidence, not a chore.

---

## Consumers (three, today)

| Consumer | Host | ProjectRefs (direct) | Actually uses |
|---|---|---|---|
| **TargetPlanner (TP)** | WinForms | Core, NINA, Diagnostics (+ `.WinForms` shell → `.Windows` transitively) | Core (broad), `NINA.Persistence`, XISF (assembly flows transitively via NINA; TP *code* calls 4 members directly — see the XISF row below), Diagnostics (`Log` + `DiagnosticsDialog`). **Catalog present transitively but unused.** PCL deliberately *not* referenced (avoids `unsafe` in the WinForms host). |
| **TargetSchedulerManager (TSM)** | WinUI | Catalog, Diagnostics (+ `.WinUI` shell → `.Windows` transitively, since 2026-08-10), **Core** | Catalog (broad), Diagnostics (`Log` + `DiagnosticsWindow`), Core (narrow — `Locations`, `Horizons`, `Night`, `Session`, `Targets`; added 2026-07-23, commit `a48b2fa`). XISF only *inside* Catalog's scanner. **NINA / PCL not referenced.** |
| **XisfFileManager (XFM)** | WinForms | XISF, Core, Diagnostics (+ `.WinForms` shell → `.Windows` transitively) | XISF (header reader, codecs, `XisfBlockRewriter`, `XisfChecksumVerifier` — adopted v2.4.0, 2026-08; the checksum-alias and block-rewrite work shipped for it), Core (`Astrometry.WcsOrientation` — plate-solve support), Diagnostics (`Log` + `DiagnosticsDialog`). **Catalog / NINA / PCL not referenced.** *(Added to this table 2026-08-10 — XFM had adopted AL without the datasheet, the DRC, or the parent map recording it.)* |

All three consume **by `ProjectReference` (source) — no DLL, no NuGet.** That's the free continuity check.

**Clock convention (user directive 2026-08-11):** `Core.Time.IClock` / `SystemClock` is the
portfolio's **single clock source**. New consumer code takes an `IClock` (or an explicit
`DateTime utc` parameter, the Core convention) instead of reading `DateTime.UtcNow` /
`DateTime.Now`; existing app-side ambient reads migrate opportunistically (owner: the user).
Inventory (corrected same day — the first count missed `DateTimeOffset` variants): TP 0, XFM 0;
TSM's `MainViewModel` adopted the seam 2026-08-11 (TSM commit `906d607` — all four VM reads,
including the planning-input `UtcNow` that mattered), leaving five service-layer reads
(`TsJournal` ×2, `TsSync` ×2, `ReconciliationLoader` ×1) tracked in TSM's ROADMAP for
opportunistic constructor threading.

**Not consumers** (despite portfolio proximity — important to be honest about):
- **IntervalScheduler (IS)** — design docs only, no project, zero references (planned consumer).
- **IntervalSchedulerManager (ISM)** — stub repo only (CLAUDE.md + ROADMAP.md; no project, no Library
  reference).
- NINA plugin/source + other Astronomy projects — no Library reference.
- *(XisfFileManager left this list 2026-08-10 — it adopted AL at its v2.4.0 and is the third
  consumer in the table above.)*

So the live constellation is a **4-node graph** (Library → TP, TSM, XFM — XFM added 2026-08-10); see
the graph below for the edges.

## Dependency graph

```
Core (base)      XISF (base)      Diagnostics (base)
Catalog → XISF, Core          (Core edge added 2026-07-29 — FieldFootprint)
NINA    → Core, XISF, Catalog
PCL     → Core  (+ native Astronomy.PCL.Native, build-only)

TP  → Core, NINA, Diagnostics (+.WinForms)   (XISF + Catalog transitive via NINA)
TSM → Catalog, Diagnostics (+.WinUI), Core   (XISF transitive via Catalog)
XFM → XISF, Core, Diagnostics (+.WinForms)
```
No consumer→consumer references. Note: `Catalog.Schema.Target` is Catalog's **own POCO**, not Core's `Targets.Target` — the Catalog→Core edge (2026-07-29) exists for `FieldFootprint`, not the target type.

## The contract — what's actually depended on (the pinned surface)

> Summary level; for member-level `file:line` usage, grep the consumer for the type name.

- **Astronomy.Diagnostics (the four-assembly stack — layering contract:
  `openspec/specs/diagnostics-platform-layering/`)** — *used by all three apps*: `Log` (Init · StartNewSession
  · Info/Warn/Error · Diag/IsDiagEnabled ·
  **LogFolderPath** — TP-only, and TP passes it to a *recursive directory delete*, so its meaning
  must never widen past the app's own log folder), `AppLogIdentity` (+ `VersionAssembly`,
  2026-08-10 — the plugin-host `build=` stamp source; null = entry assembly), `DiagDefault`.
  **Since 2026-08-10 the Ctrl+N dialog itself is Library surface**: TP and XFM drive
  `Astronomy.Diagnostics.WinForms.DiagnosticsDialog.ShowOrFocus(owner, contextProvider)` (shipped
  2026-08-06); TSM drives
  `Astronomy.Diagnostics.WinUI.DiagnosticsWindow.ShowOrFocus(owner, contextProvider, iconPath?)`
  (ported from its app-side window, shipped TSM v1.5.1 2026-08-10). `ObservationSession` (`Begin` — **takes a required platform `capture` delegate since
  2026-08-10**, on Windows `ScreenCapture.ToPng` from
  `Astronomy.Diagnostics.Windows` · `CaptureAsync` · `CompleteAsync` · `Cancel` ·
  `Id`/`CaptureCount`/`IsTerminated`) + `ObservationCapture` (`Path`/`StatusText`/`Succeeded`)
  remain pinned surface — the shells are Library-side now, but IS (programmatic, no dialog) and
  the contract bench drive the session directly. `Log.UserObservation*` and
  `Log.NewObservationScreenshotPath` stay in-assembly composition, not consumer surface (like
  `Log.FilePath`, which feeds the session's status text; `Log.ScreenshotsFolderPath` likewise —
  dead-surface list, but the composition root of the screenshot chain. Not safe to delete
  outright.)
  **WindowsAppSDK lockstep**: `Astronomy.Diagnostics.WinUI` pins `Microsoft.WindowsAppSDK`
  (currently **2.3.1**, TFM floor `net10.0-windows10.0.19041.0`); WinUI consumers (TSM, ISM) must
  reference a WindowsAppSDK **≥ the pin** — NuGet unifies upward at app build, so drift fails as a
  restore warning, not silent breakage. Upgrade the pin alongside the first consumer that moves.
  The TFM half is harder-edged: referencing `.WinUI` obliges the **consumer's own TFM** to carry a
  Windows SDK version at or above the satellite's `net10.0-windows10.0.19041.0` floor — an app on
  plain `net10.0-windows` cannot reference it at all (a reference incompatibility, not a restore
  warning). All three consumers unified at `net10.0-windows10.0.26100.0` on 2026-08-10; AL's central
  TFM stays deliberately OS-version-less (→ `VERIFICATION.md`).
  **When wiring a new AL project into a consumer, also add it to that consumer's sln/slnx** — the
  apps list cross-repo AL projects explicitly, and a `ProjectReference` alone builds green while
  Solution Explorer silently omits the project (bit both apps 2026-08-10; the trap is documented in
  each app's `VERIFICATION.md`).
- **Astronomy.Catalog** — *TSM*: `Scan.ImageLibraryScanner.ScanAsync` + `ImageLibraryReport`;
  `Scan.MosaicConvention.PanelLabel`; `Scan.FilterPurpose` + `Scan.FilterPurposeClassifier.Classify`;
  `Build.TargetResolver.Resolve` + `ResolveOptions` +
  `CatalogGraph` + `CatalogBuildReport` and its typed issue rows —
  `NameMismatch`/`AmbiguousMatch`/`InvalidTsTarget`/`DuplicateTsTarget`/`UnanchoredTsTarget`/`TargetMatchIssues`;
  `Reconcile.ReconciliationProjection.Project` + `TargetCells`/`ReconciliationCell`;
  `Scan.RotationExpression` (the Sky/Mechanical/Unknown enum — frozen into TSM's own
  `ReconciliationRow` and its rotation formatter) and the static `Scan.FramingCluster.Fold180`
  (**TSM applies the library's fold to the *plan-side* target rotation** so plan and disk rows read
  identically — changing the enum's members or the fold convention silently desynchronizes the two
  planes in the consumer);
  `TargetScheduler.TargetSchedulerReader`(+`TsPlanData` and its element records `TsTarget`/`TsProject`);
  `TargetScheduler.TargetSchedulerEditor.TrySetField`(+`FieldEditResult`/`RefusalReason`/`TsTable`) +
  `.TryInsertRows`(+`TsRowInsert`/`InsertOutcome`/`RowInsertResult` — TSM's disk-row adoption inserts
  target/plan rows through it and replays them at push) + `.ReadPlanEffectiveExposure`; `TargetScheduler.EffectiveExposure.Seconds` (the standalone static
  rule class — distinct from the editor method); `TargetScheduler.TsEditableSchema`
  (`.For`/`.EnumValues`/`.Find`/
  `.IsCadenceBreaking` + `TsField`/`TsFieldType`/`TsCadenceClear`/`TsEnumValue` — TSM's field editors
  are schema-driven off this surface); `TargetScheduler.TargetSchedulerWriter` +
  `TargetScheduler.WriteBackPlanner` + the plan DTOs it produces and the writer consumes —
  `WriteBackPlan`/`PlannedWrite`/`ManualPlan`/`ManualGroup`/`ManualReason`/`ReconcileNote`
  (`WriteBackPlan` is the *argument type* of `TargetSchedulerWriter.Execute`, so its shape is
  unavoidably contract surface) — plus `Execute`'s return family
  `WriteBackResult`/`WriteBackChange`/`WriteBackVerifyFailure`.
  **`Astronomy.Catalog.Schema`** — TSM binds the row records directly and heavily: `Target`,
  `Project`, `ExposurePlan`, `ExposureTemplate` + the `TargetSource` enum (~121 references across
  ~17 files). See the positional-ctor hazard under *Fragility flags*.
  *(TSM's own `TsWriteBackApplier`/`WriteBackStep` are **consumer-side** types built on
  `WriteBackPlanner` — not Library surface, listed here previously in error.)*
- **Astronomy.Core** — *TP* (broad): `Targets.Target`, `Locations.Location`, `Night.*`,
  `Session.{BestSession, SessionAltitude, TransitTime, CoarseVisibility, AltitudeCurve, AltAzSample}`
  (`AltAzSample` is the declared element type of `AltitudeCurve.Sample`'s return, frozen into TP's
  cache-entry API),
  `Session.SessionSolvers.{LongestDuration, LowestHorizon}` + `Session.TargetOrdering.{ByTransit, ByRise}`
  (TP's four sort modes — a signature change here is a TP build break),
  `AltAzCalculator`/`AltAz`, `Astrometry.{ObserverInfo, AstroUtil, Refraction, RiseAndSetEvent}`
  (`RiseAndSetEvent` is the declared return type of `AstroUtil.GetMoonRiseAndSetForNight`),
  `Time.{SiderealTime, ObservationMoment}`,
  `TargetGeometry`, `Moon.{MoonSeparation, MoonEphemeris, LunarAge, MoonLimitProfile}`,
  `Horizons.{IHorizonProfile, ScalarHorizonProfile, PolylineHorizonProfile, MaxOfHorizonProfile}`
  (TP composes the polyline against the scalar floor via `MaxOfHorizonProfile`; its chart cache
  persists that shape verbatim), `Sun.SunPosition`, `Brightness.{Bortle, SkyBrightness}`.
  — *TSM* (narrow, since 2026-07-23; all in one file, `VisibleTonightPass`): `Locations.Location`,
  `Horizons.ScalarHorizonProfile`, `Night.{NightCalculator.ComputeNight, NightWindow}`,
  `Session.CoarseVisibility`, `Targets.Target`. (No `BestSession`, no `IHorizonProfile` — TP is the
  reason those stay pinned, not TSM.)
  — *XFM* (since v2.4.0): exactly `Astrometry.WcsOrientation.FromCdMatrix` and the returned record's
  `PositionAngleDegrees` / `Flipped` / per-axis arcsec-px scales, consumed in its ASTAP solve step and
  written to `OBJCTROT` — live consumer surface (never read it into the dead-surface list); a
  signature change there is an XFM build break.
- **Astronomy.XISF** — *TP call sites* (the assembly itself flows transitively via NINA — no direct
  ProjectReference): `XisfHeaderReader.ReadAsync` + `XisfHeader.{RaDegrees, DecDegrees,
  ObjectName, ImageType}`. Catalog's scanner (TSM's path) uses a **subset** of the typed accessors:
  `ObjectName`, `RaDegrees`, `DecDegrees`, `DateObsUtc`, `ExposureSec`, `Gain`, `OffsetRaw`,
  `SetTempC`, `XBinning`, `YBinning`, `Instrument`, plus the framing set —
  `RotatorSkyAngleDeg`, `RotatorPosAngleDeg` (2026-07-29, framing clusters) and `PixelWidth`,
  `PixelHeight`, `FieldWidthDeg`, `FieldHeightDeg` (2026-07-29, footprint overlap). Not the full
  surface; TP additionally reads `ImageType`, and the rest have no caller anywhere (see dead
  surface). *(`OffsetNormalized` was deleted 2026-07-27 — XFM never divided, so offset is raw on
  both planes.)*
  — *XFM* (since v2.4.0, 2026-08): `XisfHeaderReader.ReadAsync` + `XisfHeader` (ASTAP solve hints);
  `XisfBlockRewriter.RewriteAsync` in **both shapes** — `BlockCodec.None` to a temp path (the
  uncompressed input ASTAP needs) and `BlockCodec.Zstd` with an explicit level in place (browse
  hygiene) — plus the returned `XisfBlockRewriteResult.{AttachmentOffset, AttachmentSize,
  Compression}`, written straight back into XFM's cached geometry (the members exist *because* the
  caller holds cached geometry); `XisfChecksumVerifier.VerifyAsync` with **all three
  `XisfChecksumVerdict` arms** surfaced in its UI plus `XisfChecksumResult.Detail` (collapsing
  `NoChecksum` into a failure would be a silent behavior change that compiles); and
  `XisfBlockCompression.Compress/Decompress/VerifyChecksum` +
  `BlockCodec`/`BlockCompressionInfo.{CodecName, HasChecksum, ItemSize}` (its vendored
  `Files\Compression\` duplicate is retired).
  **The keyword surface stays a demand-driven subset** — `Astronomy.XISF` grows an accessor only when
  a consumer needs it, rather than mirroring XFM's `Keyword/KeywordList.cs`; as of v2.4.0 the two
  surfaces coexist in one process (XFM still carries its own ~50-member `KeywordList`), so keyword
  convergence is an open cross-repo coordination item, not a completed migration (its port is queued
  with XISF Tier 2 — see `ROADMAP.md` § *Open: Astronomy.XISF Tiers 2 & 4*).
- **Astronomy.NINA** — *TP, only `Persistence`*: `NamedSite`, `PlanningPreferencesDto`. (The root
  namespace + `ReportToTargetAdapter` have no external consumer — see dead surface.)

## Semantic assumptions — the contract beyond signatures (pinned by `Astronomy.Contracts.Tests`)

Compiler-invisible expectations consumers bake in. Each is either covered by a citing contract test
or registered in `NotCleanlyTestableAssumptions.cs` with the reason (see *How this is validated*).
**Admission rule** (decided 2026-07-07, `contracts-tests-refresh` § D1): what earns a number is a
*single* compiler-invisible semantic — a change that compiles cleanly on both sides yet silently
changes results — one semantic per number; umbrella entries covering a family of behaviours were
explicitly rejected because the list's value is one assumption per line with one independent test.

**Units / encoding (silent-wrong-result risk — highest value):**
1. `XisfHeader.RaDegrees` is **degrees** (TP ÷15 for hours).
2. `NINA.Persistence.PlanningPreferencesDto.MinDurationMinutes` is **minutes** (serialized in NamedSite).
3. `Moon.MoonSeparation.ObserveAt` returns **geometric** `MoonAltDeg` (TP adds refraction itself; apparent would double-apply).
4. `Brightness.SkyBrightness.KsAt` — **10 positional params, order load-bearing** (reorder compiles, computes wrong).
5. `Build.CatalogGraph` lists are **FK-insert order; mosaic panels immediately after their parent** (TSM nesting depends on it).

**Call-order / lifecycle:**
6. `Log.Init` **gates the silent no-op** — before `Init`, every `Log.*` silently does nothing. `StartNewSession` is rotation-only (must follow `Init` and precede logging you want inside the rotated session; skipping it costs the session boundary, not the trail).
7. `Night.NightCache.ComputeYearStartDay` / `ComputeYearDaysCount` are **pure statics called before the ctor**.
8. `TargetSchedulerReader`/`Editor` **open the DB in their ctor** — file must exist; reader is single-use.
9. `TargetSchedulerEditor.HasRequiredColumns` (`Id,guid,active`) **gates ALL writes** (else `RefusalReason.SchemaIncompatible`): `TrySetField` (field edits) and `TryInsertRows` (batch-atomic row creation, 2026-08-03) are the editor's **only public write paths** (structurally pinned by `EditorWriteSurfaceContractTests`). The formerly-public raw `Set*` setters that bypassed the gates were removed/internalized 2026-07-24 — `SetField` survives only as the internal engine behind `TrySetField`.
10. Editor write-back **key = `ImportedFromTsGuid`** (GUID string *or* TS int Id as decimal string; disambiguated by `long.TryParse`).

**Performance-coupled:**
11. `Session.BestSession.PlaceBest(..., altitudeQuality: null)` dispatches the **sin(alt) closed form (~25× faster)** — TP relies on the null default.
12. `AltitudeCurve.Sample` / `MoonEphemeris.Sample` (Meeus core) are **thread-safe/lock-free** (TP parallelizes per-target).
13. `MoonEphemeris.Sample(count)` returns **exactly `count`** elements (TP gates a cache hit on it).

**Input / path & process-global:**
14. `Scan.ImageLibraryScanner.ScanAsync(root)` expects `<target>/Captures/<Camera>/<Filter>/`; missing root throws `DirectoryNotFoundException`.
15. `Horizons.PolylineHorizonProfile(az[], alt[])` — parallel arrays; **length mismatch and empty input throw `ArgumentException`** (fail-fast); azimuths are normalized to `[0, 360)` and sorted internally — unsorted/duplicate input is accepted (duplicates tolerated, last wins).
16. **Non-UTC `DateTimeKind` throws, library-wide** (widened 2026-07-24; was `LunarAge.DaysAt` only). `Time.JulianDate.FromUtc` is the central gate every time-based primitive funnels through, so `AltAzCalculator.At`, the `Session.*` helpers, `MoonSeparation.*` and friends now reject `Local`/`Unspecified` with `ArgumentException` instead of silently reinterpreting the instant as UTC. Converting entry points (`AstroUtil`, `MoonEphemeris`, `NightCalculator`, `Sun.*`) still accept any `Kind` — they call `TimeKindGuard.AsUtc` first. **Both consumers already satisfied this by construction** (TP funnels everything through `ObservationMoment`; TSM through `DateTime.UtcNow`), so the gate was a runtime no-op at introduction.
17. `Time.ObservationMoment.Zone` must stay in lockstep with `Location.TimeZoneInfo`.
18. *(retired 2026-07-06)* ~~`TsEditGate`/editor calls `SqliteConnection.ClearAllPools()` after every verified write~~ — TSM's sync-model rework (commit `9e8ec19`) deleted the call: edits now hit a **local working copy** (pull at open / push-as-replay), so the stale-SMB-read concern the call defended against no longer exists. Kept numbered so the assumption list stays stable.

**TS editing / write-back (surface shipped 2026-07-05/06; assumptions added 2026-07-07):**

19. `EffectiveExposure.Seconds` — a plan's whole-second effective exposure is **its own value when set, else its template's default**; the raw-TS overload treats a **negative** exposure as TS's "use template default" sentinel and **`0` as a literal zero-second exposure**; both-null → **0** (never matches a scanner bucket, all ≥ 1). *(Adjudicated 2026-07-07 against the TS source: the planner's sentinel test is exactly `!= -1` — `PlanningExposure.cs` — so 0 is literal; #20's SQL was the outlier (`> 0`) and was aligned. TS's sync-client path re-marks `<= 0` as unset — a client wrinkle, deliberately not mirrored.)*
20. `TargetSchedulerEditor.ReadPlanEffectiveExposure` resolves the sentinel **through the template** (the #19 rule as SQL) and returns `Found=false` for an unknown key *or a missing template row* — TSM seeds its exposure editor from the resolved value, never the raw sentinel.
21. `TsEditableSchema.EnumValues` **codes are the persisted TS ints** (authored from the TS source enums); a renumber compiles but writes wrong values into the TS DB. `For`/`Find` are the exact editable-column set TSM's schema-driven editors are generated from; `exposureplan.exposure` carries the `-1` "template default" sentinel metadata.
22. `TsEditableSchema.IsCadenceBreaking`/`TsField.Clears` gate cadence handling: a cadence-breaking edit **clears the scoped `filtercadenceitem` rows in the same transaction** as the column write (an unchanged-value edit is a verified no-op — **no clear**); a **target-scope clear refuses** (`RefusalReason.HasOverrideOrder`) when the target has hand-authored override-order rows, leaving the DB untouched.
23. `TargetSchedulerWriter.Execute` is **update-only**: existing `exposureplan` rows, three columns (`acquired`/`accepted` set to disk count, `desired` **ratcheted to `max(old, new)`** — raised, never lowered) — never inserts/deletes rows, never alters the journal mode; `DiskCount = 0` is a **real write**, not a skip.

**K-S moon gate (shipped 2026-07-24; replaced the Lorentzian):**

24. The moon gate (`MoonClearIntersect` behind `BestSession`/`SessionSolvers`) **refraction-corrects moon altitude internally** (Saemundsson, the Sky-chart convention) — #3 still holds: `MoonSeparation.ObserveAt` returns **geometric**; a consumer adding its own refraction before handing altitudes to the gate would double-apply. And `Δmag` (`SkyBrightness.KsMoonDeltaMag`) is **bandwidth-independent by construction** — the profile carries band *center* only; per-filter moon policy differences are expressed through `ToleranceMag`, not band fields. Site inputs (`v0Mag`, band-k) derive from the `Location` passed to the session helpers, never from the profile.

**ObservationSession (shipped 2026-07-24; the shared dialog orchestration):**

25. `ObservationSession` logs **exactly one START** (at `Begin`) and **exactly one terminator**
   (END via `CompleteAsync`, or CANCEL via `Cancel`) per id; terminators are idempotent and latch
   `IsTerminated`; post-termination captures/completes are no-ops that never touch the caller's
   delegates — including the platform `capture` delegate `Begin` requires since 2026-08-10.
   Delegates run on the **`CaptureAsync`/`CompleteAsync` caller's** synchronization
   context (no context is captured at `Begin`) — **call from the UI thread**. (Both dialog shells
   wire `Cancel` to their close-X fallback; whichever terminator fires first wins.)

### Contract facts not yet numbered

Behaviours consumers already depend on, documented here but **not yet pinned as numbered
assumptions** — numbering them requires a bench test or a registry entry, which is a code change (see
`ROADMAP.md` § *Open: pin the unnumbered contract facts*):

- **Cancellation throws; no partial result is ever returned.** Every long-running Catalog entry point
  takes an optional `CancellationToken` and genuinely observes it: `TargetSchedulerReader.ReadPlanData`
  and the `Read*` family (forwarded to the private `Query<T>` choke point, checked **per row**),
  `TargetResolver.Resolve` (each phase boundary, plus per TS target in the anchoring pass — the one
  super-linear loop), `CatalogBuilder.BuildAsync`, and `ImageLibraryScanner.ScanAsync`. A cancelled call
  throws rather than returning a truncated graph or report. Because the parameters are optional, a
  regression here is **compiler-invisible** — nothing breaks if the token silently stops being observed.
  (Guarded today by `Resolve_ObservesCancellation` in `Astronomy.Catalog.Tests`, not by the bench.)
- **Write-back's join key is (target, filter, purpose, whole-second exposure), credited by pairing.** A
  plan receives the disk count at exactly its `round(ExposureSeconds ?? template default)` bucket (filter
  compared ordinal-ignore-case), and **within the bucket only frames whose capture configuration pairs
  with the plan's template count** (`CaptureConfigPairing` — gain/offset/binning value equality after
  plan-side normalization; a camera-default sentinel `-1` pairs with nothing) and whose framing serves the
  target's rotation (2026-08-04, `pairing-credited-write-back`). Same-purpose plans at different durations
  resolve to separate writes rather than manual; a disk bucket with no plan at that duration — or frames
  whose configuration no plan pairs with — surfaces as an `UnplannedFrames` `ReconcileNote` and is never
  folded into a neighbouring plan; and no pairing frames at the plan's spec is a real `DiskCount = 0`
  write ("spec unmet"), not a skip. This is a silent-wrong-result surface — a duration or configuration
  mismatch writes 0 to a live TS plan.
- **The XISF codec-layer semantics XFM bakes in** (added 2026-08-11 — the third consumer arrived at
  v2.4.0 without its pins): checksums cover the **stored/post-compression** bytes; LZ4 is the **raw**
  block format, so decode is impossible without the declared uncompressed size; and
  `BlockCompressionInfo.Parse` is tolerant on an unknown token (returns `BlockCodec.Other` so
  inspect-only reads keep working) while `Decompress`/`Format` throw naming it — the
  tolerant-parse/strict-use split is the contract, not an oversight. Normative:
  `openspec/specs/xisf-block-compression/` (do not restate it here).
- **The `WcsOrientation` conventions XFM bakes in**: position angle is North-toward-East `[0,360)`,
  parity comes from the CD-matrix determinant sign, and a both-axes mirror is indistinguishable from
  a 180° rotation by construction. Normative: `openspec/specs/wcs-orientation/`.

## Fragility flags
- **Three public `Target` types** — `Core.Targets.Target` (class), `NINA.Target` (class), `Catalog.Schema.Target` (record). Naming-overload hazard; consumers alias around it.
- **Positional-ctor coupling (latent, partially defused)** — the wide record ctors are still a
  same-typed-reorder hazard (`CatalogBuildReport` 10 required + 4 optional; `Schema.Target` 19 —
  18 required + optional `ParentTargetId`). Defused only for `Schema.Target` (TSM helpers use named
  args from `ProjectId:` onward, and the two leading positional args `Id`/`Source` are differently
  typed). **Still live** for the other three families: every TSM `ExposurePlan` helper opens with
  three adjacent positional `Guid`s, `ExposureTemplate` helpers with two adjacent `Guid`s + two
  adjacent `string`s, and one `CatalogBuildReport` construction is fully positional
  (five same-typed ints + five lists). A same-typed reorder there compiles silently.
- **Transitive load-bearing refs invisible in the consumer `.csproj`** — TP uses XISF via NINA; TSM uses XISF via Catalog. Dropping a transitive edge breaks the consumer far from the cause.
- **Consumer-local DTOs freeze a Library-type subset** — TP `LocalTargetStore` persists only `Name/RA/Dec/North`. A new *required* `Target` ctor param breaks TP's named-argument call site loudly at compile time; the **silent** loss applies to *optional/defaulted* additions — and is already live today: TP hardcodes `directory: string.Empty` / `enabled: true` on load, so those two fields never round-trip.

## Dead / speculative public surface (no external consumer — a *design-review* decision)

A large fraction of the public API has **no external caller** (only Library tests / internal composition):
**all of Astronomy.PCL** · **most of Astronomy.NINA root** (+ `ReportToTargetAdapter`) · **most of
Astronomy.Catalog persistence** (`CatalogStore`, `SchemaManager`, `CatalogBuilder`, `Reconciler` —
`WriteBackPlanner`/`TargetSchedulerWriter` left this list 2026-07-06 when TSM's write-back shipped) ·
many **Core statics**
(`Sun.*` beyond `SunPosition` — `SunEvents`, `SunPower`, `SunTracking`, `SunSeparation`,
`SunHeliographic`; `Night.TwilightCalculator` **and** `Brightness.Twilight` — two distinct types,
both uncalled; and in `Session` exactly
**`VisibilityWindows`, `IntegratedQuality`, `QualitySamples`, `RiseSet`**) ·
the `XisfHeader` members with no caller anywhere (the weight/quality block, optics/pointing, the
unused focuser accessors, `CcdTempC`, `InstrumentDescription`, `Filter`, `KeywordNames`) ·
`Core.Horizons.ObstructionTableHorizonProfile` (the fourth `IHorizonProfile` implementation — its
three siblings are all live TP surface) · `Catalog.TargetScheduler.SingleTargetPlanner` (the
surgical sibling of `WriteBackPlanner`; the bulk path left this list 2026-07-06 when TSM's
write-back shipped, the surgical path never followed) ·
**Diagnostics** `Log.FilePath` / `Log.ScreenshotsFolderPath`.

> **The `Session` members are named deliberately.** `SessionSolvers` and `TargetOrdering` are **live TP
> surface** (four call sites, TP's sort modes) and must never be read into this list — an earlier
> unnamed "several `Session.*`" would have sanctioned pruning them.

→ **Do not prune.** The retention decision and its revisit-trigger are forward-looking, so they live in
`ROADMAP.md` § *Open: public-surface retention — API ahead of its consumers*. Short version: this is
API ahead of its consumers (the planned IS plugin, XFM's migration), not dead generality. **This
section is the inventory; the roadmap holds the policy.**
