using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests.Xisf;

public class XisfHeaderTests
{
    [Fact]
    public void Ctor_NullDict_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new XisfHeader(null));
    }

    [Fact]
    public void Raw_MissingKey_ReturnsNull()
    {
        var h = new XisfHeader(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        Assert.Null(h.Raw("OBJECT"));
        Assert.False(h.Has("OBJECT"));
    }

    [Fact]
    public void Raw_CaseInsensitiveLookup()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["OBJECT"] = "M31" };
        var h = new XisfHeader(d);
        Assert.Equal("M31", h.Raw("object"));
        Assert.Equal("M31", h.Raw("Object"));
        Assert.True(h.Has("OBJECT"));
    }

    [Fact]
    public void ObjectName_PreferredAccessor()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["OBJECT"] = "NGC 7000" };
        var h = new XisfHeader(d);
        Assert.Equal("NGC 7000", h.ObjectName);
    }

    [Fact]
    public void NumericAccessors_ParseFloatsAndInts()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EXPTIME"] = "600.0",
            ["GAIN"] = "111",
            ["OFFSET"] = "10",
            ["SET-TEMP"] = "-20.0",
            ["RA"] = "202.469625",
            ["DEC"] = "47.195167",
            ["XBINNING"] = "1",
            ["YBINNING"] = "1",
        };
        var h = new XisfHeader(d);
        Assert.Equal(600.0, h.ExposureSec);
        Assert.Equal(111, h.Gain);
        Assert.Equal(10, h.OffsetRaw);
        Assert.Equal(-20.0, h.SetTempC);
        Assert.Equal(202.469625, h.RaDegrees);
        Assert.Equal(47.195167, h.DecDegrees);
        Assert.Equal(1, h.XBinning);
        Assert.Equal(1, h.YBinning);
    }

    [Fact]
    public void NumericAccessors_MalformedValueReturnsNull()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EXPTIME"] = "not a number",
            ["GAIN"] = "abc",
        };
        var h = new XisfHeader(d);
        Assert.Null(h.ExposureSec);
        Assert.Null(h.Gain);
    }

    [Fact]
    public void DateObsUtc_ParsesIso8601_AsUtcKind()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DATE-OBS"] = "2024-02-18T04:51:28.000",
        };
        var h = new XisfHeader(d);
        Assert.NotNull(h.DateObsUtc);
        Assert.Equal(DateTimeKind.Utc, h.DateObsUtc!.Value.Kind);
        Assert.Equal(new DateTime(2024, 2, 18, 4, 51, 28, DateTimeKind.Utc), h.DateObsUtc.Value);
    }

    [Fact]
    public void ExposureSec_FallsBackToLegacyExposureKeyword()
    {
        // XFM-processed files in Dan's library use the legacy EXPOSURE keyword, not EXPTIME.
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["EXPOSURE"] = "600" };
        var h = new XisfHeader(d);
        Assert.Equal(600.0, h.ExposureSec);
    }

    [Fact]
    public void ExposureSec_ExptimeWinsOverLegacyExposure()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EXPTIME"] = "300",
            ["EXPOSURE"] = "600",
        };
        var h = new XisfHeader(d);
        Assert.Equal(300.0, h.ExposureSec);
    }

    [Theory]
    [InlineData("ZWO ASI183MM Pro", 50, 10)]      // /5 — full manufacturer name
    [InlineData("Z183", 50, 10)]                  // /5 — XFM short code (observed in Dan's library)
    [InlineData("ZWO ASI533MC Pro", 200, 5)]      // /40
    [InlineData("Z533", 200, 5)]                  // /40 — short code
    [InlineData("Q178", 1833, 100)]               // /18.33
    [InlineData("Unknown Camera", 30, 30)]        // pass-through
    public void OffsetNormalized_AppliesPerCameraDivisor(string instrument, int raw, int expected)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OFFSET"] = raw.ToString(),
            ["INSTRUME"] = instrument,
        };
        var h = new XisfHeader(d);
        Assert.Equal(expected, h.OffsetNormalized);
    }

    [Fact]
    public void OffsetNormalized_A144_Stripped()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OFFSET"] = "100",
            ["INSTRUME"] = "A144",
        };
        var h = new XisfHeader(d);
        Assert.Null(h.OffsetNormalized);
    }

    [Fact]
    public void OffsetNormalized_MissingRaw_ReturnsNull()
    {
        var h = new XisfHeader(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        Assert.Null(h.OffsetNormalized);
    }
}
