using Astronomy.NINA;
using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests;

public class TargetTests
{
    private static Astronomy.Core.Targets.Target M31Geometry() =>
        new("M31", 0.712306, 41.269167, north: true, directory: "", enabled: true);

    [Fact]
    public void Ctor_RequiresNameAndGeometry()
    {
        Assert.Throws<ArgumentException>(() => new Astronomy.NINA.Target("", M31Geometry()));
        Assert.Throws<ArgumentException>(() => new Astronomy.NINA.Target("   ", M31Geometry()));
        Assert.Throws<ArgumentNullException>(() => new Astronomy.NINA.Target(null, M31Geometry()));
        Assert.Throws<ArgumentNullException>(() => new Astronomy.NINA.Target("M31", null));
    }

    [Fact]
    public void Ctor_DefaultsImagingHistoryToEmpty()
    {
        Astronomy.NINA.Target t = new("M31", M31Geometry());
        Assert.NotNull(t.ImagingHistory);
        Assert.Empty(t.ImagingHistory);
        Assert.Null(t.PlannedExposures);
        Assert.Null(t.CustomHorizon);
        Assert.Equal(0.0, t.RotationDeg);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(45.0, 45.0)]
    [InlineData(359.99, 359.99)]
    [InlineData(360.0, 0.0)]      // wraps
    [InlineData(720.0, 0.0)]      // double wrap
    [InlineData(-90.0, 270.0)]    // negative wraps positive
    [InlineData(-360.0, 0.0)]
    public void Ctor_NormalizesRotationModulo360(double input, double expected)
    {
        Astronomy.NINA.Target t = new("M31", M31Geometry(), rotationDeg: input);
        Assert.Equal(expected, t.RotationDeg, 9);
    }

    [Fact]
    public void Ctor_PreservesGeometry()
    {
        var geom = M31Geometry();
        Astronomy.NINA.Target t = new("M31 - Andromeda", geom);
        Assert.Same(geom, t.Geometry);
        Assert.Equal(0.712306, t.Geometry.RightAscension);
        Assert.Equal(41.269167, t.Geometry.Declination);
        Assert.True(t.Geometry.North);
    }

    [Fact]
    public void With_RoundTripsAllProperties()
    {
        Astronomy.NINA.Target a = new("M31", M31Geometry(), rotationDeg: 90);
        var history = new[] {
            new FilterHistory(Filter.H, FilterPurpose.Light, 5, TimeSpan.FromHours(1),
                new DateTime(2024,2,18,4,0,0,DateTimeKind.Utc),
                new DateTime(2024,2,18,5,0,0,DateTimeKind.Utc),
                new ExposureSettings(111, 10, -20, (1, 1), 600))
        };
        var planned = new[] { new PlannedExposure(Filter.O, 20, 600) };

        Astronomy.NINA.Target b = a.With(imagingHistory: history, plannedExposures: planned, rotationDeg: 180);
        Assert.Equal("M31", b.Name);
        Assert.Same(a.Geometry, b.Geometry);
        Assert.Single(b.ImagingHistory);
        Assert.NotNull(b.PlannedExposures);
        Assert.Single(b.PlannedExposures);
        Assert.Equal(180.0, b.RotationDeg);
    }
}
