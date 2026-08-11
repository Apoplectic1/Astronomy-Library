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
- **Where NINA already solves a problem the Library needs, NINA is a *strategy reference, not a
  source*.** Formulas are re-derived from their standard/public form (WCS trigonometry for
  `Astrometry.WcsOrientation`; the XISF spec's tokens and levels for the block-codec layer) and then
  pinned by NINA's published test vectors and call-identical interop fixtures — so AL carries no
  NINA-licensed code, which became load-bearing on 2026-08-02 when this repo went public under MIT
  while NINA's source carries MPL. `WcsOrientation`'s domain of validity is itself a domain fact:
  it covers normal and single-mirrored images only (a both-axes mirror is indistinguishable from a
  180° rotation by construction), and solver-specific offsets (e.g. ASTAP's 180°) stay with the
  calling wrapper, never modeled here. The single deliberate exception to re-derive-don't-mirror is
  `Astrometry.AstroUtil`, next bullet. (Derivation:
  `openspec/changes/archive/2026-08-06-wcs-position-angle/proposal.md` — "MPL-untangled".)
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
  portfolio-wide. (Decided 2026-07-24. The condition it flagged is now live: the mirror has been
  public since 2026-08-02 and public readers lack the glossary — neutrality rule at `RELEASING.md`
  § *Content rules*.) Who consumes what, and the
  semantic contract they rely on, is `CONSUMERS.md` (the "pinned pinout").
  Two further standing rules of the same strategy:
  - **No library-side caching** — AL primitives are pure functions; memoization is the consumer's
    job at the consumer's own scope (TP's `ChartCacheStore`, IS's planned cache). When compute is
    lifted out of a consumer it is deliberately reshaped into a stateless primitive so the next
    consumer adopts the same surface without re-porting; `NightCache` is the one sanctioned,
    caller-owned-lifetime amortization helper, not a counter-example. (Derivation: `CHANGELOG.md`
    § 2026-05-28, the MoonEphemeris/`AltitudeCurve.Sample` extraction.)
  - **A per-framework Diagnostics satellite carries framework glue only** — the shell, its controls,
    the session delegates — never an app-framework utility: a consumer helper the shell happens to
    need is re-implemented privately inside the satellite (TSM's `UiTask.FireAndLog` was inlined,
    not exported, 2026-08-10 § D3), the same scope rule that keeps TSM's `AppDialog` behavior layer
    parked out of the library until a second WinUI consumer justifies it
    (`openspec/changes/archive/2026-08-10-diagnostics-portable-core/design.md` § D3/D5).

- **Stars-purpose frames count toward a filter's exposure goal by default**
  (`ReconcilePolicy.Combined`) because the observing workflow shoots broadband RGB solely as
  star-colour frames for otherwise-narrowband targets — those frames are real progress against that
  filter's plan, not a separate product. `LightOnly` is retained as the strict-separation policy for
  callers wanting purpose-pure accounting. (Mechanics: `ARCHITECTURE.md` § *Astronomy.Catalog* →
  `Reconcile/`; derivation: `CHANGELOG.md` § 2026-06, goal-vs-actual reconciliation.)

What belongs *here* as it accrues: observing-domain rationale not tied to one API (e.g. why the
*geometric* primitives stay unrefracted while refracted altitude is a first-class output elsewhere,
time-scale/epoch policy), naming conventions for the domain
vocabulary, and any science decision that spans modules.
