using Astronomy.NINA;
using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests;

public class FilterTests
{
    [Fact]
    public void Ctor_RequiresName()
    {
        Assert.Throws<ArgumentException>(() => new Filter(""));
        Assert.Throws<ArgumentException>(() => new Filter("   "));
        Assert.Throws<ArgumentNullException>(() => new Filter(null));
    }

    [Fact]
    public void Ctor_RejectsNonPositiveBandwidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Filter("H", 656.3, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Filter("H", 656.3, -3.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Filter("H", -100.0, 3.0));
    }

    [Fact]
    public void Ctor_AllowsNullBandwidth()
    {
        Filter custom = new("Custom");
        Assert.Null(custom.CenterNm);
        Assert.Null(custom.BandwidthNm);
    }

    [Fact]
    public void Presets_HaveExpectedShape()
    {
        Assert.Equal("H", Filter.H.Name);
        Assert.Equal(656.3, Filter.H.CenterNm);
        Assert.Equal(3.0, Filter.H.BandwidthNm);

        Assert.Equal(550.0, Filter.L.CenterNm);
        Assert.Equal(300.0, Filter.L.BandwidthNm);

        Assert.Equal(650.0, Filter.R.CenterNm);
        Assert.Equal(60.0, Filter.R.BandwidthNm);
    }

    [Fact]
    public void With_OverridesOnly_SpecifiedProperties()
    {
        Filter f = Filter.H.With(bandwidthNm: 7.0);
        Assert.Equal("H", f.Name);
        Assert.Equal(656.3, f.CenterNm);
        Assert.Equal(7.0, f.BandwidthNm);
    }
}

public class ExposureSettingsTests
{
    [Fact]
    public void Ctor_RequiresValidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExposureSettings(-1, 0, -10, (1, 1), 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExposureSettings(100, 0, -10, (0, 1), 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExposureSettings(100, 0, -10, (1, 0), 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExposureSettings(100, 0, -10, (1, 1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExposureSettings(100, 0, -10, (1, 1), -60));
    }

    [Fact]
    public void Ctor_AcceptsNegativeOffset()
    {
        // Some camera workflows use negative offset; only Gain must be non-negative.
        ExposureSettings e = new(100, -5, -20.0, (1, 1), 300);
        Assert.Equal(-5, e.Offset);
    }

    [Fact]
    public void With_RoundTrips()
    {
        ExposureSettings a = new(111, 10, -20.0, (1, 1), 600);
        ExposureSettings b = a.With(exposureSec: 300);
        Assert.Equal(111, b.Gain);
        Assert.Equal(10, b.Offset);
        Assert.Equal(-20.0, b.SetTempC);
        Assert.Equal((1, 1), b.Binning);
        Assert.Equal(300, b.ExposureSec);
    }
}

public class PlannedExposureTests
{
    [Fact]
    public void Ctor_RequiresFilterAndPositiveCount()
    {
        Assert.Throws<ArgumentNullException>(() => new PlannedExposure(null, 10, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlannedExposure(Filter.H, 0, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlannedExposure(Filter.H, -1, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlannedExposure(Filter.H, 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlannedExposure(Filter.H, 10, -60));
    }

    [Fact]
    public void Settings_OptionalAndNullable()
    {
        PlannedExposure p = new(Filter.H, 10, 600);
        Assert.Null(p.Settings);

        ExposureSettings s = new(111, 10, -20, (1, 1), 600);
        PlannedExposure p2 = p.With(settings: s);
        Assert.NotNull(p2.Settings);
        Assert.Equal(111, p2.Settings.Gain);
    }
}

public class FilterHistoryTests
{
    private static ExposureSettings DefaultSettings() => new(111, 10, -20, (1, 1), 600);

    [Fact]
    public void Ctor_RequiresPositiveCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FilterHistory(
            Filter.H, FilterPurpose.Light, 0, TimeSpan.FromSeconds(60),
            DateTime.UtcNow, DateTime.UtcNow, DefaultSettings()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FilterHistory(
            Filter.H, FilterPurpose.Light, 5, TimeSpan.Zero,
            DateTime.UtcNow, DateTime.UtcNow, DefaultSettings()));
    }

    [Fact]
    public void Ctor_RejectsReversedDates()
    {
        DateTime first = new(2024, 2, 18, 4, 0, 0, DateTimeKind.Utc);
        DateTime last = new(2024, 2, 17, 4, 0, 0, DateTimeKind.Utc);  // earlier
        Assert.Throws<ArgumentException>(() => new FilterHistory(
            Filter.H, FilterPurpose.Light, 5, TimeSpan.FromHours(1),
            first, last, DefaultSettings()));
    }

    [Fact]
    public void Ctor_NormalizesNonUtcDatesToUtc()
    {
        // Pass Unspecified-kind dates; ctor promotes via ToUniversalTime().
        DateTime first = new(2024, 2, 18, 4, 0, 0, DateTimeKind.Unspecified);
        DateTime last = new(2024, 2, 18, 5, 0, 0, DateTimeKind.Unspecified);
        FilterHistory h = new(Filter.H, FilterPurpose.Light, 5, TimeSpan.FromHours(1),
            first, last, DefaultSettings());
        Assert.Equal(DateTimeKind.Utc, h.FirstImagedUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, h.LastImagedUtc.Kind);
    }

    [Fact]
    public void With_RoundTrips()
    {
        DateTime first = new(2024, 2, 18, 4, 0, 0, DateTimeKind.Utc);
        DateTime last = new(2024, 2, 18, 5, 0, 0, DateTimeKind.Utc);
        FilterHistory a = new(Filter.H, FilterPurpose.Light, 5, TimeSpan.FromHours(1),
            first, last, DefaultSettings());
        FilterHistory b = a.With(exposureCount: 10, purpose: FilterPurpose.Stars);
        Assert.Equal(10, b.ExposureCount);
        Assert.Equal(FilterPurpose.Stars, b.Purpose);
        Assert.Equal("H", b.Filter.Name);
    }
}
