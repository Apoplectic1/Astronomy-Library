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
