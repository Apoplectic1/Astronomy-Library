# Tasks: wcs-position-angle

_Design artifact deliberately skipped (conditional): single pure function, decisions carried in the
proposal (NINA math port, domain-of-validity documentation, solver-offset boundary)._

## 1. Implementation

- [x] 1.1 `Astronomy.Core\Astrometry\WcsOrientation.cs`: readonly struct + `FromCdMatrix(cd1_1, cd1_2, cd2_1, cd2_2)` → RotationDegrees, PositionAngleDegrees, Flipped, PixelScaleXArcsec, PixelScaleYArcsec; XML docs carry the parity domain-of-validity note and the solver-offset boundary (consumer-agnostic wording)

## 2. Tests

- [x] 2.1 `Astronomy.Core.Tests`: NINA's three real-matrix vectors (rotation, PA complement, pixel scales, 0.001 tolerance) + single-flip detection case (identity-scale matrix → Flipped, adjusted rotation)

## 3. Docs + verify (same commit)

- [x] 3.1 CHANGELOG entry; build + full Core test run green
