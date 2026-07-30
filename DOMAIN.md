# DOMAIN.md

**Charter.** The human/strategy home for the Library's **domain layer** — astronomy science choices,
unit/convention decisions, and the multi-consumer strategy — anything that shapes the code but isn't
itself subsystem mechanics. Read when asking "*why* does the library model it this way?" rather than
"how does module X work?" (→ `ARCHITECTURE.md`).

**Status: charter'd-thin.** The Library's domain truth is currently embedded where it is load-bearing;
this file routes to it and is the ready home for domain content that outgrows those spots:

- **Science + unit conventions** (hemisphere sign convention, RA in decimal hours, unrefracted
  altitude, azimuth from North, DateTime-kind rules, immutability) — live in
  `ARCHITECTURE.md` § *Architectural conventions*, because they are baked into the public API and
  must travel with it.
- **Algorithm provenance** — the managed math is **Meeus-backed** (Jean Meeus, *Astronomical
  Algorithms*); the CoordinateSharp dependency was removed (commits `759496a` parity baseline →
  `e602bdb` Meeus swap) so every helper is
  self-contained. Native XISF pixel *read* is the vendored PixInsight **PCL** — no image math is
  exposed; the wrapper does no numerical work (see `archive/PCL-InterOp.md` for the wrapping
  decision, `ARCHITECTURE.md` § *Astronomy.PCL / Astronomy.PCL.Native* for the surface).
- **`Astrometry.AstroUtil` is deliberately a NINA-port *mirror surface*.** `GetMoonAltitude` /
  `GetMoonIllumination` / `GetMoonRiseAndSet` reproduce the shape of NINA's `NINA.Astrometry/AstroUtil.cs`
  so ported code drops in unchanged. Two standing consequences: **(a)** do not split the class into
  per-method types and do not reshape those three members — the split was explicitly considered and
  rejected 2026-05-18; **(b)** `GetMoonPhaseName` is a *deliberate divergence*, bucketing by synodic age
  where NINA buckets by Sun-Moon angle, so although the names line up, **boundary instants differ by hours
  near a quarter-phase transition**. Full record: `archive/2026-05-18-library-review.md` § B2.
- **Moon-gate tolerance calibration** — why `MoonLimitProfile`'s shipped defaults are `ToleranceMag` 1.0
  (Narrowband) / 0.30 (Broadband) rather than round numbers: each is the **cycle-median Δmag at the old
  Lorentzian's own accept/reject boundary**, computed on a Bortle-5 / k₅₀₀ = 0.28 / sun −18° geometry grid,
  so the replacement lands where the previous gate actually sat instead of re-tuning by feel. The same
  calibration is *why the Lorentzian had to go*: it was never an iso-quality contour — the sky-brightness
  tolerance it implied varied ~10-30× across the lunar cycle (crescent boundaries at Δmag ≈ 0.1 against
  full-moon boundaries at 1.6-2.0). Full derivation:
  `openspec/changes/archive/2026-07-24-ks-dmag-moon-gate/design.md` § D4; the gate's normative behavior is
  `openspec/specs/moon-brightness-gate/`, its mechanics `ARCHITECTURE.md` § *Astronomy.Core* → `Moon/`.
- **Filter presets are calibrated, datasheet-sourced values, and center/bandwidth is the spectral fact.**
  There is no filter-kind enum — a filter is a center + bandwidth in nm, and "unknown" is `CenterNm == null`.
  The `Astronomy.NINA.Filter` presets carry real product values: Astrodon 3 nm Hα 656.3 and [O III] 500.7;
  Chroma 3 nm SII **672.4 — deliberately centered between the 671.6/673.1 doublet rather than on either
  spectroscopic line**; Astrodon E-Series L 550/300, R 650/60, G 525/65, B 450/100. (Consumed by
  `SkyBrightness.ScaleK(CenterNm)`, so a wrong center moves extinction, not just a label.) Canonical filter
  **names are single letters end-to-end** — `Filter.Name` = the scanner's `FilterAggregate.FilterName` = the
  reconcile join key — which is why no normalization step exists anywhere in that chain; introducing a
  multi-character name would require inventing one. Derivation: `CHANGELOG.md` § 2026-05-27 (Filter rename +
  center/bandwidth fill; drop `FilterKind`).
- **Multi-consumer strategy** — the Library is deliberately consumer-agnostic: no consumer **UI
  terminology** (chart names, control names, per-app feature vocabulary) in the public surface or
  its XML docs. Portfolio **app names** (TP, TSM, XFM, … — the parent `..\CLAUDE.md` glossary
  vocabulary) are acceptable in `///` remarks as provenance/consumer notes; they are defined
  portfolio-wide. (Decided 2026-07-24; note the publish-to-GitHub caveat — public readers lack the
  glossary — tracked in `ROADMAP.md` § *Open: publish to GitHub*.) Who consumes what, and the
  semantic contract they rely on, is `CONSUMERS.md` (the "pinned pinout").

What belongs *here* as it accrues: observing-domain rationale not tied to one API (e.g. why the
*geometric* primitives stay unrefracted while refracted altitude is a first-class output elsewhere,
time-scale/epoch policy), naming conventions for the domain
vocabulary, and any science decision that spans modules.
