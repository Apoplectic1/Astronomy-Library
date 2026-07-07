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
  Algorithms*); the CoordinateSharp dependency was removed (commit `2249834`) so every helper is
  self-contained. Native image math is the vendored PixInsight **PCL** (see
  `archive/PCL-InterOp.md` for the wrapping decision).
- **Multi-consumer strategy** — the Library is deliberately consumer-agnostic (no downstream app
  names or UI terminology in the public surface). Who consumes what, and the semantic contract they
  rely on, is `CONSUMERS.md` (the "pinned pinout").

What belongs *here* as it accrues: observing-domain rationale not tied to one API (e.g. why altitude
stays unrefracted portfolio-wide, time-scale/epoch policy), naming conventions for the domain
vocabulary, and any science decision that spans modules.
