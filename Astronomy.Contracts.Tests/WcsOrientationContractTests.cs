using Astronomy.Core.Astrometry;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #29 — the WcsOrientation conventions a plate-solve
/// consumer bakes into persisted headers. Normative spec: openspec/specs/wcs-orientation/.
/// </summary>
public sealed class WcsOrientationContractTests
{
    private const double S = 1d / 3600d;   // 1 arcsec/px CD-matrix scale (degrees per pixel)

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #29:
    //   Position angle is North-toward-East in [0, 360); mirror parity comes from
    //   the CD-matrix DETERMINANT SIGN; and a both-axes mirror is indistinguishable
    //   from a 180° rotation BY CONSTRUCTION (its domain of validity: normal and
    //   single-mirrored images only). A consumer writes PositionAngleDegrees into
    //   persisted headers — a convention change silently rewrites every solved
    //   orientation on disk.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parity_ComesFromTheDeterminantSign()
    {
        // det < 0 is the standard (non-mirrored) sky-projection handedness.
        Assert.False(WcsOrientation.FromCdMatrix(-S, 0, 0, S).Flipped);
        // det > 0 is mirrored parity.
        Assert.True(WcsOrientation.FromCdMatrix(S, 0, 0, S).Flipped);
    }

    [Fact]
    public void UprightUnmirroredMatrix_IsPositionAngleZero_AtDeclaredScale()
    {
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(-S, 0, 0, S);

        Assert.Equal(0.0, wcs.PositionAngleDegrees, precision: 6);
        Assert.Equal(1.0, wcs.PixelScaleXArcsec, precision: 6);
        Assert.Equal(1.0, wcs.PixelScaleYArcsec, precision: 6);
    }

    [Fact]
    public void RealSolvedMatrix_ReproducesTheReferencePositionAngle()
    {
        // One of the NINA-pinned real-matrix vectors (full set: Astronomy.Core.Tests'
        // WcsOrientationTests): rotation 325.046° → PA = 360 − 325.046 = 34.954°.
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(
            -0.0002218599577337, 0.0001551969897898, 0.0001550825162187, 0.0002219379280554);

        Assert.Equal(34.954, wcs.PositionAngleDegrees, tolerance: 0.001);
        Assert.False(wcs.Flipped);
    }

    [Fact]
    public void BothAxesMirror_IsIndistinguishableFrom180Rotation()
    {
        // Negating the whole CD matrix is simultaneously "mirror both axes" and
        // "rotate 180°" — the determinant (and so the parity) is unchanged, and the
        // orientation lands exactly 180° away. This is why the domain of validity is
        // normal + single-mirrored images only.
        WcsOrientation a = WcsOrientation.FromCdMatrix(-S, 0.3 * S, 0.2 * S, S);
        WcsOrientation b = WcsOrientation.FromCdMatrix(S, -0.3 * S, -0.2 * S, -S);

        Assert.Equal(a.Flipped, b.Flipped);
        Assert.Equal((a.PositionAngleDegrees + 180.0) % 360.0, b.PositionAngleDegrees, precision: 6);
        Assert.Equal(a.PixelScaleXArcsec, b.PixelScaleXArcsec, precision: 9);
        Assert.Equal(a.PixelScaleYArcsec, b.PixelScaleYArcsec, precision: 9);
    }

    [Theory]
    [InlineData(-1, 0, 0, 1)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(-0.7, 0.7, 0.7, 0.7)]
    [InlineData(0.7, -0.7, -0.7, -0.7)]
    public void PositionAngle_IsAlwaysInHalfOpen360(double cd11, double cd12, double cd21, double cd22)
    {
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(cd11 * S, cd12 * S, cd21 * S, cd22 * S);

        Assert.True(
            wcs.PositionAngleDegrees is >= 0.0 and < 360.0,
            $"PA {wcs.PositionAngleDegrees} outside the half-open [0, 360)");
    }
}
