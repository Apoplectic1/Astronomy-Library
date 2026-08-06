# Proposal: wcs-position-angle

## Why

A plate solution's CD matrix (FITS WCS `CD1_1..CD2_2`) encodes sky orientation — rotation, pixel scale, and image parity — but nothing in AL can read it. The first consumer is queued: XFM's ASTAP solve step (decided 2026-08-06) must convert solved CD matrices into the position angle it stamps as `OBJCTROT`. The conversion is pure astrometry with subtle sign/parity conventions — exactly the kind of math that belongs in `Astronomy.Core` once, pinned by known-good test vectors, rather than re-derived per app.

## What Changes

- New `Astronomy.Core.Astrometry` type: derive from a CD matrix — **position angle** (degrees from celestial North toward East, [0, 360)), **image-axis rotation**, **parity** (flipped: is the image a mirror of the sky, from the determinant sign), and **pixel scales** (arcsec/px per axis).
- Math ports NINA's `WorldCoordinateSystem` CD-matrix path (strategy reference, MPL-untangled: the formulas are standard WCS trigonometry), pinned by NINA's published test vectors (three real solved matrices + flip cases).
- **Documented domain of validity, not a guard**: correct for normal and single-mirrored images; a both-axes mirror is mathematically indistinguishable from a 180° rotation (the determinant is unchanged), so it cannot be detected from the matrix — stated in the contract, no runtime check possible.
- **Solver-specific offsets stay with solvers**: NINA's ASTAP integration adds 180° to the generic position angle (`360 − (Rotation − 180)`). That offset is an ASTAP-wrapper concern for the consumer, deliberately NOT baked into this math; the contract documents the generic form. (Fold-180 consumers are indifferent either way.)

Out of scope: gnomonic projection / per-pixel coordinate lookup (`GetCoordinates` — no consumer), the CDELT/CROTA legacy input form (ASTAP always emits a CD matrix), any solver invocation or file I/O.

## Capabilities

### New Capabilities

- `wcs-orientation`: the CD-matrix → orientation contract — position angle, rotation, parity, pixel scales; conventions, domain of validity, and the solver-offset boundary.

### Modified Capabilities

_None._

## Impact

- **Code**: one new file in `Astronomy.Core\Astrometry\`; tests in `Astronomy.Core.Tests` (NINA vectors + flip cases).
- **Dependencies**: none.
- **Consumers**: none change now; XFM's ASTAP step consumes next.
- **Docs**: CHANGELOG entry; ROADMAP untouched (not a tracked open item).
