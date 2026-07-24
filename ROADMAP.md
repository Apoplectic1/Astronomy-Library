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

- **2026-07-24** — K-S Δmag moon gate replaces the Lorentzian (`MoonLimitProfile`; closes the partial-moon-tolerance + refraction-asymmetry items; TP migrates per its in-repo note).
- **2026-07-24** — PCL linked closure restored to upstream AVX2 (the 4800U imaging machine has no AVX-512).
- **2026-07-24** — UTC contract gate + azimuth `[0, 360)` fold-back; docs audit remediation.

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

## Open: Astronomy.XISF Tiers 2-4

Captured 2026-05-18 with the Tier 1 extraction; **added when a real consumer needs them — no eager
design.** (Moved here from `CHANGELOG.md` on 2026-07-24: forward scope belongs in the roadmap, and
`Astronomy.XISF.csproj` already pointed here.)

- **Tier 2** — header write-back. Modify FITS keywords in place, preserving the image-attachment block. Required for XFM migration (XFM does rename / normalization / accept-reject prefix writes) and a future TPS grade-state keyword write.
- **Tier 3** — full image read. Pixel data decode for uncompressed + LZ4 + zlib + zstd. Borrow compression algorithm strategies from NINA's `XISFData`; don't pull NINA's classes (decouple). Required by any consumer that does actual image processing. *(Partially seeded: the shared zlib+shuffle+SHA-1 codec shipped 2026-06 — `Astronomy.XISF.Compression`, which still has no caller outside its own tests.)*
- **Tier 4** — full image write. Image data composition + compression + checksum (SHA-256). Required for XFM's writes and any future image-save pipeline.

When XFM eventually migrates to Astronomy.XISF as its sole reader, the additional `KeywordList` accessors (FocalLength, Camera, EGAIN, MasterFrame metadata, weight keywords, etc.) port over alongside Tier 2.

## Open: `ObservationSession` — collapse the duplicated Diagnostics wiring

Deferred at the `Astronomy.Diagnostics` extraction (2026-06-11) and **now the next consolidation
there**: both live consumers duplicate the same log/session bootstrap wiring by hand. An
`ObservationSession` abstraction would own it once. Mechanics of what exists today are in
`ARCHITECTURE.md` § *Astronomy.Diagnostics*. *(Moved here 2026-07-24 — the plan had been living in
ARCHITECTURE, which is a mechanics doc, and had no roadmap entry at all.)*

## Open: Astronomy.NINA Phases C-D

Captured with the Phase A/B work (2026-05-18); neither phase has started. *(Moved here 2026-07-24 from
`ARCHITECTURE.md` § *Astronomy.NINA*, same reason as above.)*

- **Phase C** — TargetPlanner migrates from `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target`; the image library becomes a new TP target source; the Sky chart surfaces per-target Filter (color tint + badge + per-target K-S filter bandwidth).
- **Phase D** — `InputTargetAdapter` (bidirectional `Astronomy.NINA.Target ↔ NINA.InputTarget`); unblocks future NINA-sequence-JSON export from TP. Phase D is what introduces the `NINA.Plugin` NuGet dependency — `Astronomy.NINA` deliberately has **no NINA assembly dependency** until then.

## Open: public-surface retention — API ahead of its consumers

**Decision (2026-06-28, reaffirmed 2026-07-24): keep the unused public surface — do not prune.**
A large fraction of the public API has no external caller today; the inventory lives in `CONSUMERS.md`
§ *Dead / speculative public surface*. Much of it is for the **planned IntervalScheduler Plugin (ISP —
not yet started)** and XFM's planned Library migration. The TSM write-back action was once on that list
and shipped 2026-07-06 consumed as-built, which validated the call: this is *API ahead of its
consumers*, not dead generality. A smaller public surface is still better in principle, but pruning
here would just be rebuilt when ISP lands.

**Revisit a block only if it ends up with no planned consumer.** *(Moved here 2026-07-24 from
`CONSUMERS.md`: this is a forward-looking commitment, and it was the only library-level record of the
ISP plan — invisible to anyone following the router's "forward-looking → ROADMAP" rule.)*

## Open: SIMD / FMA deep dive

Captured 2026-05-12. The FMA hygiene pass landed in `b83a0d8` (Meeus +
TargetGeometry + SkyBrightness, 1.5-4.2% on Sun / Simpson paths, noise
on the transcendental-dominated moon path). The user wants to follow
up with a proper SIMD / vectorization investigation when time allows.

Full field notes — toolchain answers, runtime knobs, performance model,
microbench design lessons, the HotPathBenchmarks before/after table,
and four open directions ranked by impact — live in
**[archive/2026-06-21-simd-investigation.md](archive/2026-06-21-simd-investigation.md)**
(archived — conclusions graduated into `VERIFICATION.md` § *Benchmark findings*).

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

## Open: Library-review residuals (2026-05-18)

The 2026-05-18 library review and its re-check both fully closed — every actionable item landed (full record archived at `archive/2026-05-18-library-review*.md`). These are the only items that remained genuinely open after closure, lifted here so they don't get lost in the archive:

- **F5.7 Phase 3 — NINA-as-oracle parity.** The parity baseline currently freezes the Library's *own* post-CoordinateSharp output as a self-snapshot (catches drift, but not an independent-implementation check). Promoting it to "Library matches NINA within tolerance" needs a small `tools/NinaParityExtract` exe referencing `NINA.Astrometry` (with `NOVAS31.dll` co-located), calling `AstroUtil` directly to dodge `IProfileService`, emitting `NinaBaselineSnapshot` initializers for a `ParityFixtures.NinaBaselines` dictionary. ~30–60 min, native-DLL co-location the likeliest stumble. Lower-fidelity alternative: NOAA/USNO web baselines for the 9 fixtures. Full integration scope in the archived follow-ups doc.
- **Docstring drift — resolved (2026-07-07 audit).** Both cited sites were in fact fixed the same day as the review (`0e777de`), and `AltAzCalculator.Of` was later deleted outright (`b3fc182`) — nothing remains to do. Kept only for the standing warning: the review's other residuals — single-value hemisphere extensions and the `360.985647` Meeus citation literal — were deliberately left as-is; do not "fix" them.

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
   - **39 lines across 16 files** mention "Penns Park" (re-counted 2026-07-24; the
     earlier "~14 test comments" understated it ~2.8×) — keep them or rephrase as
     "the 40°N test fixture"; either is defensible. **Note the scope trap:** they
     span *three* test projects, not one — 34 in `Astronomy.Core.Tests`, plus
     `Astronomy.Catalog.Tests\Tests\CatalogTests.cs` (2) and
     `Astronomy.NINA.Tests\Persistence\NamedSiteTests.cs` (2). The latter two fall
     outside Option A's spin-out set, so a scrub scoped to Option A leaves them
     untouched under any option. Heaviest single files: `SessionSolversTests.cs` (5),
     `ParityFixtures.cs` / `SunEventsTests.cs` / `VisibilityWindowsTests.cs` (4 each).
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

**Scope note (2026-07-24):** since the K-S Δmag moon gate shipped, this regime feeds a
*placement gate*, not just a chart. The overdrive dims the predicted low-altitude sky →
*under*-states Δmag → the gate is too permissive below ~10° target altitude at high-k
sites. Consumers' existing low-altitude floors (TP's target floor / `KsLowAltitudeGateDeg`)
cover the practical range; recorded in the moon-brightness-gate spec as a known limit.
