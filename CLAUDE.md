# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout

`Astronomy.sln` is **x64-only** (Debug/Release × x64; no AnyCPU/x86) and holds twelve buildable projects, plus a `PCL` Solution Folder containing eight view-only PCL projects (`PCL.vcxproj`, the six 3rd-party `.lib`s — `cminpack`, `lcms`, `lz4`, `RFC6234`, `zlib`, `zstd` — and the `xisf.vcxproj` CLI utility) sourced from `Library\PCL\`. The PCL projects have `ActiveCfg` set but `Build.0` omitted: full source visibility and IntelliSense / F12 in the IDE, but `Build Solution` and `msbuild Astronomy.sln` skip them. PCL rebuilds happen manually via `Library\PCL\src\pcl\windows\vc18\PCL.sln`.

The twelve buildable projects:

- **`Astronomy.Core`** — `net10.0-windows`, `LangVersion latest`, `Nullable enable`. The library proper. Pure managed C# with **no NuGet dependencies** (post-`2249834` CoordinateSharp removal — every helper is now Meeus-backed). XML doc generation is on (`GenerateDocumentationFile=true`), so public surface is expected to carry `///` docs. Buildable independently with `dotnet build` if a contributor wants only the managed primitives. The original netstandard2.0 floor was lifted to net10.0 on 2026-05-04 (commit `b834f52`) once NINA's upstream migration confirmed every consumer was on modern .NET; then narrowed to `net10.0-windows` on 2026-05-11 (commit `e7ae75c`) when the VS2026 settings review formalized Windows-only intent (the prior uncommitted `net10.0-windows10.0.26100.1` pin was over-tight and broke the build via NETSDK1229 / MSB4184).
- **`Astronomy.Core.Tests`** — `net10.0-windows` x64, `OutputType=Exe`. **xUnit v3** tests for `Astronomy.Core` (`Tests/`), including the native PCL round-trip tests under `Tests/PCL/` — so it references both `Astronomy.Core` and `Astronomy.PCL` and pulls `Astronomy.PCL.Native` (vcxproj) into its graph (build with `msbuild`, then `dotnet test --no-build`). Benchmarks split out to `Astronomy.Core.Benchmarks` on 2026-06-21 when this adopted xUnit v3 (v3 generates the assembly entry point, which collided with the old `BenchmarkSwitcher` `Main`). `xunit.runner.visualstudio` is intentionally on the `4.0.0-pre.X` prerelease line — VS2026 IDE test discovery and the .NET 10 SDK both need the 4.x architecture, but the stable channel hasn't released 4.x yet (stable is still on the older 3.x line, which would be a downgrade from `4.0.0-pre` not an upgrade).
- **`Astronomy.Core.Benchmarks`** — `net10.0-windows` x64, `OutputType=Exe`. The BenchmarkDotNet harness (`Program.cs` + `FmaBenchmarks` / `HotPathBenchmarks` / `AltitudeCurveBenchmark`), split out of `Astronomy.Core.Tests` on 2026-06-21. References only `Astronomy.Core`'s public surface — **pure-managed, no `PCL.Native`** — so it builds and runs under plain `dotnet run -c Release` (no SLN / MSBuild). The `InProcessEmit` toolchain is retained from its old native-graph home but no longer required.
- **`Astronomy.PCL.Native`** — vcxproj, x64-only C++ DLL. Statically links the vendored PixInsight Class Library (`Library\PCL\lib\x64\$(Configuration)\*-pxi.lib`). Public surface is the `extern "C"` C ABI in `include\Astronomy\PCL\XisfCApi.h`. Mirrors PCL's build flavor (`/MD`, `/fp:fast`, `stdcpp17`, `__PCL_WINDOWS`, `__PCL_AVX2`, `__PCL_FMA`) using the same MSVC toolset PCL itself uses (`v145` as of VS2026 — both wrapper and PCL bumped together); compiled with `AdvancedVectorExtensions2` (AVX2) for portability — PCL's own AVX-512 paths remain runtime-gated inside the static lib. The wrapper itself does no numerical work, but matching PCL's flags keeps any PCL header inlines instantiated in the wrapper TU bit-identical to the same inlines inside the static libs.
- **`Astronomy.PCL`** — `net10.0-windows` x64, `LangVersion latest`. Managed P/Invoke wrapper. Public surface: `XisfFile : IDisposable` (`Open` / `SelectImage` / `ReadImageF32`), `XisfImageInfo`, `XisfColorSpace`, `XisfException`. Internal `NativeMethods` in `Interop/` holds the `[DllImport]` declarations. `<InternalsVisibleTo Include="Astronomy.Core.Tests" />` lets the smoke test bypass the wrapper. The TFM history: originally net8.0 because VS2026 `MSBuild.exe` had a defect resolving `System.Runtime.InteropServices.DllImportAttribute` for netstandard2.0 projects (and TP, the only other potential consumer at the time, was on net481 but didn't use PCL); bumped to net10.0 on 2026-05-04 (commit `c7eeff9`) alongside the rest of the portfolio, then narrowed to `net10.0-windows` on 2026-05-11 (commit `e7ae75c`) alongside the same portfolio-wide VS2026 settings-review pass.
- **`Astronomy.XISF`** — `net10.0-windows` x64, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`. XISF (PixInsight image format) read library. Tier 1 surface (2026-05-18) is header-only read: `XisfHeader.cs` (typed FITS-keyword accessors carrying value + comment per keyword) and `XisfHeaderReader.cs` (header-only XISF parser, pure managed `XDocument.Parse()`, no native dep). Ported from `XisfFileManager/Files/XisfXmlReader.cs` + a subset of `XisfFileManager/Keyword/KeywordList.cs`. Designed for sharing across XFM, TP, ISP, and the user's other apps without dragging NINA's heavyweight `NINA.Image.FileFormat.XISF` (which is coupled to `NINA.Profile` / WPF / image-data factories and forces pixel decode on every read). Tiers 2-4 (header write-back, image read, image write) tracked in ROADMAP.
- **`Astronomy.XISF.Tests`** — `net10.0-windows` x64, `OutputType=Exe`. xUnit tests for Astronomy.XISF. Synthetic-XISF fixtures (no test-file dependency); 26 tests covering header parsing, FITS-value normalization, per-camera OFFSET normalization (incl. comment-field fallback), and bad-input error paths.
- **`Astronomy.NINA`** — `net10.0-windows` x64, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`. NINA-integration / planning library. ProjectReference to Astronomy.Core + Astronomy.XISF + **Astronomy.Catalog** — no NINA assembly dependency yet (deferred to Phase D when `InputTargetAdapter` lands). `<InternalsVisibleTo Include="Astronomy.NINA.Tests" />` exposes planning-model internals to tests. Current surface:
  - **Phase A — image-library scanner: MOVED to `Astronomy.Catalog`** (`Scan/`, 2026-06). Scanning the .xisf library is catalog work; the cluster depended only on `Astronomy.XISF`. NINA's `Xisf/` now keeps only `ReportToTargetAdapter` (the scan → `Astronomy.NINA.Target` bridge), so NINA now references Astronomy.Catalog (planning consumes inventory; no cycle).
  - **Phase B — rich `Target` shape + composition** (root namespace): `Target` wrapping `Astronomy.Core.Targets.Target` geometry + composed `IReadOnlyList<FilterHistory>` (per-filter history) + `IReadOnlyList<PlannedExposure>?` (forward-looking sequence plans). Leaf types `Filter` (with `FilterKind` enum + static factories Ha/OIII/SII/L/R/G/B), `ExposureSettings`, `PlannedExposure`, `FilterHistory` (carrying `FilterPurpose` Light/Stars distinction). Sealed + immutable + `With(...)` per AL convention. `Xisf/ReportToTargetAdapter.cs` bridges Phase A → Phase B: `report.ToTargets()`.
  - Forward roadmap (Phases C–D in `~/.claude/plans/what-is-next-from-crispy-garden.md`): TP migration off `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target` + Sky-chart Filter feature (Phase C); bidirectional `InputTargetAdapter` for NINA sequence-JSON export (Phase D).
- **`Astronomy.NINA.Tests`** — `net10.0-windows` x64, `OutputType=Exe`. xUnit tests for Astronomy.NINA (composition types, `ReportToTargetAdapter`, planning `Target`). The scanner tests + `ImageLibrarySmokeTest` moved to `Astronomy.Catalog.Tests/Scan/` with the scanner. Separate from `Astronomy.Core.Tests` because Astronomy.NINA will eventually pull NINA assemblies that have no business in Core's test graph.
- **`Astronomy.Catalog`** — `net10.0-windows`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`. The shared catalog-database library **and** the owner of the .xisf-library scanner. One `Catalog.db` (SQLite via `Microsoft.Data.Sqlite`) reconciles two sources onto **one canonical `target`**: the disk image library is **ACTUAL** (source of truth), N.I.N.A. Target Scheduler is the **PLAN**, and the catalog re-organizes the plan clean and anchored to actual. Each `target` carries both facets (disk identity + plan attributes) discriminated by `source_id` — `Actual` (on disk only), `Planned` (in TS only / not yet shot), `Both` (merged); `inventory_filter` (actuals per filter) and `exposure_plan` (goals) both hang off it. `Scan/` holds the moved `ImageLibraryScanner` + report types (depends on `Astronomy.XISF`). `Build/` holds the reconciliation: `TargetResolver` (pure, **coordinate-primary** — each TS target anchors to the nearest disk target within a tolerance, default 0.5° haversine; name only validates; disk plate-solved coords win on merge; the TS guid is **retained on `Both` for write-back**; TS duplicates fold onto one canonical and name-mismatch / ambiguous / unanchored / out-of-range rows are reported in `CatalogBuildReport`, not dropped) and `CatalogBuilder.BuildAsync` (full rebuild: scan disk + read TS → resolve → `WriteCatalog` in one transaction; either source may be omitted). `Schema/` holds the POCOs/mappers + `schema.sql`; `SchemaManager` applies it idempotently — **no migration framework**: the catalog is fully derived (scan + TS import) and rebuildable, so a schema change just means deleting `Catalog.db`. **Harden rule:** never pass a raw TS integer into a CHECK/FK column — `TargetResolver` coerces unknown epoch/state/priority codes to a safe default and normalizes/clamps planned RA/Dec, so a single bad external TS row can't abort the rebuild. `CatalogStore` is the read/write surface (`WriteCatalog(graph)` replaces the whole catalog transactionally; `GetShotTargets()` = source `Actual`|`Both` is XFM's actual-only view — a `Both` target has frames on disk, so it belongs; planned-only is excluded); `TargetScheduler/TargetSchedulerReader` reads TS's `schedulerdb.sqlite` read-only (Mode=ReadOnly + busy-timeout, explicit columns); `TargetScheduler/TargetSchedulerWriter` (+ pure `WriteBackPlanner` / `SingleTargetPlanner`) writes reconciled disk counts back into a **local** TS copy (Phase 4 engine — bulk or surgical single-target, per panel for mosaics; dry-run by default, column-presence-validated, read-back verified; the driving CLI verbs retired with the consumer's console host 2026-06-11, to resurface as a TSM app action). `Reconcile/` answers goal-vs-actual (`Reconciler` + `CatalogStore.GetReconciliation`): TS goals (`exposure_plan.desired_count`) vs disk actuals (`inventory_filter`) per (target, filter), joined on the shared single-letter filter name — actuals are disk truth (TS's stale `acquired_count` is ignored). `ReconcilePolicy.Combined` (default) counts Light + Stars toward a goal; status is NotStarted/InProgress/Complete/Unplanned. Consumed by XFM / TP / IS / ISP and the TargetSchedulerManager (TSM) app (renamed from TargetCatalogManager 2026-06-11). Pure-managed → builds with `dotnet build`.
- **`Astronomy.Catalog.Tests`** — `net10.0-windows` x64, `OutputType=Exe`. xUnit tests for Astronomy.Catalog: the moved scanner tests (`Scan/`), schema creation (WAL, lookups), the `WriteCatalog` round-trip, the `TargetResolver` units (Both/Planned/Actual classification, name-mismatch, coordinate dedup, tolerance boundary, angular separation, out-of-range coercion), `CatalogBuilder` against the pinned TS snapshot (102 planned-only / 10 projects / 20 templates / 662 plans), and the `Reconciler` units (goal-vs-actual, Combined vs LightOnly, full-outer-join, over-shot-doesn't-mask-gaps), and the write-back planners (`WriteBackPlanner` + surgical `SingleTargetPlanner` + the `TargetSchedulerWriter` round-trip + per-panel mosaic scan). 75 tests.
- **`Astronomy.Diagnostics`** — `net10.0-windows`, `Nullable enable`, `ImplicitUsings enable` (+ `System.Drawing.Common`). The portfolio's shared **logging + observation contract** (built 2026-06-11) — *convention-as-code*: `Log` (always-on Info/Warn/Error severity + gated `Diag` channels, default all-in-Debug / off-in-Release via the app's env var; session rotation; `%APPDATA%\<app>\Logs\` structure; USER_OBS protocol — all configured once per app via `AppLogIdentity`, since a shared library compiles once and can't read the consumer's `#if DEBUG`) + `ScreenCapture.ToPng` (System.Drawing `CopyFromScreen`, framework-agnostic). Factored out of the hand-ported `Support\Log.cs` copies; **both TSM (WinUI) and TP (WinForms) consume it** — the per-app diagnostics *dialog* stays per-app (different UI frameworks), only the engine is shared. No `Astronomy.*` deps; pure-managed → builds with `dotnet build`. **Open follow-up — `ObservationSession`:** factor the dialogs' START/CAP/END/CANCEL orchestration into the library so each app's dialog (TSM's `DiagnosticsWindow`, TP's `DiagnosticsDialog` — both renamed from "Observation" 2026-06-11; the USER_OBS log protocol keeps its name) — which still hand-rolls that wiring + the hide/grab/reshow + the singleton/terminator bookkeeping — thins to just its controls + bounds/hide-show callbacks. Deferred at extraction; **now that two live consumers each duplicate the wiring, this is the next consolidation.**

`Library\PCL\` is the vendored PixInsight Class Library (~10 GB of source + prebuilt `.lib` outputs). It's gitignored — re-extract from `PCL\PCL-master.zip` (snapshot pinned 2025-02-22 per `PCL InterOp.md`) on a fresh clone.

## Build / test / benchmark

The mixed C++/C# solution requires `MSBuild.exe` (from VS2026; build 18.x) for full builds — `dotnet build Astronomy.sln` cannot drive the C++ vcxproj. Locate it via `vswhere.exe` or run from a Developer Command Prompt. Earlier VS lines (2022 / build 17.x) likely also work but the project is developed and verified against VS2026.

```bash
# Full mixed build (native + managed) — REQUIRED to produce Astronomy.PCL.Native.dll
msbuild Astronomy.sln /p:Configuration=Debug /p:Platform=x64 /m

# Run xUnit tests (all). The native DLL must already be built; --no-build skips a redundant rebuild.
dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj -c Debug -p:Platform=x64 --no-build

# Run a single test by fully-qualified name or substring filter
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AltitudeCurveTests"
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "TargetDefault_IsM31"

# Pure-managed Astronomy.Core build (no SLN, no C++ tooling needed)
dotnet build Astronomy.Core/Astronomy.Core.csproj

# Astronomy.XISF / Astronomy.NINA build + tests — pure-managed (no native dep in Phase A)
dotnet build Astronomy.XISF/Astronomy.XISF.csproj
dotnet test Astronomy.XISF.Tests/Astronomy.XISF.Tests.csproj
dotnet build Astronomy.NINA/Astronomy.NINA.csproj
dotnet test Astronomy.NINA.Tests/Astronomy.NINA.Tests.csproj

# Smoke-test against a real image library (env var gates the live scan)
TP_SMOKE_IMAGE_LIBRARY='E:\Photography\Astro Photography\Processing' \
    dotnet test Astronomy.NINA.Tests --filter "FullyQualifiedName~ImageLibrarySmokeTest" --logger "console;verbosity=detailed"

# Run BenchmarkDotNet — Release is mandatory (Debug numbers are misleading).
# Benchmarks live in Astronomy.Core.Benchmarks (pure-managed, references only Astronomy.Core),
# so plain dotnet drives them — no SLN / MSBuild / native build needed.
dotnet run -c Release --project Astronomy.Core.Benchmarks -- --filter *
dotnet run -c Release --project Astronomy.Core.Benchmarks -- --list tree
```

`BDN0001` (BenchmarkDotNet's "build in Release" warning) is suppressed in `Astronomy.Core.Benchmarks.csproj` so a Debug build stays warning-free; the runtime check inside BenchmarkSwitcher still enforces Release for benchmark runs.

**Trap to avoid:** `dotnet build Astronomy.sln` silently produces a managed-only build (skipping the vcxproj) and any test that touches `Astronomy.PCL` then throws `DllNotFoundException`. Always use `msbuild` for the SLN.

## Architectural conventions

These are baked into the public API and must be respected when adding code:

- **Hemisphere convention.** `Latitude`, `Longitude`, and `Declination` are stored as **non-negative magnitudes**, with direction in companion bool flags (`Location.North` / `Location.West` / `Target.North`). Constructors normalize: a negative magnitude is flipped positive and the corresponding flag is inverted (sign takes precedence over the supplied flag). Internal math reconstructs signed values just before feeding geometry.
- **RA is decimal hours in `[0, 24)`**, not degrees. Declination is decimal degrees.
- **Altitude is unrefracted** — degrees above the mathematical horizon, never adjusted for atmospheric refraction. Don't introduce a refraction term without coordinating across `AltAz`, `TargetGeometry`, `RiseSet`, etc.
- **Azimuth** is degrees from North, clockwise, in `[0, 360)`.
- **DateTime kinds.** Public Library APIs that take a `DateTime` parameter route it through `Astronomy.Core.Time.TimeKindGuard.AsUtc(DateTime)` (internal): `Local`/`Unspecified` are treated as local and converted via the machine zone; `Utc` is a no-op. `NightWindow` outputs are `Kind=Utc`. The recommended consumer-facing carrier for an observation moment + its zone is `Astronomy.Core.Time.ObservationMoment` (record struct, `(Utc, Zone)`) — consumers can build it via `FromLocal(local, zone)` or `Now(zone)` and pass the `.Utc` field to math helpers. One deliberate exception to the lenient guard: `LunarAge.DaysAt` throws on non-Utc rather than converting, because it sits inside `BestSession.MoonClearIntersect`'s tight loop where a stray non-Utc kind would silently corrupt the whole-night sweep.
- **Immutability + `With(...)`.** `Location` and `Target` are immutable; mutations produce new instances via a `With` method that takes optional parameters.
- **D/M/S accessors are computed on read**, never stored. Don't add stored DMS fields — they would drift.
- **`Location` is the site, not the session.** Carries geography (lat/lon/elev/TZ), atmospheric (Bortle/ExtinctionK), and terrain (`LocalHorizon: IHorizonProfile`). It does NOT carry an observation moment, a target floor, or a minimum duration — those are per-session inputs that consumers thread explicitly. As of 2026-05-18 (Phase 2 of the Location refactor), the previously `[Obsolete]` `Horizon`/`Duration` scalars and the `DateTime` field have been removed entirely. Scheduling helpers (`BestSession.For` / `ResolveCandidates` / `SessionSolvers`, `NightCalculator.ComputeNight(location, utc)`, `AltAzCalculator.At(target, location, utc)`) take all per-session inputs as explicit parameters.

## Thread safety

`Astronomy.Core` is **thread-safe by construction** since the CoordinateSharp removal in commit `2249834`. Verified: zero mutable static fields, zero static caches / dictionaries / `Lazy<T>` / singletons; all public APIs take parameters with no hidden state. All inputs are immutable POCOs (`Target`, `Location`, `NightWindow`, `MoonAvoidanceProfile`); all returns are immutable POCOs, small structs (`AltAz`, `RiseSet`), or `IReadOnlyList<T>`. **Consumers may call any helper from any thread without external synchronization.**

Two caveats limit that promise:

1. **Caller-supplied delegates are the consumer's responsibility.** Methods that take `Func<...>` parameters — most notably `IntegratedQuality.OverSession(..., Func<double, double> altitudeQuality)` and `IntegratedQuality.HalvesAroundMidpoint(...)`, plus any future delegate-taking helper — invoke the delegate from whatever thread called the method. If a consumer passes a closure that mutates captured state (e.g. `int count = 0; alt => { count++; return ...; }`), thread safety of that closure is on the consumer. Pure / referentially transparent functions are always safe.

2. **`Astronomy.PCL` is NOT part of this contract.** PCL is a separate assembly (`Astronomy.PCL.csproj` + `Astronomy.PCL.Native.vcxproj`) wrapping the vendored PixInsight Class Library. `Astronomy.Core` never references it. The underlying PCL C++ library historically isn't safe to call across threads — assume single-threaded access until PCL's own concurrency story is documented per-API.

`NightCache` is a per-`Location` amortization helper: build once per Graph click, hand to multiple targets so each target's year work doesn't re-derive the same 365-day `NightWindow` series. The instance is immutable after construction; concurrent readers are safe. Several caches can be built in parallel for different locations.

## Code organization (high-level)

- `AltAz.cs` / `TargetGeometry.cs` — topocentric coordinate primitives (the core "where is this target right now" math).
- `Time/` — `JulianDate`, `SiderealTime` (GMST/LST).
- `Locations/`, `Targets/` — the immutable observer + target value types described above.
- `Horizons/` — `IHorizonProfile` and three implementations (`Scalar`, `Polyline`, `ObstructionTable`) for flat-vs-azimuth-varying horizon math.
- `Night/` — twilight calculation, `NightWindow` (a single night's astronomical/nautical/civil dusk/dawn), `NightCache` (year-of-nights for one location).
- `Session/` — higher-level analysis built on the primitives: `AltitudeCurve` (uniform-grid sampling via linear LST advance — ~2.6x faster than per-sample `AltAzCalculator.At`; returns `IReadOnlyList<AltAzSample>` with `AltDegGeometric` + `AltDegApparent` + `AzDeg` per sample), `RiseSet`, `TransitTime`, `VisibilityWindows`, `CoarseVisibility`, `IntegratedQuality`, `BestSession`, `TargetOrdering`.
- `Moon/` — `MoonSeparation`; `MoonEphemeris.Sample(location, startUtc, step, count)` returns `IReadOnlyList<MoonSample>` with topocentric AltAz (geometric + apparent) + `DistanceKm` + `AgeDays` + `PhaseAngleDeg` + `IlluminatedFrac` per minute. Pure-function per-night sampling primitive consumed by TargetPlanner's `MoonEphemerisEntry` cache axis; designed for the planned IntervalScheduler Plugin (ISP) cache shape.

## PCL local build

`Library\PCL\` is the vendored Pleiades PixInsight Class Library, locally pruned to **Windows-only** — the macOS and Linux build trees were largely stripped (some Makefile stubs survive under `Library\PCL\src\modules\...`, but the actual source trees were removed). The canonical pinned snapshot is `Library\PCL\PCL-master.zip` (2025-02-22 per `PCL InterOp.md`). Re-extract on a fresh clone, or to discard local edits.

**Toolset.** All PCL projects (`PCL.vcxproj` + the six 3rd-party libs + `xisf.vcxproj`) are at `<PlatformToolset>v145</PlatformToolset>`, matching `Astronomy.PCL.Native`. The directory naming `vc17` under `src\3rdparty\*\windows\vc17\` and `src\modules\*\windows\vc17\` is historical PCL convention and was deliberately not renamed; only the main PCL solution moved from `src\pcl\windows\vc17\` → `src\pcl\windows\vc18\` to signal the VS2026 build flavor.

**Main solution.** `Library\PCL\src\pcl\windows\vc18\PCL.sln`. Builds PCL plus the six 3rd-party static libs into `Library\PCL\lib\x64\{Debug,Release}\*-pxi.lib` and the xisf utility into `Library\PCL\bin\x64\{Debug,Release}\xisf.exe`. Astronomy.PCL.Native consumes the seven `.lib` outputs from `lib\x64\$(Configuration)\`.

**Required environment variables** (consumed by PCL.vcxproj, the 3rd-party libs, and xisf.vcxproj — set as system env vars and reboot before opening the SLN; otherwise the C++ build fails to find headers and libraries):

- `PCLINCDIR` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\include`
- `PCLSRCDIR` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\src`
- `PCLLIBDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\lib\x64`
- `PCLBINDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\bin\x64`

**xisf utility.** PCL's CLI test app at `Library\PCL\src\utils\xisf\`. Statically links the same seven `.libs` Astronomy.PCL.Native does (`PCL-pxi`, `lz4-pxi`, `zlib-pxi`, `zstd-pxi`, `lcms-pxi`, `cminpack-pxi`, `RFC6234-pxi`, plus `Userenv`). Built by PCL.sln (which lists xisf.vcxproj) or independently via `xisf.sln` in the same directory; both produce the same `xisf.exe`. **xisf.exe must be invoked from its solution directory** so its relative-path arguments resolve. `Test_GetXisfKeywords.bat` is a developer-convenience smoke that runs `xisf.exe --read-fits-keywords` against `TestData\test.xisf`. Both `xisf.vcxproj` and `xisf.sln` are x64-only — Win32/x86 was dropped because the v140_xp toolset isn't installed under VS2026.

**Re-snapshot caveat.** Re-extracting `PCL-master.zip` clobbers all local edits — the `vc18` directory, the v143→v145 toolset bumps across PCL + 3rd-party + xisf, the Win32/x86 strip on xisf, anything else added under `Library\PCL\`. Plan to re-apply or keep a patch.

**Upstream Pleiades docs.** `Library\PCL\README.md` (PCL overview), `CODING_STYLE.md` (Pleiades C++ conventions), `COPYING.md` + `LICENSE.txt` (PCLL license) are unmodified upstream content; left alone deliberately so re-snapshot doesn't churn them.

## PCL interop

The hybrid architecture from `PCL InterOp.md` is **implemented** for the first surface (XISF read). Two projects:

- `Astronomy.PCL.Native` (C++ DLL, statically links PCL `.lib`s from `Library\PCL\lib\x64\`).
- `Astronomy.PCL` (managed P/Invoke wrapper, `net10.0` x64).

C ABI is the source of truth: `Astronomy.PCL.Native\include\Astronomy\PCL\XisfCApi.h`. Every export is wrapped in `ASTRONOMY_PCL_TRY` / `ASTRONOMY_PCL_CATCH` (in `src\Exception.h`) — this catches `pcl::Exception` and `std::exception` and translates to status codes; the `catch (...)` branch picks up SEH (access violations etc.) due to `<ExceptionHandling>Async</ExceptionHandling>`. Last-error message is thread-local (`src\LastError.cpp`); consumers retrieve via `AstronomyXisf_GetLastErrorMessage` (two-call idiom: query length, alloc, fetch).

**Watch-out: don't use `XISFReader::ReadImage(FImage&)` for non-float source images.** PCL's auto-converting read path appears to need PixInsight platform services that aren't available in a host process — UInt16 or UInt32 sources surface as an unrecognized SEH exception ("Unknown C++ exception" at `catch (...)`). The wrapper dispatches on `ImageOptions.bitsPerSample` / `ieeefpSampleFormat`, reads in the file's native type (`UInt16Image`, `FImage`, `DImage`, …), then converts to float32 in our own loop. See `XisfCApi.cpp:AstronomyXisf_ReadImageF32`.

Adding new PCL surfaces: bind on demand. Add the `extern "C"` export to `XisfCApi.h` and `Astronomy.PCL.Native.def`, implement in `XisfCApi.cpp` wrapped in the macro, mirror the `[DllImport]` in `Astronomy.PCL/Interop/NativeMethods.cs`, expose the C# surface in `Astronomy.PCL`. Test in `Astronomy.Core.Tests/Tests/PCL/`.
