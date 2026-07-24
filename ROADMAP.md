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

- **2026-07-07** — Contracts.Tests refresh: TS surface pinned (#19–#23), #6/#10 gaps closed, exposure-0 divergence adjudicated.
- **2026-07-06** — Cadence-safe TS editing: transactional clear + `HasOverrideOrder` refusal.
- **2026-07-06** — `TsEditableSchema`: full `exposuretemplate` surface.

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
  `0c106e0` refraction fix — the "refraction freebie" in the Filter/PlanningPolicy commit): `moonAltApparent = m.MoonAltDeg +
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
