# Astronomy Library — Roadmap

**Charter.** Forward-looking design for the `Astronomy` library — *where the library is going*, plus a
three-line digest of what just landed. How current modules work lives in `ARCHITECTURE.md`; the full
shipped history lives in `CHANGELOG.md` (this file never accumulates it). (The PCL wrapper's design
records — the interop decision + the parked wrapper-extension plan — are archived; see § PCL design docs below.)

## PCL design docs (archived)

The PCL wrapper is a deep but **settled / parked** subsystem; its design records live in `archive/`, off the live reference set:
- **Interop decision** — *why* PCL is wrapped via a native DLL + P/Invoke (Option 3 / Hybrid, not C++/CLI): `archive/PCL-InterOp.md`.
- **Wrapper-extension plan** — *what PCL surface to wrap next* (parked discussion-stage; resume cold when ready): `archive/PCL-WrapperRoadmap.md`.

## Recently shipped — digest

Latest three only. **Full shipped history: [`CHANGELOG.md`](CHANGELOG.md)** (append-only, dated, newest first).

- **2026-08-11** — Contract assumptions **#26–#29** pinned (+ **#15** reconciled): the "not yet numbered" facts (Catalog cancellation-throws, write-back join key + pairing credit, XISF codec semantics, `WcsOrientation` conventions) each gained a citing bench test in `Astronomy.Contracts.Tests` and a number in `CONSUMERS.md`; the holding section is retired and the ROADMAP item closed. Same day, #15's stale "does not validate" registry note was adjudicated against the fail-fast ctor (code is the contract) and #15 moved from registry to covered — bench 61 → 83 passing, skips 6 → 5.
- **2026-08-11** — XML-doc neutrality sweep: the last 8 consumer-UI-terminology sites in `///` docs reworded caller-neutral (`NightCache`, `ObserverInfo`, `BestSession`, `FramingCluster`) + `DiagnosticsDialog`'s stale "three delegates" → four. Zero consumer-UI vocabulary remains across the nine shipped assemblies; ROADMAP item closed.
- **2026-08-11** — `ARCHITECTURE.md` split executed (option a, per-module files): mechanics now live in `docs/architecture/<module>.md` with the root file demoted to the index, and **all 26 promotions held by the same day's maintain sweep landed in the new homes** (Tier-3 XISF mechanics, the epoch translation table, the OK-time capture contract, four PCL interop rules, six Core conventions, seven Catalog rules, the bench scope rule).

## Open: `WcsOrientation.FramingAngleDegrees` — queued for the second orientation consumer

Decided 2026-08-07 (XFM ROADMAP follow-up #9, where the full rationale lives): the PA ≡ PA+180
framing-equivalence fold becomes a **naming choice, not consumer math** — `WcsOrientation` gains
`FramingAngleDegrees` (folded [0,180); "PA and PA+180 frame identical sky on a rectangular
sensor") beside the existing `PositionAngleDegrees` (true 0–360), so consumers pick a value whose
name states its semantics instead of remembering to fold. No consumer folds by hand; on-disk
`OBJCTROT` stays true 0–360 (FITS convention — folding at the writer was rejected: it hands
third-party readers a framing angle labeled as a PA). **Build when the second consumer lands**
(TSM's `°(M)` rescan framings work) — and that consumer must read orientation *through this type*,
not raw `OBJCTROT`; the named-property protection only covers AL-path readers. XFM's format-time
0.1° rounding dance stays XFM-side (display quantization, not domain math).

## Open: parked PCL wrapper-extension plan — premise needs re-checking

The plan itself is `archive/PCL-WrapperRoadmap.md` (captured 2026-04-28, parked at discussion stage).
**Audit 2026-07-24 found its Phase A premise overtaken.** Phase A proposes exposing XISF header /
FITS-keyword metadata to C# by extending the native wrapper with new C ABI exports
(`ExtractXisfHeader`, `ReadFitsHeaderKeywords`, property enumeration). But `Astronomy.XISF` — created
2026-05-18, *after* that capture — already ships a pure-managed, header-only FITS-keyword reader
(`XisfHeaderReader`, `XDocument.Parse`, no native dep), chosen precisely to avoid native coupling and
forced pixel decode for metadata-only reads.

Before any Phase A work resumes, decide one of: **(a)** fold the FITS-keyword needs into
`Astronomy.XISF` and drop Phase A, or **(b)** justify the native path explicitly for what
`Astronomy.XISF` genuinely does *not* cover — non-FITS PCL `Variant` properties being the obvious
candidate. Phases B+ (pixel/image operations) are unaffected.

## Open: Astronomy.XISF Tiers 2 & 4

Captured 2026-05-18 with the Tier 1 extraction; **added when a real consumer needs them — no eager
design.** (Moved here from `CHANGELOG.md` on 2026-07-24: forward scope belongs in the roadmap, and
`Astronomy.XISF.csproj` already pointed here.)

- **Tier 2** — metadata write-back. Modify image metadata in place, preserving the image-attachment block. **Design direction (2026-08-06): property-first.** PixInsight is migrating from FITS keywords toward typed, namespaced XISF properties; NINA already writes both. Tier 2 should model XISF properties as the primary surface with FITS keywords as a compatibility projection — not a generalized keyword bag — so AL absorbs PixInsight's evolution without consumer rewrites. Target consumer: replacing XFM's two-stage custom keyword pipeline (high-level programmatic interface over a low-level name/value/comment writer — the high level is the migration choke point); also a future TSM-side grade-state write. *(The ASTAP plate-solve write-back home was decided 2026-08-06: XFM's existing writer — it does NOT wait on Tier 2.)*
- **Tier 3** — **SHIPPED 2026-08-06** (`xisf-codecs-and-image-read`): symmetric block-codec layer (zlib/lz4/lz4hc/zstd ±shuffle, all five spec checksum algorithms) + `XisfImageReader.ReadImageAsync` (locate attachment → verify checksum → decompress → verified pixel buffer + geometry/sample metadata). NINA's `XISFData` used as strategy reference only; codecs wired via managed-only `K4os.Compression.LZ4` + `ZstdSharp.Port`. Consumers: XFM's encode-side adoption (`adopt-al-xisf-compression`) **landed at its v2.4.0** — vendored codec duplicate retired; the TSM-side ASTAP pipeline (read) is still genuinely queued. **Cheap hardening step, unadopted** (the change's archived open question): codec interop is proven only against fixtures encoded with NINA's exact calls (same package, levels, attribute strings) — reading a genuine NINA- or PixInsight-written compressed field file (a user-supplied small crop) can land any time, no design work.
- **Tier 4** — full image write. Image data composition + compression + checksum (SHA-256). Required for XFM's writes and any future image-save pipeline.

When XFM eventually migrates to Astronomy.XISF as its sole reader, the additional `KeywordList` accessors (FocalLength, Camera, EGAIN, MasterFrame metadata, weight keywords, etc.) port over alongside Tier 2.

## Open: AppDialog-layer graduation — parked until ISM

Rescoped 2026-08-10: the diagnostics dialog itself **shipped** as `Astronomy.Diagnostics.WinUI`
(`diagnostics-portable-core` — code inspection showed TSM's `DiagnosticsWindow` never actually
depended on the `AppDialog` layer, so the 2026-08-06 parking rationale applied only to the larger
prize). What stays parked is that larger prize: TSM's `AppDialog` behavior layer (drag, Ctrl+N
wiring, lone-button centering) as a general WinUI dialog substrate. Revisit when ISM (the second
WinUI app) actually needs it — with two real consumers, TSM's implementation as the proven
reference and ISM's requirements as the generalization test. An `Astronomy.WinUI`-shaped design
exercise, not a diagnostics errand.

## Open: Diagnostics platform satellites — build with their consumers

The 2026-08-10 layering (TFM-neutral core + platform capture backends + per-framework shells —
`openspec/specs/diagnostics-platform-layering/`) leaves the future legs deliberately unbuilt: a
`.Wpf` shell only if IS (NINA plugin, WPF host) wants the *interactive* observation dialog rather
than programmatic `Log` + capture at scheduler decision points (likely sufficient); an `.Android`
capture backend when the ISM Android port picks its UI stack (MediaProjection needs a runtime
permission prompt — capture-as-delegate absorbs that). The core needs no changes for either.

## Open: Astronomy.NINA Phases C-D

Captured with the Phase A/B work (2026-05-18); neither phase has started. *(Moved here 2026-07-24 from
`ARCHITECTURE.md` § *Astronomy.NINA*, same reason as above.)*

- **Phase C** — TargetPlanner migrates from `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target`; the image library becomes a new TP target source; the Sky chart surfaces per-target Filter (color tint + badge + per-target K-S filter bandwidth).
- **Phase D** — `InputTargetAdapter` (bidirectional `Astronomy.NINA.Target ↔ NINA.InputTarget`); unblocks future NINA-sequence-JSON export from TP. Phase D is what introduces the `NINA.Plugin` NuGet dependency — `Astronomy.NINA` deliberately has **no NINA assembly dependency** until then.

## Open: public-surface retention — API ahead of its consumers

**Decision (2026-06-28, reaffirmed 2026-07-24): keep the unused public surface — do not prune.**
A large fraction of the public API has no external caller today; the inventory lives in `CONSUMERS.md`
§ *Dead / speculative public surface*. Much of it is for the **planned IntervalScheduler plugin (IS —
not yet started)** and XFM's planned Library migration. The TSM write-back action was once on that list
and shipped 2026-07-06 consumed as-built, which validated the call: this is *API ahead of its
consumers*, not dead generality. A smaller public surface is still better in principle, but pruning
here would just be rebuilt when IS lands.

**Revisit a block only if it ends up with no planned consumer.** *(Moved here 2026-07-24 from
`CONSUMERS.md`: this is a forward-looking commitment, and it was the only library-level record of the
IS plan — invisible to anyone following the router's "forward-looking → ROADMAP" rule.)*

**Standing counter-case (2026-05-27, `FilterKind` deletion):** per-instance metadata no production
code branches on is **deleted, not retained** — `FilterKind` was stored on every `Filter`, read only
by tests, and TP's own filter type never had it. The retention rule protects *callable API ahead of a
planned consumer*, not state that rides on every instance and can drift or lie.

## Open: SIMD / FMA deep dive

Captured 2026-05-12. The FMA hygiene pass landed in `b83a0d8` (Meeus +
TargetGeometry + SkyBrightness, 1.5-4.2% on Sun / Simpson paths, noise
on the transcendental-dominated moon path). The user wants to follow
up with a proper SIMD / vectorization investigation when time allows.

Full field notes — toolchain answers, runtime knobs, performance model,
microbench design lessons, the HotPathBenchmarks before/after table,
and four open directions ranked by impact — live in
**[archive/2026-06-21-simd-investigation.md](archive/2026-06-21-simd-investigation.md)**
(archived — conclusions graduated into `docs/2026-05-12-fma-benchmark-findings.md`).

The four open directions in summary:

1. **Specialized non-`params` `Horner` overloads.** Eliminates the
   `double[]` allocation that shows up in `Sun_AltAzAt` (144 B/call)
   and `BestSession_For_MoonBlind` (168 B/call). Low risk, modest win.
2. **`Vector<double>` SIMD on `MoonPosition.ApparentEcliptic` 60-term
   table loops.** Biggest perf upside in the portfolio; needs a
   vectorised sin/cos (hand-rolled polynomial approximation or lookup
   table) since .NET doesn't auto-vectorise scalar `Math.Sin`/`Cos`.
   **Payoff rose when the K-S Δmag gate shipped (2026-07-24):** the gate's
   per-minute cost is dominated by the moon-position evaluation
   (`MoonSeparation.ObserveAt`, ~1,425 ns — `KsAt` adds ~5.6 ns, the sun
   call ~147 ns), and `BestSession.MoonClearIntersect` now walks it per
   minute, so this moved off the microbench and onto the placement hot
   path (cost breakdown:
   `openspec/changes/archive/2026-07-24-ks-dmag-moon-gate/proposal.md` § Why).
3. **Estrin's-scheme polynomial parallelisation.** Drop-in alternative
   to Horner for longer chains; modest win on 4-term polynomials, grows
   logarithmically with length.
4. **Explicit `System.Runtime.Intrinsics.X86.{Fma,Avx2,Avx512F}`
   intrinsics.** Reserve for cases `Vector<T>` doesn't express. Niche.
5. **Target-independent per-night moon-ephemeris grid** (~5 µs/sample,
   amortized across targets) — the structural, non-SIMD alternative for
   the same hot path direction 2 attacks. Deferred 2026-07-24
   (`ks-dmag-moon-gate` § D3) only because the per-target loop was
   already paid and the gate's delta was ~10%; revisit if a consumer's
   per-target parallel sweep profiles hot.

Not gating any active work — recorded here so the investigation isn't
re-derived next time.

## Open: Library-review residuals (2026-05-18)

The 2026-05-18 library review and its re-check both fully closed — every actionable item landed (full record archived at `archive/2026-05-18-library-review*.md`). These are the only items that remained genuinely open after closure, lifted here so they don't get lost in the archive:

- **F5.7 Phase 3 — NINA-as-oracle parity.** The parity baseline currently freezes the Library's *own* post-CoordinateSharp output as a self-snapshot (catches drift, but not an independent-implementation check). Promoting it to "Library matches NINA within tolerance" needs a small `tools/NinaParityExtract` exe referencing `NINA.Astrometry` (with `NOVAS31.dll` co-located), calling `AstroUtil` directly to dodge `IProfileService`, emitting `ParityFixtures.BaselineSnapshot` initializers for a NINA-sourced sibling of the existing `ParityFixtures.Baselines` dictionary. ~30–60 min, native-DLL co-location the likeliest stumble. Lower-fidelity alternative: NOAA/USNO web baselines for the 9 fixtures. Full integration scope in the archived follow-ups doc.
- **Standing do-not-"fix" warning** *(retitled 2026-08-11 — was "Docstring drift — resolved", a closed item narrating its own closure; the commits stay recoverable via the archived recheck)*: the review's deliberately-left residuals — single-value hemisphere extensions and the `360.985647` Meeus citation literal — stay as-is; do not "fix" them.
- **The review's deliberately-declined findings are a roster of live do-not-"fix" decisions** (from `archive/2026-05-18-library-review-followups.md` § *Intentional non-actions* + `archive/2026-05-18-library-review.md` § *Counterpoints*, all the kind a later reviewer re-raises). **C2:** do *not* unify `MoonSeparation.IntervalsAboveDeg` with `BestSession.MoonClearIntersect` behind a shared generic `IntervalSweep` — the predicates differ in observation arity and the type gymnastics outweigh the readability win. **D1:** `TargetGeometry.HourAngleAtAltitude`'s `(latDeg, decDeg, altDeg)` parameter order differing from its siblings' `(haHours, latDeg, decDeg)` is intentional, governed by the "input drives the signature" rule. **E2:** `XisfFile`'s `Dispose`/finalizer bodies stay duplicated rather than collapsing into a `CloseNative()` (cosmetic only). **`MoonPosition`'s 60-term tables stay `private static readonly int[]`** — JIT-friendly indexing; `ImmutableArray<int>` adds an indirection the hot loop doesn't need and `ReadOnlySpan<int>` can't be a static field — squarely in the path of SIMD direction 2 below, so a future SIMD session must not "modernize" them first. **`MeeusUtility.HorizonDipDeg` stays in the Meeus namespace** (the citation surface belongs together). **The `(DateTime Start, DateTime End)` tuple element type across five public session APIs** — *resolved 2026-08-11, same day its "defer to a v2-API window" premise was flagged stale*: the interval-algebra change (`c3a6b89`, BREAKING) replaced the tuple with `Time.UtcInterval` across the producers, exactly the evolve-in-place move the stale premise had deferred. *(The fourth non-action, C4 — `LunarAge.DaysAt` throwing as the lone exception to a lenient rule — is obsolete: the 2026-07-24 UTC gate made throwing library-wide.)*

## Publish to GitHub — CLOSED 2026-08-02 (executed; one residual re-opened below)

The 2026-05-08 publish plan (scope Options A/B/C, prep checklist, TP downstream analysis) was
**executed 2026-08-02** as `publish-astronomy-library`: whole Library public at
github.com/Apoplectic1/Astronomy-Library with dev staying local (effectively Option B's scope on
Option C's workflow), MIT license, MinVer tag-derived versions, history published whole after a
288-file audit. Mechanics now live in `RELEASING.md`; the normative contract is
`openspec/specs/github-distribution/`. The checklist's **CI and NuGet** steps were explicit
Non-Goals of that change — **declined, not deferred**. The old section's full text stays recoverable
in git history (last present at the 2026-08-11 maintain commit's parent).

## Open: personal data in the public mirror

The one prep-checklist step that never executed — the **personal-data scrub** — is now a *live
exposure*, not prep: `git ls-tree origin/main` confirms `Astronomy.Core.Tests` publishes Penns Park
lat/lon `40.282835`/`74.997369` today (`Tests/Astrometry/ParityFixtures.cs` named test cases,
`LocationTests.cs`, `TestLocations.cs`, `AstroUtilMoonTests.cs`, `SessionSolversTests.cs` among ~40
"Penns Park" lines across 17 files spanning **three** test projects), and `ROADMAP.md` /
`VERIFICATION.md` carry the coordinates and absolute `E:\` personal paths onto the mirror as well.
Two unresolved scope traps from the 2026-07-24 audit carry forward: **(a)** a name-grep misses
coordinate-only files (`LocationTests.cs` has the numbers with zero name mentions — 19 coordinate
lines across 4 files); **(b)** the "move into `TestLocations.PennsPark`" remedy is circular — that
fixture itself hardcodes name + coordinates. Re-scope before executing; history publishes whole, so
a real scrub of *committed history* means `git filter-repo` (a rewrite + force-push decision), while
a forward-only scrub just stops the bleeding at the next tag.

## Open: K-S unphysical extinction-overdrive at low altitudes (urban regime)

Captured 2026-05-24 from TP visual testing. **Mechanism, worked example, and the interim
consumer policy (null-gate K-S display below ~10° target altitude at urban sites) live in the
`SkyBrightness.cs` class remarks** — alongside the near-moon (separation < ~10°) and
narrow-airglow-overlap regime caveats. Short version: the `k·(X−1)` extinction term grows
linearly with airmass, so at high-k sites below ~10° K-S predicts a sky *darker than zenith* —
K-S 1991 doesn't model urban in-scatter. TP applies the interim gate on its Sky chart (see TP
ROADMAP § *Future-flagged TP-side work* for the removal condition).

**Real fix:** adopt Garstang 1986 / Falchi 2016 framework that models
artificial-light scattering INTO the line of sight from off-axis city
sources. Requires per-site inputs the Library doesn't currently carry
(city positions, brightnesses, distances, azimuths) and substantially
more compute. Not a v2 lift — more like "TP/Library becomes a research
tool" lift. On-ramp: pull VIIRS satellite data + model the largest
city near each site + run a simplified single-scatter calc per-azimuth.

**Scope note (2026-07-24):** since the K-S Δmag moon gate shipped, this regime feeds a
*placement gate*, not just a chart. The overdrive dims the predicted low-altitude sky →
*under*-states Δmag → the gate is too permissive below ~10° target altitude at high-k
sites. Consumers' existing low-altitude floors (target floor / the interim K-S display gate)
cover the practical range; recorded in the moon-brightness-gate spec as a known limit.
