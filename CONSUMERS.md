# CONSUMERS.md — Astronomy Library datasheet

**Charter / datasheet.** The Library's de-facto public **contract**: what each consumer actually
depends on (the "pinned pinout"), the semantic assumptions beyond signatures, and the dependency
graph. Treat this as the **stable interface** — change it *deliberately* (a pinout revision = update
consumers + tests in the same breath); the implementation behind it can churn freely. Derived from
grep-verified real usage (2026-06-28); keep current as the contract evolves.

**How this is validated** (docs *describe* the contract; these *verify* it):
- **Structural — free:** both consumers `ProjectReference` the Library (source), so a breaking API
  change is a **consumer build break**. The constellation build (`..\build-all`) is that check.
- **Semantic:** the compiler can't see the assumptions in the last section — those want **contract
  tests** in the Library.

---

## Consumers (only two, today)

| Consumer | Host | ProjectRefs (direct) | Actually uses |
|---|---|---|---|
| **TargetPlanner (TP)** | WinForms | Core, NINA, Diagnostics | Core (broad), `NINA.Persistence`, XISF (4 members, transitive via NINA), Diagnostics. **Catalog present transitively but unused.** PCL deliberately *not* referenced (avoids `unsafe` in the WinForms host). |
| **TargetSchedulerManager (TSM)** | WinUI | Catalog, Diagnostics | Catalog (broad), Diagnostics. XISF only *inside* Catalog's scanner. **Core / NINA / PCL not referenced.** |

Both consume **by `ProjectReference` (source) — no DLL, no NuGet.** That's the free continuity check.

**Not consumers** (despite portfolio proximity — important to be honest about):
- **XisfFileManager** — has its own independent XISF/SQLite stack; *migration to Library is
  aspirational, not done.* Zero Library references today.
- **IntervalScheduler** — empty WPF stub, zero references.
- **LibraryCatalogManager** — does not exist (no project in the tree).
- NINA plugin/source + other Astronomy projects — no Library reference.

So the live constellation is a **3-node graph** (Library → TP, TSM), not the 5-consumer web the docs imply.

## Dependency graph

```
Core (base)      XISF (base)      Diagnostics (base)
Catalog → XISF
NINA    → Core, XISF, Catalog
PCL     → Core  (+ native Astronomy.PCL.Native, build-only)

TP  → Core, NINA, Diagnostics      (XISF + Catalog transitive via NINA)
TSM → Catalog, Diagnostics         (XISF transitive via Catalog)
```
No consumer→consumer references. Note: **Catalog does NOT depend on Core** (its `Schema.Target` is its own POCO).

## The contract — what's actually depended on (the pinned surface)

> Summary level; for member-level `file:line` usage, grep the consumer for the type name.

- **Astronomy.Diagnostics** — *used by BOTH* (the only shared assembly): `Log` (Init · StartNewSession
  · Info/Warn/Error · Diag/IsDiagEnabled · UserObservation* · NewObservationScreenshotPath),
  `AppLogIdentity`, `DiagDefault`, `ScreenCapture.ToPng`.
- **Astronomy.Catalog** — *TSM*: `Scan.ImageLibraryScanner.ScanAsync` + `ImageLibraryReport`;
  `Scan.MosaicConvention.PanelLabel`; `Build.TargetResolver.Resolve` + `ResolveOptions` +
  `CatalogGraph` + `CatalogBuildReport` + `UnanchoredTsTarget`/`TargetMatchIssues`;
  `Reconcile.ReconciliationProjection.Project` + `TargetCells`/`ReconciliationCell`;
  `TargetScheduler.TargetSchedulerReader`(+`TsPlanData`);
  `TargetScheduler.TargetSchedulerEditor.TrySetField`(+`FieldEditResult`/`RefusalReason`/`TsTable`).
- **Astronomy.Core** — *TP* (broad): `Targets.Target`, `Locations.Location`, `Night.*`,
  `Session.{BestSession, SessionAltitude, TransitTime, CoarseVisibility, AltitudeCurve}`,
  `AltAzCalculator`/`AltAz`, `Astrometry.{SiderealTime, ObserverInfo, AstroUtil, Refraction}`,
  `TargetGeometry`, `Moon.{MoonSeparation, MoonEphemeris, LunarAge, MoonAvoidanceProfile}`,
  `Horizons.{IHorizonProfile, ScalarHorizonProfile, PolylineHorizonProfile}`, `Sun.SunPosition`,
  `Brightness.{Bortle, SkyBrightness}`, `Time.ObservationMoment`.
- **Astronomy.XISF** — *TP directly*: `XisfHeaderReader.ReadAsync` + `XisfHeader.{RaDegrees, DecDegrees,
  ObjectName, ImageType}`. The full typed-accessor surface is used *inside* Catalog's scanner (TSM's path).
- **Astronomy.NINA** — *TP, only `Persistence`*: `NamedSite`, `PlanningPreferencesDto`. (The root
  namespace + `ReportToTargetAdapter` have no external consumer — see dead surface.)

## Semantic assumptions — the contract beyond signatures (contract-test candidates)

Compiler-invisible expectations consumers bake in. Each is a candidate for an explicit Library test.

**Units / encoding (silent-wrong-result risk — highest value):**
1. `XisfHeader.RaDegrees` is **degrees** (TP ÷15 for hours).
2. `NINA.Persistence.PlanningPreferencesDto.MinDurationMinutes` is **minutes** (serialized in NamedSite).
3. `Moon.MoonSeparation.ObserveAt` returns **geometric** `MoonAltDeg` (TP adds refraction itself; apparent would double-apply).
4. `Brightness.SkyBrightness.KsAt` — **10 positional params, order load-bearing** (reorder compiles, computes wrong).
5. `Build.CatalogGraph` lists are **FK-insert order; mosaic panels immediately after their parent** (TSM nesting depends on it).

**Call-order / lifecycle:**
6. `Log.Init` → `Log.StartNewSession` **must precede any `Log.*`** (else silent no-op).
7. `Night.NightCache.ComputeYearStartDay/Count` are **pure statics called before the ctor**.
8. `TargetSchedulerReader`/`Editor` **open the DB in their ctor** — file must exist; reader is single-use.
9. `TargetSchedulerEditor.HasRequiredColumns` (`Id,guid,active`) **gates ALL writes** (else `RefusalReason.SchemaIncompatible`).
10. Editor write-back **key = `ImportedFromTsGuid`** (GUID string *or* TS int Id as decimal string; disambiguated by `long.TryParse`).

**Performance-coupled:**
11. `Session.BestSession.PlaceBest(..., altitudeQuality: null)` dispatches the **sin(alt) closed form (~25× faster)** — TP relies on the null default.
12. `AltitudeCurve.Sample` / `MoonEphemeris.Sample` (Meeus core) are **thread-safe/lock-free** (TP parallelizes per-target).
13. `MoonEphemeris.Sample(count)` returns **exactly `count`** elements (TP gates a cache hit on it).

**Input / path & process-global:**
14. `Scan.ImageLibraryScanner.ScanAsync(root)` expects `<target>/Captures/<Camera>/<Filter>/`; missing root throws `DirectoryNotFoundException`.
15. `Horizons.PolylineHorizonProfile(az[], alt[])` — parallel arrays; length/monotonic/dedup preconditions are caller's to honor.
16. `Moon.LunarAge.DaysAt` throws on non-UTC `DateTimeKind`.
17. `Time.ObservationMoment.Zone` must stay in lockstep with `Location.TimeZoneInfo`.
18. **`TsEditGate`/editor calls `SqliteConnection.ClearAllPools()` after every verified write** — required against stale SMB reads, but **AppDomain-global** (disturbs any other SQLite connection in-process).

## Fragility flags
- **Three public `Target` types** — `Core.Targets.Target` (class), `NINA.Target` (class), `Catalog.Schema.Target` (record). Naming-overload hazard; consumers alias around it.
- **Positional-ctor coupling** — TSM tests build Library records positionally (`CatalogBuildReport` 11 args, `Schema.Target` 18, …); a same-typed reorder compiles *wrong*.
- **Transitive load-bearing refs invisible in the consumer `.csproj`** — TP uses XISF via NINA; TSM uses XISF via Catalog. Dropping a transitive edge breaks the consumer far from the cause.
- **Consumer-local DTOs freeze a Library-type subset** — TP `LocalTargetStore` persists only `Name/RA/Dec/North`; new required `Target` ctor fields silently won't round-trip.

## Dead / speculative public surface (no external consumer — a *design-review* decision)

A large fraction of the public API has **no external caller** (only Library tests / internal composition):
**all of Astronomy.PCL** · **most of Astronomy.NINA root** (+ `ReportToTargetAdapter`) · **most of
Astronomy.Catalog persistence + write-back** (`CatalogStore`, `SchemaManager`, `CatalogBuilder`,
`Reconciler`, the whole `WriteBackPlanner` family, `TargetSchedulerWriter`) · many **Core statics**
(`Sun.*` beyond `SunPosition`, several `Session.*`, `TwilightCalculator`, …) · **XISF `Compression`** +
most typed accessors.

→ Decision for the user: is each block **intended future API** (a consumer is coming — e.g. XFM's
planned migration, a TSM write-back action) or **speculative generality** to *internalize/prune*? A
smaller public surface = a clearer contract = easier to keep stable. (This is the "enforce consistency
& good design" lever.) Don't prune blindly — the write-back family, for instance, is built+tested for a
*planned* TSM action.
