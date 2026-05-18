# Astronomy Library — Roadmap

## Recently shipped (2026-05-18): Astronomy.NINA — Phase B Target shape

Rich `Target` class + composition types in `Astronomy.NINA` root namespace, plus `Xisf/ReportToTargetAdapter` bridging Phase A output. What landed:

- **`Target`** — wraps `Astronomy.Core.Targets.Target` geometry; composes `IReadOnlyList<FilterHistory>` (empty when never imaged), `IReadOnlyList<PlannedExposure>?` (null when no plan source covers this target), optional `IHorizonProfile`, and `RotationDeg` (mod-360 normalized for lenient user input).
- **`Filter`** — sealed, immutable; `FilterKind` enum (Narrowband / Broadband / Luminance / RGB / Unknown); static factory presets (`Ha`/`OIII`/`SII`/`L`/`R`/`G`/`B`) with conventional center/bandwidth values.
- **`FilterHistory`** — per-(filter, purpose) rich history with count + integration + first/last imaged + typical settings. Carries `FilterPurpose` (Light vs Stars) so star-recombination captures don't muddy primary integration totals.
- **`ExposureSettings`** + **`PlannedExposure`** — leaf records for camera config + forward-looking sequence-plan entries.
- **`Xisf/ReportToTargetAdapter`** — extension methods `ImageLibraryReport.ToTargets()` / `TargetReport.ToTarget()`; maps XFM single-letter filter codes to standard `Filter` presets, preserves `FilterPurpose`, hands signed declination to Core's normalizing ctor (passing a pre-derived `north` flag would double-flip and silently land southern targets in the wrong hemisphere — caught by an early test).

All sealed + immutable + `With(...)` per AL convention. 87 unit tests pass cumulative (Phase A 48 + Phase B 39).

## Recently shipped (2026-05-18): Astronomy.NINA — Phase A foundation

Sixth and seventh buildable projects added: `Astronomy.NINA` + `Astronomy.NINA.Tests`. Phase A of the multi-phase plan at `~/.claude/plans/what-is-next-from-crispy-garden.md` is complete. What landed:

- **`Xisf/XisfHeaderReader`** — pure-managed XISF header parser, ported from `XisfFileManager/Files/XisfXmlReader.cs` (XDocument-based, no native dependency). Read-only; no XFM mutation logic.
- **`Xisf/XisfHeader`** — typed FITS-keyword accessors. Required-for-aggregation keywords (OBJECT, RA, DEC, DATE-OBS, EXPTIME with legacy EXPOSURE fallback, FILTER, GAIN, OFFSET with per-camera normalization, SET-TEMP, CCD-TEMP, X/YBINNING, IMAGETYP, INSTRUME) plus capture-only (SSWEIGHT/W_SNR/W_FWHM/W_ECC, FOCALLEN, focuser/rotator, etc.) for future quality-summary work.
- **`Xisf/ImageLibraryScanner`** — walks user's standardized image library (`<Target>/Captures/<Camera>/<Filter>/*.xisf`), groups by OBJECT FITS keyword + directory-derived filter+purpose, aggregates per-target / per-filter counts, integration totals, first/last imaged UTC, mode-based typical settings. Parallel per-target scan; .xisf parse failures recorded in `SkippedFiles` rather than aborting.
- **Output records**: `ImageLibraryReport`, `TargetReport` (DirectoryName/Catalog/CommonName/ObjectName/RaHours/DecDegrees/Filters), `FilterAggregate`, `TypicalSettings`, `FilterPurpose` enum (Light/Stars). Sealed + immutable per AL convention.
- **36 unit tests** + **smoke test** gated on `TP_SMOKE_IMAGE_LIBRARY` env var. Smoke run against Dan's `E:\Photography\Astro Photography\Processing` library: **70 targets, 14,015 frames, 1,228 hours of integration** in ~1s — sane data flowing end-to-end.

**Forward roadmap (next-up phases, separate commits):**

- **Phase C** — TargetPlanner migrates from `Astronomy.Core.Targets.Target` to `Astronomy.NINA.Target`; image library becomes a new TP target source; Sky chart surfaces per-target Filter (color tint + badge + per-target K-S filter bandwidth).
- **Phase D** — `InputTargetAdapter` (bidirectional `Astronomy.NINA.Target ↔ NINA.InputTarget`); unblocks future NINA-sequence-JSON export from TP. Phase D introduces the `NINA.Plugin` NuGet dependency.

**Deferred open question:** `Astronomy.XISF` extraction (move `Xisf/` subtree into a separate library) — see plan file. Discussion after Phase B settles.

## Open: SIMD / FMA deep dive

Captured 2026-05-12. The FMA hygiene pass landed in `b83a0d8` (Meeus +
TargetGeometry + SkyBrightness, 1.5-4.2% on Sun / Simpson paths, noise
on the transcendental-dominated moon path). The user wants to follow
up with a proper SIMD / vectorization investigation when time allows.

Full field notes — toolchain answers, runtime knobs, performance model,
microbench design lessons, the HotPathBenchmarks before/after table,
and four open directions ranked by impact — live in
**[docs/SIMD_Investigation.md](docs/SIMD_Investigation.md)**.

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
  + `Astronomy.Core.Tests` (the xUnit half) into its own public repo. Leave
  `Astronomy.PCL` / `Astronomy.PCL.Native` in the existing private layout (or
  a separate private sibling). Smallest scope, no PCL-license entanglement,
  gets the pure-Meeus astronomy code into the open. Estimated 1–2 sessions.
- **Option B — whole Library, public**. One public repo with all four
  projects. PCL adds friction: third-party SDK dependency, build docs,
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
   paths in commit diffs. The library was extracted from TargetPlanner in
   `b28ef9e` (2026-04-23), so the surface area to audit is small.
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
