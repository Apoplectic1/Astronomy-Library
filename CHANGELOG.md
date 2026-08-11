# CHANGELOG.md

**Charter.** Shipped history for the `Astronomy` library — **append-only, dated, newest first**. One
section per shipped unit: what landed and why it mattered. Read to answer "*when* did X land, and what
shape did it land in?" Forward-looking design and the short recently-shipped digest live in
`ROADMAP.md`; how the code works today lives in `ARCHITECTURE.md`. Git remains the commit-level
backstop — this is the human-legible layer above it.

**Entry format:** `## YYYY-MM-DD — <what landed>` (a month-only `YYYY-MM` is fine when the exact day
wasn't recorded). Newest first; add new entries directly below this charter, never at the bottom.

## 2026-08-11 — injectable clock: `IClock` + `SystemClock` (IS gap 3)

The third IS-motivated change (IS ROADMAP "AL gaps to close for IS" item 3; openspec change
`add-injectable-clock`), giving the IS dossier's injectable-clock decision its one AL home:
`Time/IClock` (`UtcNow`, always `DateTimeKind.Utc` — flows into the UTC-gated math without
conversion) + stateless `SystemClock.Instance`, and an `ObservationMoment.Now(zone, clock)`
overload so clock-driven consumers never touch the ambient path. Test fakes deliberately stay
consumer-side. The thread-safety census's ambient-clock exception widens from one sanctioned
site to two (`ObservationMoment.Now(zone)` + `SystemClock.UtcNow`). Same-day scope widening
(user directive): this is the **portfolio-wide single clock source**, not an IS/ISM-only seam —
existing app ambient reads (TP 0, XFM 0, TSM 2) migrate opportunistically, user-owned; the
standing convention lives in `CONSUMERS.md`. Verified: 533 Core tests green (3 new).

## 2026-08-11 — meridian primitives: `Meridian` + `MeridianSide` (IS gap 2)

The second IS-motivated change (IS ROADMAP "AL gaps to close for IS" item 2; openspec change
`add-meridian-primitives`), closing the dossier's `MeridianFlipTime` assignment. New
`Session/Meridian` — pure composition over `TransitTime`/`SiderealTime`, no new astronomy:
`HourAngleAt` (signed `[-12, +12)`), `SideAt` (**sky-side** `MeridianSide.East/West` — ASCOM
pier-side vocabulary deliberately kept out; the mapping and its inversion trap belong to the
mount adapter), `TransitsIn` (a >sidereal-day window yields multiple), `FlipTimeIn` (searches
transits from `session.Start − allowance`, so a pre-session transit whose flip lands in-session
is found; negative allowances legal), and `SplitAtFlip` (same-side pieces for the interval
solver). Two findings hardened during apply: transit-search seeds advance by one minute, not
one tick (LST jitter stutters a one-tick advance into ~14 repeats of the same transit), and
`SplitAtFlip` suppresses splits leaving a piece under 1 s (the ~0.1 ms transit-recomputation
jitter would otherwise emit micro-slivers on every replanning re-split). Consequence for the
interval algebra: the canonical-list contract relaxed from "merged" to **ordered + disjoint,
touching legal** (flip-split pieces touch by construction and are semantically distinct;
`Union` output alone stays fully merged) — `add-interval-algebra`'s unarchived spec delta
amended in place. Verified: 1061 tests green (15 new).

## 2026-08-11 — XML-doc neutrality sweep: the last 8 consumer-UI-terminology leaks fixed

The report-only axis from the 2026-07-24 audit finally ran (site list: `docs/2026-07-29-maintain-report.md`
§ *Code bug*, minus the two Ctrl+N sites that became Library surface). All 8 remaining `///` sites
reworded to caller-neutral language per `DOMAIN.md` § *Multi-consumer strategy*: `NightCache` (the
"Graph path"/"Graph click" framing, the dangling TP `AltitudeSeries`/`LocationsCacheEquivalent`
member refs, "Year / Sessions chart x-axis labels"), `ObserverInfo` ("chart-cache prepare loop" →
"a caller's cache-prepare loop"), `BestSession` (the chart's "Symmetric" semantics → "the caller's
symmetric-session mode"), `FramingCluster` ("the framing badge" → "a consumer's framing-disagreement
indicator"). Rode along per the maintain sweep's K12 booking: `DiagnosticsDialog`'s stale "three
delegates" remark corrected to four (drifted at the 2026-08-10 capture-delegate change). Doc-comments
only — no behavior change; Core/Catalog/Diagnostics.WinForms rebuild clean (CS1591 ratchet
enforcing on Core). A verification grep across all nine shipped assemblies finds zero remaining
consumer-UI vocabulary; the two deliberately-generic mentions ("consumers (chart UIs, schedulers)",
"below chart pixel resolution") stay, as cleared by the 2026-07-29 adjudication. ROADMAP item closed.

## 2026-08-11 — ARCHITECTURE split: per-module files + the 26 held promotions land

The split booked by the 2026-07-29 maintain sweep, executed as adjudicated **option (a)**: subsystem
mechanics moved verbatim to one file per buildable module under `docs/architecture/` (solution, core,
xisf, diagnostics, catalog, nina, contracts-tests, pcl) and the root `ARCHITECTURE.md` became a thin
index (~4 KB) whose rows resolve existing "§ *Astronomy.X*" references. With healthy per-module homes,
**all 26 promotions held by the 2026-08-11 maintain sweep landed** (`docs/2026-08-11-maintain-report.md`
§ *Held graduates* records each claim/target/disposition): six Core conventions (WcsOrientation +
its spec route-in, efficiency bar, rename-vs-reshape, rotation nullable-vs-not, signed-dec
double-flip, the three-layer test strategy), the full XISF Tier-3 mechanics block (module shape,
producer interop, alias-at-parse, write-side sha-1 asymmetry, rewriter textual-edit contract, zstd
encode-side level), seven Catalog rules (epoch translation table, schema field rules, sentinel
metadata, directory-is-identity, both-planes pairing, sentinel asymmetry + `TemplateSentinel`,
write-back-groups-every-plan), the DiagnosticsHotkey + OK-time capture contract, four PCL interop
rules (same-thread last-error, no in-place write, buffer granularity, no-ownership-transfer C ABI),
and the bench scope rule. Also folded in: `Time/UtcInterval` + `Intervals` added to Core's folder map
(the same-day interval-algebra commit `c3a6b89` carried no doc updates), and the thread-safety audit
recorded as run-three-times. Router + `docs/README.md` updated (docs/architecture/ is reference tier,
not journal); the ROADMAP split item closed.

## 2026-08-11 — scanner aborts on a coordinate-less unit (the (0,0) fallback is gone)

The silent contract violation flagged by the same day's maintain sweep: a unit none of whose readable
frames carried RA/DEC was placed at RA 0h / Dec 0° — a real sky position flowing into `TargetResolver`
coordinate matching and the reconcile join ("caller can sanity-check downstream"; no caller did).
`ConsensusCoordinates` now **aborts the scan with `InvalidDataException`** naming the directory, the
frame count, and the missing keyword(s); `Median`'s empty-list `return 0.0` fallback is removed
outright per the fail-fast rule. The `SkippedFiles` tolerance is thereby narrowed to what it was
always documented to be — **per-file** parse failures only; a frame silent on one coordinate still
never aborts (consensus needs one carrier per coordinate, pinned by the new partial-coordinate test).
Verified: the two new scanner tests + full Catalog suite (286) + contract bench green, and a gated
smoke scan of the real image library passes the new gate — no real target trips it.

New `DiagnosticsHotkey.Register(owner, contextProvider)` in `Astronomy.Diagnostics.WinForms`:
installs an application-level `IMessageFilter` that opens (or focuses) `DiagnosticsDialog` on
Ctrl+N. Hoisted from TP (its `Support/DiagnosticsKeyFilter.cs`, deleted) so hotkey **routing** is
uniform by construction across WinForms consumers, the same move TSM made dialog-side with
`AppDialog`: a filter sees every thread `WM_KEYDOWN` before dispatch, covering MenuStrip menu
mode and modal WinForms dialogs — the two states a consumer-side `ProcessCmdKey` override misses
(TP obs f231; XFM carried exactly that override and had the latent gap). Native modal loops
(common dialogs, `MessageBox`) remain out of reach by Win32 design. Register-once contract —
a second call throws (fail-fast, matching `ObservationSession.Begin`). Consumers: TP + XFM swapped
same day; TSM (WinUI) keeps its accelerator + `AppDialog` hook, which is the same coverage there.

## 2026-08-11 — interval algebra: `UtcInterval` + `Intervals` ops; producers converge (BREAKING)

The first IS-motivated Library change (IS ROADMAP "AL gaps to close for IS" item 1; openspec change
`add-interval-algebra`). `Time/UtcInterval` — readonly record struct, UTC-only fail-fast ctor,
half-open `[Start, End)` — plus `Time/Intervals` (`Intersect` / `Union` / `Subtract` / `Clip` over
ordered-disjoint-merged lists, input-validated with a throw on violation). Generic half-open
subtraction covers all six relative positions of TS's `MaximumAltitudeClipper` reference pattern by
construction; named tests pin the correspondence. **BREAKING, value-identical:** every public
tuple-interval API converged on the shared type — `VisibilityWindows.For`,
`MoonSeparation.IntervalsAboveDeg`, `SunSeparation.IntervalsBelowDeg`,
`BestSession.ResolveCandidates` / `PlaceBest` / `PlaceCentered` (return now `UtcInterval?`), the
`SessionSolvers.*In` candidate parameters — same instants, new element type; sweep emission points
gained zero-length guards (no representation for an empty interval). This resolves the 2026-05-18
review's declined finding on the five tuple APIs the day its "defer to a v2 window" premise was
flagged stale, while its C2 sibling (don't unify the *sweeps*) stands. Verified: 1046 tests green
(36 new), TP / TSM / XFM rebuilt clean with zero edits (TP's `ChartCacheStore` consumption is
pass-through; `Start`/`End` property names carried over).

## 2026-08-11 — WinForms shell invoke-time capture: shipped and REVERTED same day

`371c204` made `DiagnosticsDialog.ShowOrFocus` (Astronomy.Diagnostics.WinForms) grab the owner
before the dialog first showed (the session's hide → grab → reshow choreography with the
never-shown dialog as the "hidden" state — synchronous at `settleDelayMs: 0`, so an open
MenuStrip dropdown survived into the shot; origin TP obs f231, TP-side hotkey rework `1b12b89`).
**Reverted the same day by user decision:** the portfolio contract is that all diagnostics
consumers (TSM, TP, XFM) behave identically on the hotkey, and the chosen uniform semantics is
**capture at OK time only** (plus the explicit Capture / Capture-in-5s buttons) — the WinUI shell
never had invoke capture, and the WinForms consumers should not lead the contract. Transient-UI
shots remain the delayed-capture workflow. Net code change across the pair of commits: none.

## 2026-08-10 — Diagnostics platform layering (`diagnostics-portable-core`)

Diagnostics restructured into a **four-assembly layered stack** (normative contract:
`openspec/specs/diagnostics-platform-layering/`), motivated by two coming consumers: the ISM
Android port (a `net10.0-windows` library is un-referenceable from an Android TFM — compile wall)
and IS as a WPF guest in NINA's process (where `Assembly.GetEntryAssembly()` stamps the *host's*
version). What landed:

- **`Astronomy.Diagnostics` → `net10.0` (TFM-neutral)**; `System.Drawing.Common` dropped. A
  platform API creeping into the core now fails the build — the neutrality is compiler-enforced.
- **New `Astronomy.Diagnostics.Windows`** (`net10.0-windows`): `ScreenCapture` moved here — the
  Win32 capture backend all Windows shells share.
- **BREAKING: `ObservationSession.Begin` requires the platform `capture` delegate** (the internal
  test seam, promoted). Windows callers pass `ScreenCapture.ToPng`; other platforms bring their own.
- **`AppLogIdentity.VersionAssembly`** (null = entry assembly): plugin-hosted consumers stamp
  their own `build=`, standalone apps unchanged.
- **New `Astronomy.Diagnostics.WinUI`** (`net10.0-windows10.0.19041.0` — the WinUI floor, not any
  app's SDK; `Microsoft.WindowsAppSDK` 2.3.1, lockstep recorded in `CONSUMERS.md`):
  `DiagnosticsWindow` ported from TSM and de-apped (icon → caller parameter, `UiTask` inlined) —
  TSM's port pending; ISM gets Ctrl+N free on day one. The AppDialog-layer graduation stays parked
  (`ROADMAP.md`) — inspection showed the dialog never depended on it.
- Tests: `Begin` wiring + `VersionAssembly` + contract-shaped `ScreenCapture` smoke coverage;
  `Contracts.Tests` re-wired the way consumers wire (assumption #25 wording updated). Coordinated
  consumer errand queued: TSM port + TP/TSM TFM unification at `10.0.26100.0`.

## 2026-08-07 — Surgical block rewrite (`block-rewriter`)

New **`XisfBlockRewriter.RewriteAsync(sourcePath, targetPath, codec[, zstdLevel])`**: re-store a
monolithic XISF's primary image block under a different codec (`BlockCodec.None` writes it
uncompressed and strips `compression`/`checksum`; a base family compresses via
`XisfBlockCompression` and records SHA-1) while preserving the XML header **byte-for-byte**
except the attributes the block change forces — the primary's `compression`/`checksum`, every
shifted attachment's `location`, and the signature's XML-length field. Header edits are textual
(never a re-serialization); attachments after the swapped block shift with preserved bytes;
layout honors a declared `XISF:BlockAlignmentSize`; the write is temp-file + atomic replace
(in-place when `targetPath == sourcePath`). A declared source checksum is verified before
re-encoding — a corrupt block fails the rewrite instead of being re-certified under a fresh
digest. Returns the written block's `BlockCompressionInfo` + offset/size + XML length so callers
holding cached geometry can refresh it. `XisfXmlLoader` gained an internal raw-text load;
`XisfImageReader`'s geometry/sample-format parsers are now internal-shared. Pinned by ten tests:
round-trips (fresh→zstd-19, zlib-no-checksum→zstd-19, compressed→None), byte-preservation,
trailing-attachment shift, alignment, corrupt-checksum refusal, temp-file hygiene.

## 2026-08-07 — Verify-only block integrity check (`checksum-verifier`)

New **`XisfChecksumVerifier.VerifyAsync`**: locate the primary image attachment, hash the stored
bytes, compare against the declared checksum — no decompression, no pixel allocation, strictly
cheaper than `XisfImageReader.ReadImageAsync` (whose read path verifies as a side effect).
Verification is a detection operation, so a digest disagreement returns a
`XisfChecksumVerdict.Mismatch` result (with declared-vs-computed detail) rather than throwing;
the three-way verdict (`Verified` / `NoChecksum` / `Mismatch`) keeps "can't confirm" distinct
from "confirmed bad". Structural violations (bad signature/XML, truncated attachment) still
throw. First consumer: XFM's Verify SHA browse checkbox (its ROADMAP #6). Reuses the reader's
location parser (now internal-shared); pinned by verified/mismatch/no-checksum/truncated tests
across zlib and zstd blocks.

## 2026-08-07 — `Compress` gains an optional zstd level (`zstd-level`)

`XisfBlockCompression.Compress(raw, itemSize, codec, level: null)`: the new optional parameter
sets zstd encoder effort (1–22; null keeps the level-1 interop default, so existing callers'
bytes are unchanged). Level affects encode cost and output size only — any zstd decoder reads
any level, so no read-side or attribute change. Non-zstd families take no level; passing one
throws rather than being silently ignored. Motivated by XFM's library benchmark (its
`docs/2026-08-07-compression-benchmark.md`): shuffled 16-bit frames compress ~11% smaller at
zstd-19 than zlib-SmallestSize, a win that only appears at the level-15+ strategy switch —
XFM's save path is the first caller to pass a level. Pinned by round-trip `[Theory]` (1/19/22,
token stays `zstd+sh`) and rejection tests.

## 2026-08-06 — Legacy XISF checksum aliases accepted on read (`checksum-alias`)

`BlockCompressionInfo.Parse` canonicalizes the non-hyphenated checksum algorithm tokens legacy
producers wrote (`sha1`/`sha256`/`sha512` → the spec's `sha-1`/`sha-256`/`sha-512`), so the read
path verifies them instead of rejecting the file. Found in the field the day the ASTAP solve path
shipped: an XFM Browse+solve pass over 2019-era SGP files failed all 106 with "Unrecognized XISF
checksum algorithm 'sha1'" before ASTAP ever ran — valid legacy input, not a contract violation.
Canonicalizing at the single parse site (rather than aliasing in `ComputeChecksumHex`) means
`ChecksumName` is always spec-form and a re-save rewrites the attribute to the spec token for free.
Pinned by NIST-vector `[Theory]` cases for all three aliases.

## 2026-08-06 — WinForms diagnostics satellite (`diagnostics-winforms-satellite`)

New **`Astronomy.Diagnostics.WinForms`**: per-framework dialog shells over the UI-free
`Astronomy.Diagnostics` core, so WinForms consumers share one implementation and WinUI consumers
never inherit the WinForms stack. Initial content: `DiagnosticsDialog` (the Ctrl+N observation
dialog) moved verbatim from TP — `public`, namespace + doc generalization, construction reordered
(controls before `ObservationSession.Begin`) to satisfy the nullable ratchet. `ScreenCapture` stays
in the core deliberately: `ObservationSession` invokes it for every consumer (TSM's WinUI Ctrl+N
included) and `System.Drawing.Common` is Windows-GDI+, not WinForms-specific. Consumers queued: TP
(deletes its local copy), XFM (first-time adoption). WinUI counterpart parked in ROADMAP (gated on
ISM; the real asset there is TSM's AppDialog behavior layer).

## 2026-08-06 — WCS orientation from a CD matrix (`wcs-position-angle`)

`Astronomy.Core.Astrometry.WcsOrientation.FromCdMatrix` — position angle (North-toward-East, [0,360)),
image-axis rotation, parity (`Flipped`, from the determinant sign), and per-axis pixel scales
(arcsec/px) out of a plate solution's `CD1_1..CD2_2`. NINA `WorldCoordinateSystem` as the strategy
reference, pinned by its three real-matrix test vectors + flip cases. Contract documents the domain of
validity (normal + single-mirrored images; a both-axes mirror is indistinguishable from a 180°
rotation — undetectable by construction) and the solver-offset boundary (solver-specific conventions,
e.g. ASTAP's 180°, stay in the calling wrapper — this is the generic form). First consumer queued:
XFM's ASTAP solve step. Core tests 478.

## 2026-08-06 — XISF Tier 3: full-codec block compression + image read (`xisf-codecs-and-image-read`)

`Astronomy.XISF` graduated from header-only. **`XisfBlockCompression`** is now the symmetric,
XISF-1.0-conformant block codec layer: encode *and* decode for zlib / lz4 / lz4hc / zstd, each ±
byte-shuffle (`Compress` gained a base-family codec parameter, zlib default preserving the old call
shape), all five spec checksum algorithms (sha-1/256/512, sha3-256/512) via `ComputeChecksumHex` /
`VerifyChecksum`, and fail-fast `BlockCompressionInfo.Parse` (malformed or sub-block attribute forms
for known codecs throw; unknown tokens still parse as `Other` for read-side detection but throw on
decode). **`XisfImageReader.ReadImageAsync`** is the Tier-3 read slice: locate the primary image's
attachment → bounds-check → verify declared checksum → decompress → geometry/sample-format-verified
pixel buffer (`XisfImageData`). Signature/XML handling extracted to a shared `XisfXmlLoader`; the
header path stays pixel-free and byte-identical. Codec wiring via managed-only `K4os.Compression.LZ4`
+ `ZstdSharp.Port` (NINA's `XISFData` as strategy reference only — tokens, levels L00_FAST/L06_HC/zstd-1,
checksum-over-stored-bytes all pinned by NINA-call-identical interop tests). Consumers queued: the
TSM-side ASTAP plate-solve pipeline (read) and XFM's `adopt-al-xisf-compression` (encode — retires its
vendored codec duplicate). XISF tests 91; full suite green.

## 2026-08-06 — Editable `project.name` + altitude-clause handling (v1.4.0)

*(Backfilled 2026-08-11 by the maintain sweep — this shipped unit had no CHANGELOG entry; derived
from commits `769837c`..`b648e3a`, all 2026-08-06, published as **v1.4.0**.)*

- **`project.name` joins `TsEditableSchema`** (Text field, `ba16dcd`) so a landed `minimumaltitude`
  write can also rewrite an existing name's altitude clause.
- **`minimumaltitude` Max clamps to 89.9, not 90** (`d0b97d9`): TS's `HorizonDefinition` asserts
  `< 90` at plan time, so a 90 write would pass the editor and abort TS planning. Schema notes now
  record TS-UI semantics — 0 = "Off" (`c1c3a90`) and the project-constraint choice lists (`698a5e2`).
- **`MosaicConvention.StripAltitudeClause`**: name matching tolerates a trailing altitude clause —
  the legacy `" - Above N"` form (`769837c`) and the short `" - N"` form with a required end-anchored
  dash so a plain name like "Abell 2218" is never mistaken for a clause (`60736de`).

## 2026-08-04 — Write-back credits by the capture-configuration pairing rule

`WriteBackPlanner` and `SingleTargetPlanner` no longer credit every frame in a plan's
`(target, filter, purpose, seconds)` bucket: an inventory row joins a plan's sum only when its
gain/offset/binning **pair with the plan's template** under the new shared predicate
**`Reconcile.CaptureConfigPairing`** (value equality after plan-side normalization; the camera-default
sentinel `-1` pairs with nothing). `ReconciliationProjection`'s cell key derives from the same
normalization, so a consumer's rendered separation and the stamped counts can never drift — the drift
this closes was field-observed (frames at a superseded gain still crediting a differently-configured
plan, which then never healed to 0). Non-pairing buckets surface as `UnplannedFrames` notes naming the
configuration; the surgical path holds a configuration miss as a `NoMatchingPlan` manual with context.
Also: `TsExposureTemplate` gained `ReadoutMode` (the reader now selects `readoutmode`; `-1` = TS's
use-camera-default sentinel) and `ReconciliationCell` gained `TemplateSentinel` — true when a plan's
template leaves any of gain/offset/readout-mode unspecified, the consumer's cue to badge it.
Consumer contract note updated in `CONSUMERS.md` (silent-wrong-result surface: a configuration mismatch
now writes 0 to a live plan — by design, disk is truth). Catalog tests 268.

## 2026-08-03 — Diagnostics ≠ Catalog boundary rationale graduated into ARCHITECTURE.md

One-sentence standing truth landed in § *Astronomy.Diagnostics* (user placement decision, TSM
2026-07-29 maintain sweep held graduate H1): shared observation tooling stays out of
`Astronomy.Catalog` on purpose — Catalog is a schema/build contract, not a grab-bag utility
library. The rationale predates Diagnostics itself (TSM archive review 2026-06-10 §4.4) and had
no home in any live doc until now.

## 2026-08-03 — TryInsertRows: guarded batch-atomic row creation; v1.2.0 published

`TargetSchedulerEditor` gained its second public write path, the row-creation sibling of
`TrySetField`: `TryInsertRows(IReadOnlyList<TsRowInsert>) → (InsertOutcome?, RefusalReason)` —
one all-or-none transaction over exposuretemplate / target / exposureplan payloads, built for a
consumer sync model that journals creations locally and replays them remotely (TSM's disk-row
adoption is the driving consumer). Same guard order as field edits (required columns / read-only
/ open sidecar), per-table required payload columns (`guid` always; `Id` forbidden — SQLite
autoincrement mints it), payload column names validated against the live schema (the injection
whitelist role), parent references (`projectid`/`targetid`/`exposureTemplateId`) accepted as
integer id **or guid** and resolved in-transaction — a guid may name a row inserted earlier in
the same batch — a plan insert clears the target's `filtercadenceitem` rows in-transaction and
refuses on hand-authored override-order rows (`HasOverrideOrder`), and every landed row is
read-back verified (`RowInsertResult`). 15 tests (`TargetSchedulerEditorInsertTests`); the
write-surface contract test now pins both paths. Published as **`v1.2.0`** (tagged source
snapshot; the DLLs ship inside TSM v1.2.0's installer, cut the same day).

## 2026-08-02 — AL published to GitHub (Astronomy-Library mirror), MinVer versioning

The library gained a public mirror — https://github.com/Apoplectic1/Astronomy-Library — on the
portfolio's shared distribution rules (new `RELEASING.md`: local repo is ground truth, `dev`
never pushes, every `main` push carries a `vX.Y.Z` tag, docs-only exception). A library has no
installer, so **publish = the push**: bare tags mark source snapshots, no Releases page, no
assets — the compiled DLLs continue to ship inside TP/TSM installers. New public `README.md`
documents the two-tier build (managed = self-sufficient `dotnet build`; native =
`Astronomy.PCL.Native` needs the PCL mirror — https://github.com/Apoplectic1/PCL, published
the same day — cloned nested at `Library\PCL\`). MinVer 7.0.0 adopted via
`Directory.Build.props` (`MinVerTagPrefix=v`, `alpha.0` prereleases): every managed assembly
now stamps a tag-derived version, giving consumer payloads AL provenance. The standing
PCL-binary obligation (notice + acknowledgment when any product ships
`Astronomy.PCL.Native.dll`) is recorded in `RELEASING.md` content rules. First public tags:
`v1.0.0` (pre-public history) + `v1.1.0` (publish commit). New capability spec:
`openspec/specs/github-distribution/`. Same-day revision: **MIT license adopted** (© Dan
Stark) covering the repo's own code, superseding the initial no-license posture — the PCL
license still governs the vendored material and the PCL-binary obligation stands.

## 2026-08-01 — the zero-warning ratchet extends to every shipped project (incl. the C++ wrapper)

Follow-on to the test-bench sweep below, same day: all seven shipped projects — `Astronomy.Core`,
`.Diagnostics`, `.XISF`, `.NINA`, `.Catalog`, `.PCL`, and `Astronomy.PCL.Native` — now build with
warnings-as-errors (`TreatWarningsAsErrors` on the six csprojs; `/WX` via `TreatWarningAsError` in both of
the vcxproj's `ClCompile` groups — wrapper TUs only, the vendored PCL libs are prebuilt). No code changes
were needed: forced non-incremental rebuilds of all seven were already clean, in **both Debug and Release**,
before the switch went on — so the ratchet locks in an existing state rather than papering over one.
`Astronomy.Core.Benchmarks` deliberately excluded (not shipped, not test). Verified under enforcement:
all seven rebuild clean in both configs; Catalog 240 + Core 472 suites re-run green end-to-end.

## 2026-08-01 — all six test projects at zero warnings, and warnings are now build breaks

Two passes, same day. First `Astronomy.Catalog.Tests`: every ct-accepting call now passes
`TestContext.Current.CancellationToken` (`Resolve`, `BuildAsync`, `ScanAsync`/`ScanUnitsAsync`, the TS
reader methods, `XisfHeaderReader.ReadAsync`, two `File.WriteAllTextAsync`), silencing all 45 `xUnit1051`
sites the xUnit v3 analyzers had accumulated — one of which the same-day epoch fix had added, matching the
then-idiom. Then the same sweep across the rest: `Contracts.Tests` (2 sites), `XISF.Tests` (12 sites + a
`CS8632` pair — `string?` annotations in the project's deliberate `Nullable`-disable context, dropped to
match convention); `Core.Tests`, `Diagnostics.Tests`, `NINA.Tests` were verified clean-rebuild-clean
already. Beyond hygiene, the analyzer's point is real: without the token a cancelled test run waits out the
full scan/resolve. Deliberate exceptions untouched (the cancellation test's own `cts.Token`).

**Enforcement:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on all six test csprojs — the
warnings accumulated precisely because they were warnings; the next one is a build break. Verified: clean
non-incremental rebuilds of all six (Core.Tests via VS MSBuild x64, per the mixed-graph rule) with zero
warnings, and every suite green — Catalog 240, Contracts 61 (+6 skip), Core 472 (+1 skip), Diagnostics 15,
NINA 45, XISF 51.

## 2026-08-01 — TS epoch codes are translated, not cast (JNOW/B1950 were silently swapped)

Found by the IS repo's docs audit (round 4). TS persists NINA's `Epoch` enum ints — JNOW = 0,
B1950 = 1, J2000 = 2, J2050 = 3 — while the catalog's `epoch` lookup orders B1950 = 0, JNow = 1,
J2000 = 2: only J2000 agrees. `TargetResolver.SafeEpoch` raw-cast the TS int into the catalog enum, so
any non-J2000 TS target would have imported with JNow and B1950 silently swapped. Latent — every real
target is J2000 — but exactly the silent cross-tree mis-map the harden rule exists to prevent.

Fix: `SafeEpoch` is now an explicit translation table (0 → `JNow`, 1 → `B1950`, 2 → `J2000`; anything
else — including NINA's unmodeled J2050 — coerces to `J2000` and is still reported via
`FlagIfSuspect`, unchanged). Doc comments on `Schema/Enums.cs → Epoch` and `TsTarget.EpochCode` now
state both conventions and the translate-never-cast rule at the TS boundary. A new `[Theory]` in
`TargetResolverTests` pins all four codes. Catalog 240 tests green (+4); Contracts 61 green.

## 2026-07-29 — a framing off the plan's footprint is priced, not just flagged

The framing badge said a row's frames point somewhere the plan did not ask for; it never said *how far*
off. The **overlap fraction** prices it: the share of a framing's own footprint landing inside the
footprint the plan asked for.

- **`Astronomy.XISF`** — `XisfHeaderReader` now reads the mandatory `<Image geometry>` attribute (it
  harvested only `<FITSKeyword>` before); `XisfHeader` exposes the pixel dimensions and derives the
  angular field from them + `FOCALLEN` + `XPIXSZ`. **No binning factor anywhere** — `XPIXSZ` is already
  binning-adjusted, so multiplying would double the field for the 15.8% of the library shot at bin 2.
- **`Astronomy.Core`** — new `FieldFootprint`: rotated-rectangle overlap on a tangent plane
  (Sutherland-Hodgman clip + shoelace, exact for convex). RA offsets scale by `cos(dec)`; without it
  M81's east-west offsets inflate 2.8×.
- **`Astronomy.Catalog`** — `FramingCluster` carries its footprint and a spans-sensors flag (dominant
  sensor when frames span two, never a blend); `ReconciliationProjection` computes the fraction against a
  plan rectangle built from the measured framing's **own** sensor, so no neighbouring framing's sensor
  shape can move the number.

A number means off-footprint for *any* reason — wrong rotation, wrong pointing, or both. A serving
framing prices nothing while it stays above `OnFootprintFraction` (0.95); a disagreeing one always
reports whatever its overlap, so a badge is never left pointing at an empty cell. Measured over the
18,650-frame library: 52 of 60 serving framings sit at ≥99.5%, and the 11 strays span 57.5–91.9%.
Crediting is untouched — write-back stays the boolean `ServesPlanRotation`; a partially overlapping frame
is not a fractional frame. Catalog 236 / XISF 51 / Core 472 / NINA 45 / Contracts 61.

## 2026-07-29 — write-back credits only frames whose framing serves the plan

User decision 2026-07-29, from the Tulip confusion: TS said `acquired=32` while **zero** captured frames
matched the re-framed 160° plan — and TS would then under-schedule the re-shoot. The rotation-participation
predicate now lives once as **`FramingCluster.ServesPlanRotation`** with three consumers — the projection's
pairing/disagree cue, `WriteBackPlanner`'s disk sum, `SingleTargetPlanner`'s cell routing — so the badge and
the stamped counts can never tell different stories.

- **Bulk:** non-serving inventory rows don't sum, so a re-framed plan stamps its true progress, possibly 0.
- **Surgical:** a withheld cell emits a `FramingMismatch` note naming the frames, the framing, and the
  rotation they fail.
- Mechanical/unknown framing and rotation-less targets always credit; flips agree fold-180.

Verified pure-planner against the live library: Tulip H900 32→0, Barnard L600 credits exactly the serving
28 of 479, Jellyfish all-0 (TS plans a third framing), Bear Claw's 180° flip still credits 128. Catalog 222.

## 2026-07-29 — framing (fold-180 rotation + cluster centroid) keys the disk plane

A **framing** is a (field-center, sky-rotation) pair — the thing that decides whether frames share a
footprint and can integrate. New `FramingCluster` + `FramingClusterer` (expression partition
sky/mechanical/unknown → fold-180 gap clustering at 5° → field-center single-linkage at 0.5°), run per unit
**before** the aggregate grouping, so a pier flip merges (identical footprint) while a translated stray at
an unchanged angle separates — the centroid guard as ordering, not a special rule. The cluster joins the
aggregate identity beside gain/offset/binning/camera; `TargetReport` publishes per-cluster centroids;
`inventory_filter` carries `framing_ordinal` + `rotation_expression` + `rotation_fold_deg` (in the PK — two
clusters can share a fold angle and differ only by center, the M97 shape).

`ReconciliationProjection` pairs a plan with the disk framing whose sky rotation agrees fold-180 with the
target's rotation; mechanical/unknown framings and rotation-less targets skip the term (the camera
precedent — rotation only as expressed by both planes). **Mechanical is never converted to sky:** the zero
point drifts 19–35° across remounts, measured exactly on the multi-framing targets. `FramingDisagrees`
marks disk cells whose sky rotation fails the plan. Calibrated by the 2026-07-29 spike over the live
library (18,650 frames): real framings ≥9° apart, jitter ≤0.2°, every true flip's centroids within 0.12°.
219 tests green.

## 2026-07-27 — non-sidereal targets never enter the library scan

A comet's coordinates change from night to night, so no sidereal plan can describe it — the live TS
database holds zero comet targets — and every frame of one is acquired by hand at the telescope. Reading
them produced grid rows that reconcile against nothing. Their capture trees also break the
`Captures/<Camera>/<Filter>` convention, nesting date-named session folders (`"2024-10-18 - Track Comet"`)
where a filter directory belongs, so the scan was publishing those session names as filter codes.

Excluded **at the directory walk**, like the calibration tree, rather than filtered afterwards by each
consumer; the guard sits in `ScanTargetAsync` so both entry points honour it from one place. The predicate
is named for the reason rather than the spelling — `IsNonSiderealDirectory`, where the `"Comet "` prefix is
today's *evidence* for non-siderealness, not the fact itself. **Its trailing space is load-bearing:**
`"Cometary Globule CG4"` and `"NGC 2261 - Comet Nebula"` are sidereal and still scanned, both pinned by
test. Also adds the first tests for the calibration skip, which had existed since the scanner was written
with no coverage at all. Removes one target of 84 and 254 of 18,904 light frames. Catalog 199.

## 2026-07-27 — the capture configuration keys the disk plane

Gain, offset and binning join the reconciliation key, and camera becomes a disk-side label. Frames
differing in any of these do not combine into one integration, so they must not fold into one bucket
claiming they do — the 2026 broadband move from gain 53 to gain 0, and the offset-50 frames sitting in
every filter, were both invisible until now. A plan and a disk aggregate share a cell (and so render as one
`Both` row) only when they agree on every dimension **both planes express**. Camera is deliberately *not*
in the key: a TS profile cannot name a camera, so keying on it would split cells against a plan that could
never match them — it rides disk-side cells as a label and never prevents pairing.

**Offset is now read exactly as recorded.** XFM does not divide — its per-camera "ADU Offset divided by N"
comment describes the camera's scale rather than an operation it performed — so the recorded value is
already in the scale TS's templates use. `XisfHeader.OffsetNormalized` was rescaling it a second time,
reporting 2 for a Z183 frame recording 10: a number comparable to neither plane. It had one production
caller and is **removed**. `FilterAggregate` carries one camera (the capture directory, authoritative and
known before a file is opened) plus a flag for frames recording a different one, so every configuration
field is uniform within an aggregate rather than a mode over mixed frames. `WriteBackPlanner` is unchanged
and now pinned by test: its key stays the coarser (target, filter, purpose, seconds) and it sums inventory
rows, so finer disk buckets still total the same acquired. Measured over the live library (18,904 light
frames): disk buckets 471 → 542 (+15%); camera, telescope and binning each add zero; GAIN and OFFSET are
present on 18,904 of 18,904 frames. Library 186 / XISF 30 / NINA 45 / Contracts 61.

## 2026-07-26 — the TS read and the resolve observe a `CancellationToken`

`TargetSchedulerReader.ReadPlanData` (and the five `Read*` methods) and `TargetResolver.Resolve` now take
an optional `CancellationToken` and actually observe it. Previously a caller could only cancel the
*scheduling* of the work it wrapped — once a resolve started, the token was inert — while
`ImageLibraryScanner.ScanAsync` had always threaded one, making the gap an asymmetry rather than a
deliberate choice.

- **Reader:** the token forwards to the private `Query<T>`, the one choke point every read runs through, and
  is checked **per row** — a caller that cancels mid-read gets the connection released promptly instead of at
  end-of-table. Threading it in one place is what makes the whole reader cancellable.
- **Resolver:** checked at each phase boundary (projects · templates · disk working set · spatial anchor ·
  canonical build · plans) and **per TS target** inside the anchoring pass, which is the one super-linear
  loop (each target scans the disk working set). Cancellation throws; no partial graph is returned.
- **`CatalogBuilder.BuildAsync` gained the fix for free** — it already accepted a token and had the identical
  gap between accepting one and honouring it. Its doc now reads "observed throughout" rather than "during the
  disk scan".

All parameters are optional, so no call site had to change. Guarded by `Resolve_ObservesCancellation`.
172 tests.

## 2026-07-26 — template `ditherevery` gains its `-1` defer-to-project sentinel

TS's template `DitherEvery = -1` means "use the project's dither setting" (`DitherManager` tests `>= 0`;
verified against the released v5.10.3.0 tag). The editable schema had it as a plain `Min`-0 `Whole`, so
consumers displayed the raw `-1` and could never write it back once edited. Now a sentinel: `-1` labelled
"project default", the same shape as the camera-default sentinels.

## 2026-07-24 — `ObservationSession`: the observation-dialog orchestration moves library-side

Closes the ROADMAP's *Open: ObservationSession* item (deferred at the 2026-06-11 Diagnostics
extraction). **TSM's `DiagnosticsWindow` was the model**: its behaviors — the 450 ms
hide-settle (the 2026-06-10 translucent-ghost observation now lives in the XML docs), the 5 s
delayed capture, the `·` status wording — become the shared defaults. The new
`Astronomy.Diagnostics.ObservationSession` owns everything app-agnostic: 4-char id minting,
USER_OBS START/CAP/END/CANCEL sequencing with a structurally-guaranteed single idempotent
terminator, capture counting, the guarded context-provider call, status text, and the
hide → settle → grab → reshow choreography over three app-supplied delegates (owner bounds /
hide / show — the library references neither WinForms nor WinUI). Awaits keep the caller's
`SynchronizationContext`; post-`Begin` members never throw (each delegate individually guarded).
`ObservationCapture(Path, StatusText)` is the per-shot result.

New **`Astronomy.Diagnostics.Tests`** project (the assembly's first): 15 tests over the internal
fake-capture seam — delegate ordering, terminator idempotency, mid-countdown cancel,
busy-overlap no-ops, END-line escaping — with `Log.Init(RootOverride)` into a temp dir.
Contract bench gains **#25** (`ObservationSessionContractTests`, 4 pins, no `Log.Init` per the
bench's one-Log-toucher rule). Contracts: 61 passing. Consumers adopt in their own repos next
(TSM then TP; TP also picks up the delayed-capture button and TSM's Enter-key semantics —
decided this session).

## 2026-07-24 — editor write path hardened: `TrySetField` is the only public write

Closes the audit's ungated-writer finding (breaking, no shim — zero consumer callers, grep-verified
across TP/TSM). Deleted `SetTargetActive` (+ its `TargetEditResult` record) and the three sugar
wrappers (`SetTargetField`/`SetPlanField`/`SetProjectField`); `SetField` became the **internal**
engine behind `TrySetField` (unit tests keep access via the existing `InternalsVisibleTo`).
`target.active` edits go through `TrySetField(Target, key, "active", 0|1)` — the field was already
in `TsEditableSchema`, so the redundant bespoke path bought nothing and skipped every gate. New
`EditorWriteSurfaceContractTests` reflection-pins the surface (no public `Set*`; `TrySetField`
exists), so a future ungated writer trips the bench. Contract #9's caveat retired the same day it
was written. Verified: Catalog 171, Contracts 57 (+1), constellation DRC green (TP + TSM compile).

## 2026-07-24 — contract bench: NINA gap closed (`NamedSitePersistenceContractTests`)

`Astronomy.Contracts.Tests` now references `Astronomy.NINA` (pure-managed, stays
`dotnet`-testable) and pins assumption #2's testable half: the `NamedSite` /
`PlanningPreferencesDto` **serialized JSON property names** (the cross-app sites-file format —
a rename recompiles TP cleanly but silently zeroes values loading existing files) + lossless
round-trip + null-`Preferences` survival. The "minutes" *meaning* stays a naming convention;
the #2 register entry graduated out of `NotCleanlyTestableAssumptions.cs` (6 registered skips
remain). Bench: 56 passing.

## 2026-07-24 — docs audit #2: 65-flag remediation across the reference set

Six-round, two-model fan-out audit (placement + currency, judged separately) over the full
reference set. ~55 doc fixes applied in one pass, highlights: Catalog section restructured by
folder (+ `ReconciliationProjection`, which had gone undocumented); the contract bench's
covered-or-registered rule surfaced into `CONSUMERS.md` (heading no longer says "candidates");
TSM's Core pinout corrected to what it actually calls; PCL interop docs gained the two
undocumented host-process survival mechanisms (`SilentLogHandler`, `PclRuntimeInit`) and the
GetLastErrorMessage no-macro exception; VERIFICATION's benchmark conclusions extracted to
`docs/2026-05-12-fma-benchmark-findings.md`; `-p:Platform=x64` added to the pure-managed test
invocations (AnyCPU dual-output-tree trap); TFM history backfilled below; DOMAIN softened to admit
portfolio glossary app-names in XML remarks (decided; UI terminology stays banned). Report-only
handoffs: five ungated public `TargetSchedulerEditor.Set*` writers (code concern, noted in
CONSUMERS #9), publish-scrub re-scope (circular `TestLocations.PennsPark` remedy + coordinate-only
files), new `ROADMAP.md` § *Open: contract bench — NINA gap*, parent-umbrella `scheduler.db` naming.

## 2026-07-24 — K-S Δmag moon gate replaces the Lorentzian (`MoonLimitProfile`)

The placement primitives' moon gate is now physics, not curve-fitting: **accept a minute iff the
K-S-predicted sky brightness at the target is within `ToleranceMag` of the moonless baseline.**
`Δmag = ln(1 + bMoon/(bDark + bTwilight))/0.92104`, computed by the new decomposed
`SkyBrightness.KsMoonDeltaMag` (KsAt's three nL components evaluated once; `KsAt` itself untouched
— the #4 golden survives). `BestSession.MoonClearIntersect` keeps its seam, 1-minute walk and
boundary interpolation, now on `ToleranceMag − Δmag(t)`. The gate refraction-lifts moon altitude
internally (Saemundsson — the Sky-chart convention), which **closes the refraction-asymmetry item**
(~34′/~2 min disagreement between chart and gate); `ObserveAt` stays geometric (#3 unchanged).
Site inputs derive from `Location` (`Bortle.DefaultZenithMag` + `ScaleK`); the profile is minimal:
`Enabled` / `ToleranceMag` / `CenterNm`. **No bandwidth field** — the bandwidth scale cancels
exactly in the Δ (pinned by new assumption **#24**); per-filter moon policy lives in the tolerance.

**Deleted with no shim** (sole consumer, coordinated migration): `MoonAvoidance`
(`LorentzianRequiredSep`/`RequiredSepWithRelax`/`IsRejected`) and `MoonAvoidanceProfile`
(SeparationDeg/WidthDays/Relax* — the Relax zone was already off in every shipped TP filter
default). `DaysInLunarCycle` moved home to `LunarAge.SynodicMonthDays`. Profile type swapped on
`BestSession.For`/`ResolveCandidates` and four `SessionSolvers` entry points. TP breaks until its
Phase-2 migration (directed by an in-repo note, committed to TP with this change).

**Calibration (real `KsAt`, Bortle 5, geometry grid over four lunar ages).** The Lorentzian was
never an iso-quality contour: its implied sky-brightness tolerance wobbled ~10–30× across the
cycle — crescent boundaries at Δmag ≈ 0.1, full-moon boundaries at ≈ 1.6 (NB, sky ×4.4) and
≈ 1.7–2.0 (BB, ≈ 5–6× integration cost). Shipped defaults are the **cycle medians**:
**Narrowband 1.0** (sky ×2.5), **Broadband 0.30** (sky ×1.32) — deliberately stricter than the
old rule near full moon (moderately NB, strongly BB) and more permissive at half/crescent.
Per-filter override lives in TP's filter editor. Anchors pinned by calibration tests (NB full-moon
boundary ≈ 1.63 > shipped 1.0; NB gibbous/median ≈ 1.0; BB half-moon ≈ 0.32). *(A first-cut
design table mislabeled the NB gibbous cluster as full-moon — a truncated calibration readout; the
pinning test caught the error before it shipped.)*

Tests: 24 Lorentzian tests deleted; 8 reworked; 16 gate tests added (Δ properties, twilight
dilution, site derivation, tolerance monotonicity, huge-tolerance ≡ moon-blind, calibration pins,
#24 contract test + registry entry). Change artifacts: `openspec/changes/archive/2026-07-24-ks-dmag-moon-gate/`.

## 2026-07-24 — PCL linked closure restored to upstream AVX2 (4800U floor)

Follow-through on the audit's AVX-512 finding, decided once the hardware facts were in. The local
vc16→vc17/vc18 PCL port had escalated `EnableEnhancedInstructionSet` to `AdvancedVectorExtensions512`
across the whole linked closure (PCL + all six 3rd-party libs + xisf) while upstream's own projects —
and the `__PCL_AVX2` source macros — are AVX2. The result was real, not theoretical: **5,635 ungated
AVX-512 instructions in the Release `Astronomy.PCL.Native.dll`** (Debug: zero, since `/Od` doesn't
auto-vectorize — meaning no Debug test anywhere could ever have caught it). The dev 7950X (Zen 4)
executes them fine, but the imaging machine — the 4800U where IS will run — is **Zen 2: no AVX-512
at all**, so the first IS call into the wrapper would have died with an illegal-instruction fault,
likely masked as "Unknown C++ exception" by the wrapper's `catch (...)`.

Restored `AdvancedVectorExtensions2` in the 8 sln-visible projects (= upstream's configuration; the
~38 unused module projects were left alone), rebuilt all seven `*-pxi.lib` in both configs + xisf +
the wrapper DLL. **Verified: `dumpbin` now finds 0 AVX-512 instructions in the Release DLL**; the 7
PCL round-trip tests pass at Release and the full 478-test Core suite passes at Debug. Note the PCL
tree is gitignored, so this lives only in the local tree — but a re-snapshot is born correct
(upstream is already AVX2); the doc caveat now warns against re-escalating during a future toolset
pass, which is how the drift happened the first time.

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
  did the public-surface retention decision (which was the only library-level record of the IS plan,
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

## 2026-07-23 — resolver panel scoping, write-back absence-stamping, alias-fold removed

Four commits that together settled how panels anchor and what write-back is allowed to leave alone.

- **Panel-scope match tolerance (`e07efd9`).** Panel spacing is a fraction of a field, so the 0.5° target
  tolerance let an unrelated framing filed under a mosaic directory arrive as a flagged coordinate match
  ~0.2° from a planned panel. `ResolveOptions` gains `PanelMatchToleranceDegrees` (default 0.1°, generous
  for planned-vs-plate-solved offsets, which are arcminutes). Beyond it the TS panel stays planned and the
  directory stays actual-only — no claim, no name-mismatch flag.
- **…then gated on name alignment (`c27ac7d`, Clamshell P5 regression).** The flat 0.1° radius falsely
  unmatched a *real* panel whose planned-vs-plate-solved drift exceeds it (token-aligned `Panel 5of6` ↔
  `… P5`), and the new absence-stamping then zeroed its counts — while the radius exists precisely to
  reject nearby *unrelated* framings. Distance cannot separate the two cases (both live in the 0.1–0.5°
  band); the token can. Rule in `TargetResolver` + `SingleTargetPlanner`: an **aligned** panel directory
  anchors within the full `MatchToleranceDegrees` (name confirms identity, drift absorbed); an
  **unaligned** claim is limited to `PanelMatchToleranceDegrees` (coordinates alone are trusted only at
  plate-solve precision).
- **Disk truth covers absence (`c6e83b2`).** `WriteBackPlanner` groups *every* existing plan, not just
  `Both`-resolved targets': a plan on a target with no disk match stamps to 0 like any other unmet spec, so
  stray counts or a diverged accepted/acquired pair on a not-yet-shot target heal instead of persisting
  forever (the consumer's diff layer keeps clean 0/0 plans as no-ops). Identity-flagged cells still route to
  manual; `IgnoredMissing` now counts disk-only targets only — write-back never creates plans.
- **Alias-fold mechanism removed (`306f6fd`, breaking).** The alias escape (≥2 TS names each exactly
  matching a disk identity facet auto-resolve unflagged, write-back writing disk counts to every member)
  let the unintentional M27/Dumbell twin pass as benign for weeks. The hand-edit doctrine abolishes the
  category: every multi-claim is now `DuplicateTsTarget` (`IsAliasName` deleted), `CatalogBuildReport`
  drops the alias fields, and `WriteBackPlanner`'s exemption branch dies — ex-alias multi-plan cells hold
  as `ManualGroup(DuplicateFold)`, never auto-written. Single-target naming freedom is unaffected.
  `CatalogBuildReport`'s ctor loses a parameter; the sole consumer (TSM) updated in lockstep. 171 tests.

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

`CONSUMERS.md` stakes out the Library's **de-facto public contract** — the "pinned pinout" derived from grep-verified real usage: who consumes the Library and how (only **TP + TSM**, by `ProjectReference`/source), the surface each depends on, 18 semantic assumptions (contract-test candidates), fragility flags, and a design-review decision on the large dead/speculative public surface (**keep, don't prune** — it's API ahead of its planned consumers: the IS plugin, the TSM write-back action, XFM's Library migration; not cruft).

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
disk). A surgical single-target path (`SingleTargetPlanner` + `ImageLibraryScanner.ScanUnitsAsync`, driven by `tsm
writeback --target`) updates one target — **per panel for a mosaic** — without a catalog rebuild. Driven from TSM
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
62 not-started), 8221/30088 frames done; the TSM host prints the summary. 42 Catalog tests pass.

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
IntervalScheduler (IS) cache can consume the same surface
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

The XISF reading primitives moved from `Astronomy.NINA/Xisf/` into a dedicated `Astronomy.XISF` library (7th and 8th buildable projects added: `Astronomy.XISF` + `Astronomy.XISF.Tests`). Rationale: the XISF file format is NINA-independent (PixInsight defines it); separating the reader from the planning layer makes it sharable across XFM, TP, IS, and the user's other apps without dragging the planning model.

What landed:

- `Astronomy.XISF/XisfHeader.cs` — typed FITS-keyword accessors carrying value + comment per keyword (subset of `XisfFileManager/Keyword/KeywordList.cs`'s ~50+ accessors — only the ones currently consumed by the scanner are ported; rest stay in XFM for now and migrate when consumers need them).
- `Astronomy.XISF/XisfHeaderReader.cs` — header-only XISF parser; `XDocument.Parse()` on the embedded XML section. Pure managed, no native dep.
- `Astronomy.XISF.Tests` — 26 unit tests with synthetic XISF fixtures.

`Astronomy.NINA` now ProjectReferences `Astronomy.XISF`; the scanner (`Astronomy.NINA/Xisf/ImageLibraryScanner.cs` — since moved to `Astronomy.Catalog/Scan/`, 2026-06) consumes XisfHeader via `using Astronomy.XISF;`. AL.NINA tests unchanged (61 still pass); XISF-specific tests moved to the new test project (26 there).

**Why not NINA's own XISF code?** NINA.Image.FileFormat.XISF is coupled to `IImageData` / `IImageDataFactory` / `NINA.Profile.FileSaveInfo` / WPF, forces a full pixel decode on every read (`XISF.Load()` has no header-only path), and exposes FITS keywords as a weak `TryGetFITSProperty(key, out value)` dictionary. The user's existing XFM approach (XDocument + strongly-typed accessors, header-only by design) is the better fit for shared consumption across non-plugin apps.

*(Tier 1 was the shipped scope. The forward scope has moved to `ROADMAP.md`
§ **Open: Astronomy.XISF Tiers 2 & 4** — forward-looking work doesn't belong in the shipped history.)*

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

*(The forward roadmap that closed this entry — Phases C and D — moved to `ROADMAP.md`
§ **Open: Astronomy.NINA Phases C-D** on 2026-07-24; forward-looking work doesn't belong in the
shipped history.)*

**Resolved (2026-05-18):** `Astronomy.XISF` extraction landed (see the *2026-05-18 — Astronomy.XISF: Tier 1 extraction* entry above). Tier 1 (header-only read) shipped; Tiers 2 & 4 are tracked in `ROADMAP.md` § *Open: Astronomy.XISF Tiers 2 & 4*.


## 2026-05-11 — TFM narrowed to `net10.0-windows` (backfilled 2026-07-24)

The VS2026 settings review formalized Windows-only intent: `Astronomy.Core` (and `Astronomy.PCL`
alongside it, commit `e7ae75c`) narrowed from `net10.0` to `net10.0-windows`. A prior uncommitted
`net10.0-windows10.0.26100.1` pin was over-tight and broke the build via NETSDK1229 / MSB4184 —
the OS-version-less TFM is deliberate.

## 2026-05-04 — netstandard2.0 floor lifted to net10.0 (backfilled 2026-07-24)

Commit `b834f52`: once NINA's upstream migration confirmed every consumer was on modern .NET, the
original netstandard2.0 floor was lifted to net10.0 portfolio-wide (`Astronomy.PCL` came along from
its original net8.0, commit `c7eeff9` — it had been net8.0 only because VS2026 `MSBuild.exe` had a
defect resolving `DllImportAttribute` for netstandard2.0 projects).
