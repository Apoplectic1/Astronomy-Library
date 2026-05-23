# Astronomy Library — Roadmap

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

Sixth and seventh buildable projects added: `Astronomy.NINA` + `Astronomy.NINA.Tests`. Phase A of the multi-phase plan at `~/.claude/plans/what-is-next-from-crispy-garden.md` is complete. What landed:

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

## Open: K-S sky-brightness model improvements

Captured 2026-05-23 (relocated from TP ROADMAP — these are Library API
changes that consumers feel only through the Library). Three
independent items; the first two together produce a coherent
narrowband-aware Sky chart and either can land independently.

### Wavelength-dependent twilight

`Astronomy.Core.Brightness.SkyBrightness.KsAt` composes the dark-sky
baseline + twilight + moon contributions in mag/nL space. The
wavelength dependence today comes entirely from the single
Rayleigh-scaled `k`:

- `+k·(X−1)` extincts the **natural baseline** along the target's line
  of sight (filter-dependent ✓).
- The **moon contribution** uses `k` for both moon-airmass extinction
  of moonlight and target-airmass attenuation of scattered moonlight
  (filter-dependent ✓).
- The **twilight contribution** comes from
  `Twilight.ZenithBrightening(sunAltDeg)` which is **filter-blind** —
  it just returns a scalar mag delta and applies it as
  `vTwilight = vDark − delta` regardless of band.

That last omission produces physically-wrong results for narrowband
planning at low target altitudes during twilight: the model predicts
redder filters see *brighter* twilight sky than bluer filters (because
the brighter `vDark` for low-k filters pumps a larger nL twilight
contribution), inverting the actual Rayleigh physics where blue light
scatters into the sky path more strongly than red. K-S 1991 was
developed for broadband V-band moonlit sky brightness; the twilight
component was a pasted-on approximation. The chart is correct *as K-S
predicts* but the model's prediction breaks down at high airmass + low
sun altitude.

Fix: replace `Twilight.ZenithBrightening(sunAltDeg)` with a
wavelength-aware twilight model that scales the sun-scattering
contribution by `kAtBand`. Patat 2003 / Krisciunas 1990 follow-ups
have suitable forms. Don't pursue until the model's twilight regime
starts driving real planning decisions — for moderate-to-high
altitudes after astronomical dusk the current model is fine.

### Bandwidth-aware sky brightness

`KsAt` doesn't use the filter's bandwidth at all — `v0` (Bortle's
broadband V-band zenith mag) is consumed as-is regardless of whether
the filter is 85 nm wide (L) or 7 nm wide (Hα). For a
roughly-continuous source spectrum, integrated nL brightness in a
filter passband scales linearly with bandwidth:

```
B_band ≈ S(λ₀) · BW          // continuum approximation
mag_offset = 2.5 · log₁₀(BW_ref / BW_filter)
```

A 7 nm Hα filter against an 85 nm V-band reference is
`2.5 · log₁₀(85/7) ≈ 2.7 mag` darker for any continuous-spectrum
contribution (dark-sky baseline, twilight scatter, moonlight scatter).
This is the "narrowband advantage" the chart currently misses entirely
— Sky predictions for Hα / O3 / S2 should be 2-3 mag darker than they
show today.

Implementation: scale each of the three nL contributions in `KsAt` by
`(BW_filter / BW_ref)` before summing, then convert back to mag.
`Filters.Filter.BandwidthNm` is already on the POCO but unused by
`KsAt`; needs a new parameter on `KsAt` plus a `BW_ref` constant in
`SkyBrightness`. Caveat: the continuum approximation ignores narrow
airglow emission lines (sodium D 589 nm, OI 557.7 nm,
[OIII] 500.7 nm) — a v3 refinement would tabulate major lines and
check overlap per filter band, since narrowband O3 catches [OIII]
(slightly brighter than continuum-only would predict) while Hα and S2
sit clean of major airglow.

### TP consumer impact

When either / both of the above land, the interim `Sky` chart gate at
`AltitudeSubChart_Sky.BuildOrUpdateTargetSeries` (which currently
nulls per-minute K-S outside `[AstronomicalDusk, AstronomicalDawn]`)
can be removed — see TP ROADMAP §Future-flagged TP-side work for the
removal checklist.

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
