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

- **2026-08-11** — `DiagnosticsHotkey.Register` (Astronomy.Diagnostics.WinForms): shared app-level Ctrl+N message filter, hoisted from TP — WinForms consumers (TP, XFM) get menu-mode + modal-dialog hotkey coverage by construction; register-once, throws on double wiring. Same day: invoke-time capture shipped and reverted (user decision) — the uniform cross-consumer contract is **capture at OK time only**; transient-UI shots stay on the delayed-capture workflow.
- **2026-08-10** — Diagnostics platform layering (`diagnostics-portable-core`): core retargeted to TFM-neutral `net10.0` (Android/Linux-referenceable; platform APIs now fail the build), `ScreenCapture` extracted to new `Astronomy.Diagnostics.Windows`, `ObservationSession.Begin` takes the platform `capture` delegate, `AppLogIdentity.VersionAssembly` fixes the plugin-host `build=` stamp (IS-in-NINA), and the WinUI Ctrl+N shell ported from TSM as new `Astronomy.Diagnostics.WinUI` (WindowsAppSDK lockstep → `CONSUMERS.md`). TSM/TP consumer window pending.
- **2026-08-07** — `XisfBlockRewriter.RewriteAsync`: surgical re-store of a monolithic XISF's primary block under a new codec (or `None`), XML header byte-preserved except `compression`/`checksum`/`location` + length field, temp + atomic replace, declared checksums verified before re-encoding. Consumers pick codec and target — first callers are XFM's browse hygiene and its solver temp-input path.

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

## Open: split `ARCHITECTURE.md` — it crossed the size where one file still helps

The 2026-07-29 maintain sweep grew it 38.4 → 48.9 KB (+27%), and three module sections now carry 38 of
those 49 KB: **Astronomy.Core 14.0 KB, Astronomy.Catalog 13.7 KB, Astronomy.PCL 10.5 KB** (the other five
total ~10 KB). Nothing in it is off-charter — it is one section per buildable module exactly as its
charter says, which is *why* the fix is a structural split rather than a trim. It passed the
promote-into-it test at 38 KB at the start of that sweep; it would not pass it at the start of the next
one, and Catalog grows with every framing change.

Options: (a) per-module files (`docs/architecture/<module>.md`) with `ARCHITECTURE.md` demoted to the
module index — cleanest, most churn; (b) extract only the two heavyweights (Core, Catalog), leaving the
small modules inline — least churn, asymmetric; (c) split Core's three subsections (conventions /
thread-safety / code-organization) out as the API-conventions doc they effectively already are. Run it as
its own adjudicated job **before** the next maintain sweep promotes into these sections, and land held
promotions in the new homes.

## Open: consumer UI terminology has leaked into public XML docs

`DOMAIN.md` § *Multi-consumer strategy* bans consumer **UI terminology** from the public surface and its
`///` docs (app names like TP/TSM are fine — chart names, control names and per-app feature vocabulary are
not). The 2026-07-24 docs audit recorded this axis as *report-only* and the sweep never ran, so ~9 sites
still carry it — TP chart names and a TP member name in `NightCache`, "chart-cache prepare loop" in
`ObserverInfo`, the chart's "Symmetric" semantics in `BestSession`, and a consumer keybinding (`Ctrl+N`)
in both `Log` and `ScreenCapture`. Full site list, verified line numbers, and suggested neutral wording:
**`docs/2026-07-29-maintain-report.md`** § *Code bug*. Doc-only change to the library's XML comments; the
same leak class as the "Optimal-chart series" catch in `CoarseVisibility.cs`.

## Open: pin two unnumbered contract facts

Two behaviours consumers already depend on are documented in `CONSUMERS.md` § *Contract facts not yet
numbered* but not pinned as numbered assumptions, because numbering them requires a bench test or a
`NotCleanlyTestableAssumptions.cs` registry entry (the covered-or-registered rule), which the docs sweep
that found them could not make: **(a)** Catalog cancellation throws and never returns a partial
graph/report — compiler-invisible if it regresses, since every token parameter is optional (today only
`Resolve_ObservesCancellation` in `Astronomy.Catalog.Tests` guards it); **(b)** write-back's four-part
join key `(target, filter, purpose, whole-second exposure)` — a silent-wrong-result surface, since a
duration mismatch writes `DiskCount = 0` to a live TS plan. Give each a bench test (preferred) or a
registry entry, then promote both to numbered assumptions.

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
- **Tier 3** — **SHIPPED 2026-08-06** (`xisf-codecs-and-image-read`): symmetric block-codec layer (zlib/lz4/lz4hc/zstd ±shuffle, all five spec checksum algorithms) + `XisfImageReader.ReadImageAsync` (locate attachment → verify checksum → decompress → verified pixel buffer + geometry/sample metadata). NINA's `XISFData` used as strategy reference only; codecs wired via managed-only `K4os.Compression.LZ4` + `ZstdSharp.Port`. Consumers queued: TSM-side ASTAP pipeline (read), XFM `adopt-al-xisf-compression` (encode — retires XFM's vendored codec duplicate).
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

Not gating any active work — recorded here so the investigation isn't
re-derived next time.

## Open: Library-review residuals (2026-05-18)

The 2026-05-18 library review and its re-check both fully closed — every actionable item landed (full record archived at `archive/2026-05-18-library-review*.md`). These are the only items that remained genuinely open after closure, lifted here so they don't get lost in the archive:

- **F5.7 Phase 3 — NINA-as-oracle parity.** The parity baseline currently freezes the Library's *own* post-CoordinateSharp output as a self-snapshot (catches drift, but not an independent-implementation check). Promoting it to "Library matches NINA within tolerance" needs a small `tools/NinaParityExtract` exe referencing `NINA.Astrometry` (with `NOVAS31.dll` co-located), calling `AstroUtil` directly to dodge `IProfileService`, emitting `ParityFixtures.BaselineSnapshot` initializers for a NINA-sourced sibling of the existing `ParityFixtures.Baselines` dictionary. ~30–60 min, native-DLL co-location the likeliest stumble. Lower-fidelity alternative: NOAA/USNO web baselines for the 9 fixtures. Full integration scope in the archived follow-ups doc.
- **Docstring drift — resolved (2026-07-07 audit).** Both cited sites were in fact fixed the same day as the review (`0e777de`), and `AltAzCalculator.Of` was later deleted outright (`b3fc182`) — nothing remains to do. Kept only for the standing warning: the review's other residuals — single-value hemisphere extensions and the `360.985647` Meeus citation literal — were deliberately left as-is; do not "fix" them.
- **Two further findings were considered and declined** (from `archive/2026-05-18-library-review-followups.md` § *Intentional non-actions*), both the kind a later reviewer re-raises. **C2:** do *not* unify `MoonSeparation.IntervalsAboveDeg` with `BestSession.MoonClearIntersect` behind a shared generic `IntervalSweep` — the predicates differ in observation arity and the type gymnastics outweigh the readability win. **D1:** `TargetGeometry.HourAngleAtAltitude`'s `(latDeg, decDeg, altDeg)` parameter order differing from its siblings' `(haHours, latDeg, decDeg)` is intentional, governed by the "input drives the signature" rule. *(That doc's fourth non-action, C4 — `LunarAge.DaysAt` throwing as the lone exception to a lenient rule — is now obsolete: the 2026-07-24 UTC gate made throwing library-wide.)*

## Open: publish to GitHub

Captured 2026-05-08. The library currently lives only on disk; sibling
TargetPlanner consumes it via local `ProjectReference`. At some point the user
wants the astronomy code in the open. Not gating any active work — recorded
here so it doesn't drift out of memory.

### Three scope options

- **Option A — Core only, public** *(recommended, but see the blocker)*. Spin out `Astronomy.Core`
  + `Astronomy.Core.Tests` + `Astronomy.Core.Benchmarks` into its own public repo. Leave
  `Astronomy.PCL` / `Astronomy.PCL.Native` in the existing private layout (or
  a separate private sibling). Smallest scope, no PCL-license entanglement,
  gets the pure-Meeus astronomy code into the open. Estimated 1–2 sessions.

  > **Blocker found 2026-07-24 — Option A cannot ship `Astronomy.Core.Tests` as-is.**
  > That project holds a hard `ProjectReference` to `Astronomy.PCL` (for the round-trip tests under
  > `Tests/PCL/`), which drags `Astronomy.PCL.Native.vcxproj` into its build graph, and it links a
  > PCL-tree asset (`..\PCL\src\utils\xisf\TestData\test.xisf`). So the "no PCL entanglement / no
  > native build" premise doesn't hold: the public repo would either fail to build or need a prior
  > step carving `Tests/PCL/` out into a separate private test project. Add that step (and re-do the
  > 1–2 session estimate), or ship Core + Benchmarks only and leave the Core tests private —
  > which weakens the "here's the code, it's tested" story the spin-out is for.
  >
  > **Second blocker (audit 2026-07-24):** none of the three Option-A csproj declares
  > `TargetFramework` or `LangVersion` — both inherit from the repo-root `Directory.Build.props`.
  > A spin-out that copies only the csproj files fails restore; the new repo needs its own
  > `Directory.Build.props` (or the properties inlined per-project).
- **Option B — whole Library, public**. One public repo with all fourteen
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
   *Mostly answered already:* the PixInsight Class Library License is
   **permissive, BSD-style** — redistribution allowed, attribution required,
   and `LICENSE.txt` must be bundled with any redistributed binaries
   (characterization in `archive/PCL-InterOp.md` § *License note*). Confirm
   against the current upstream text before committing to Option B.
2. **Personal-data scrub.** Same kind of pass as TargetPlanner's
   2026-05-08 scrub:
   - `Astronomy.Core.Tests/Tests/Astrometry/ParityFixtures.cs` has inline
     Penns Park lat/lon (`40.282835`, `74.997369`) in named test cases
     (`PennsParkSpring`, `PennsParkDstFall`, `PennsParkDstSpring`,
     `PennsParkSummerSolstice` — the middle two are the DST regressions). Parameterize them: rename to neutral
     names (e.g. `MidLatNorthSpring`) or move the personal coordinates
     into the test's `TestLocations.PennsPark` fixture (which already
     exists for the rest of the suite as of 2026-05-08).
   - **40 lines across 17 files** mention "Penns Park" (re-counted 2026-07-24 twice; the
     earlier "~14 test comments" understated it ~3×) — keep them or rephrase as
     "the 40°N test fixture"; either is defensible. **Note the scope trap:** they
     span *three* test projects, not one — 36 lines / 15 files in `Astronomy.Core.Tests`, plus
     `Astronomy.Catalog.Tests\Tests\CatalogTests.cs` (2) and
     `Astronomy.NINA.Tests\Persistence\NamedSiteTests.cs` (2). The latter two fall
     outside Option A's spin-out set, so a scrub scoped to Option A leaves them
     untouched under any option. Heaviest single files: `SessionSolversTests.cs` (5),
     `ParityFixtures.cs` / `SunEventsTests.cs` / `VisibilityWindowsTests.cs` (4 each).
     **Scrub-scope caveats (audit 2026-07-24, unresolved):** (a) the name-grep misses
     coordinate-only files — `LocationTests.cs` carries `40.282835`/`74.997369` with zero
     "Penns Park" mentions (19 coordinate lines across 4 files total); (b) the "move into
     `TestLocations.PennsPark`" remedy is circular — that fixture itself hardcodes the name
     and coordinates, and isn't on the checklist. Re-scope the scrub before executing.
   - Audit `CLAUDE.md` — **and `ROADMAP.md` / `VERIFICATION.md`, which currently carry the
     coordinates and absolute `E:\` personal paths themselves** — for personal paths, machine
     names, or Windows-user specifics that won't make sense to a public reader. Also note the
     public XML docs name portfolio apps (TP, TSM, XFM — fine locally per the parent glossary,
     decided 2026-07-24, but public readers lack the glossary; decide keep-or-scrub at publish).
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
