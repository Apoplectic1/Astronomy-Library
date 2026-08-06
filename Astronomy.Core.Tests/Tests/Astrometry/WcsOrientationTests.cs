using Astronomy.Core.Astrometry;
using Xunit;

namespace Astronomy.Core.Tests.Tests.Astrometry;

public sealed class WcsOrientationTests
{
    // Reference vectors from real plate solutions (NINA's published WorldCoordinateSystem test
    // matrices): CD elements → expected image-axis rotation and pixel scales.
    [Theory]
    [InlineData(-0.0002218599577337, 0.0001551969897898, 0.0001550825162187, 0.0002219379280554, 325.046, 0.974, 0.974)]
    [InlineData(-1.19409568357e-05, 0.0004180497740614, 0.0004180867435723, 1.203801387e-05, 271.636, 1.506, 1.506)]
    [InlineData(-0.0001167769034989, -0.0002470716905644, -0.0002469362868456, 0.0001168178576494, 64.69, 0.983, 0.983)]
    public void FromCdMatrix_RealSolvedMatrices_ReproduceReferenceValues(
        double cd1_1, double cd1_2, double cd2_1, double cd2_2,
        double expectedRotation, double expectedScaleX, double expectedScaleY)
    {
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(cd1_1, cd1_2, cd2_1, cd2_2);

        Assert.Equal(expectedRotation, wcs.RotationDegrees, tolerance: 0.001);
        Assert.Equal(((360d - expectedRotation) % 360d + 360d) % 360d, wcs.PositionAngleDegrees, tolerance: 0.001);
        Assert.Equal(expectedScaleX, wcs.PixelScaleXArcsec, tolerance: 0.001);
        Assert.Equal(expectedScaleY, wcs.PixelScaleYArcsec, tolerance: 0.001);
        Assert.False(wcs.Flipped); // all three reference solutions have normal parity
    }

    [Fact]
    public void FromCdMatrix_MirroredParity_ReportsFlipped()
    {
        // Positive-determinant identity-scale matrix (1"/px): the image mirrors the sky.
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(1d / 3600d, 0d, 0d, 1d / 3600d);

        Assert.True(wcs.Flipped);
        Assert.Equal(1.0, wcs.PixelScaleXArcsec, 6);
        Assert.Equal(1.0, wcs.PixelScaleYArcsec, 6);
    }

    [Fact]
    public void FromCdMatrix_NormalParity_NotFlipped()
    {
        // Negative determinant (standard sky-projection handedness), zero rotation.
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(-1d / 3600d, 0d, 0d, 1d / 3600d);

        Assert.False(wcs.Flipped);
        Assert.Equal(0.0, wcs.RotationDegrees, 6);
        Assert.Equal(0.0, wcs.PositionAngleDegrees, 6);
    }

    [Fact]
    public void PositionAngle_IsComplementOfRotation_SolverNeutral()
    {
        WcsOrientation wcs = WcsOrientation.FromCdMatrix(
            -0.0002218599577337, 0.0001551969897898, 0.0001550825162187, 0.0002219379280554);

        Assert.Equal(360d - wcs.RotationDegrees, wcs.PositionAngleDegrees, 6);

        // Deterministic: the same matrix converts identically (no solver-conditional adjustment).
        WcsOrientation again = WcsOrientation.FromCdMatrix(
            -0.0002218599577337, 0.0001551969897898, 0.0001550825162187, 0.0002219379280554);
        Assert.Equal(wcs.PositionAngleDegrees, again.PositionAngleDegrees);
    }
}
