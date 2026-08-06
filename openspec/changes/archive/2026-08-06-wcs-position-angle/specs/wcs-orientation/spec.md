# wcs-orientation — delta

## Purpose

Reading sky orientation out of a plate solution's CD matrix: position angle (North-toward-East), image-axis rotation, image parity, and pixel scales — the shared conversion every solver consumer needs between a WCS solution and domain-language angles.

## ADDED Requirements

### Requirement: Orientation from a CD matrix
Given the four CD-matrix elements of a tangent-plane WCS solution, the library SHALL return the image-axis rotation and the position angle (degrees from celestial North toward East, normalized to [0, 360)), the image parity (whether the image mirrors the sky, derived from the determinant sign), and the per-axis pixel scales in arcseconds per pixel.

#### Scenario: Known solved matrices reproduce reference values
- **WHEN** a CD matrix from a real plate solution with published expected values is converted
- **THEN** rotation, position angle (rotation's [0,360) complement), and both pixel scales match the reference values within 0.001

#### Scenario: Mirrored image detected and rotation corrected
- **WHEN** the CD matrix's determinant indicates mirrored parity (a single-axis flip)
- **THEN** the result reports the image as flipped and the rotation is sign-adjusted for the mirror

### Requirement: Documented domain of validity
The contract SHALL state that results are correct for normal and single-mirrored images, and that an image mirrored on both axes is mathematically indistinguishable from a 180°-rotated normal image (the determinant is unchanged) — undetectable from the matrix by construction, therefore documented rather than guarded.

#### Scenario: Both-axes mirror is out of scope by documentation
- **WHEN** a caller consults the API contract
- **THEN** the both-axes-mirror limitation and its undetectability rationale are stated on the public surface

### Requirement: Solver-specific angle offsets stay with the solver
The returned position angle SHALL be the generic WCS form (the [0,360) complement of image-axis rotation). Solver-integration offsets (e.g. a solver whose convention differs by 180°) SHALL NOT be baked into this conversion; applying them is the calling solver-wrapper's responsibility.

#### Scenario: Generic form is solver-neutral
- **WHEN** the same CD matrix is converted twice
- **THEN** the position angle is identical and carries no solver-conditional adjustment
