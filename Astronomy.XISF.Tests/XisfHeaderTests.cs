using Astronomy.XISF;
using Xunit;

namespace Astronomy.XISF.Tests;

public class XisfHeaderTests
{
    // Test helper — builds a XisfHeader from (name → value) pairs, no comments.
    private static XisfHeader Make(params (string Name, string Value)[] kv)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in kv) d[n] = new XisfHeader.KeywordEntry(v, null);
        return new XisfHeader(d);
    }

    // Test helper — builds a XisfHeader from (name, value, comment) triples.
    private static XisfHeader MakeWithComments(params (string Name, string Value, string Comment)[] kv)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v, c) in kv) d[n] = new XisfHeader.KeywordEntry(v, c);
        return new XisfHeader(d);
    }

    [Fact]
    public void Ctor_NullDict_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new XisfHeader(null));
    }

    [Fact]
    public void Raw_MissingKey_ReturnsNull()
    {
        var h = Make();
        Assert.Null(h.Raw("OBJECT"));
        Assert.False(h.Has("OBJECT"));
    }

    [Fact]
    public void Raw_CaseInsensitiveLookup()
    {
        var h = Make(("OBJECT", "M31"));
        Assert.Equal("M31", h.Raw("object"));
        Assert.Equal("M31", h.Raw("Object"));
        Assert.True(h.Has("OBJECT"));
    }

    [Fact]
    public void Comment_ReturnsNullWhenAbsentOrEmpty()
    {
        var h = Make(("OBJECT", "M31"));   // no comment supplied
        Assert.Null(h.Comment("OBJECT"));
        Assert.Null(h.Comment("MISSING"));
    }

    [Fact]
    public void Comment_ReturnsTextWhenPresent()
    {
        var h = MakeWithComments(("INSTRUME", "Z183", "ZWO ASI183MM Pro"));
        Assert.Equal("Z183", h.Raw("INSTRUME"));
        Assert.Equal("ZWO ASI183MM Pro", h.Comment("INSTRUME"));
        Assert.Equal("ZWO ASI183MM Pro", h.InstrumentDescription);
    }

    [Fact]
    public void ObjectName_PreferredAccessor()
    {
        var h = Make(("OBJECT", "NGC 7000"));
        Assert.Equal("NGC 7000", h.ObjectName);
    }

    [Fact]
    public void NumericAccessors_ParseFloatsAndInts()
    {
        var h = Make(
            ("EXPTIME", "600.0"),
            ("GAIN", "111"),
            ("OFFSET", "10"),
            ("SET-TEMP", "-20.0"),
            ("RA", "202.469625"),
            ("DEC", "47.195167"),
            ("XBINNING", "1"),
            ("YBINNING", "1"));
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
        var h = Make(("EXPTIME", "not a number"), ("GAIN", "abc"));
        Assert.Null(h.ExposureSec);
        Assert.Null(h.Gain);
    }

    [Fact]
    public void DateObsUtc_ParsesIso8601_AsUtcKind()
    {
        var h = Make(("DATE-OBS", "2024-02-18T04:51:28.000"));
        Assert.NotNull(h.DateObsUtc);
        Assert.Equal(DateTimeKind.Utc, h.DateObsUtc!.Value.Kind);
        Assert.Equal(new DateTime(2024, 2, 18, 4, 51, 28, DateTimeKind.Utc), h.DateObsUtc.Value);
    }

    [Fact]
    public void ExposureSec_FallsBackToLegacyExposureKeyword()
    {
        // XFM-processed files in Dan's library use the legacy EXPOSURE keyword, not EXPTIME.
        // New captures will use EXPTIME going forward; this fallback handles existing files.
        var h = Make(("EXPOSURE", "600"));
        Assert.Equal(600.0, h.ExposureSec);
    }

    [Fact]
    public void ExposureSec_ExptimeWinsOverLegacyExposure()
    {
        var h = Make(("EXPTIME", "300"), ("EXPOSURE", "600"));
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
        var h = Make(("OFFSET", raw.ToString()), ("INSTRUME", instrument));
        Assert.Equal(expected, h.OffsetNormalized);
    }

    [Fact]
    public void OffsetNormalized_MatchesFullNameInCommentEvenWhenValueIsShortCode()
    {
        // INSTRUME value="Z" (truncated, doesn't match any short code) but
        // comment="ZWO ASI183MM Pro" — the comment fallback should still pin it as ASI183.
        var h = MakeWithComments(
            ("OFFSET", "50", ""),
            ("INSTRUME", "Z", "ZWO ASI183MM Pro"));
        Assert.Equal(10, h.OffsetNormalized);  // 50 / 5
    }

    [Fact]
    public void OffsetNormalized_A144_Stripped()
    {
        var h = Make(("OFFSET", "100"), ("INSTRUME", "A144"));
        Assert.Null(h.OffsetNormalized);
    }

    [Fact]
    public void OffsetNormalized_MissingRaw_ReturnsNull()
    {
        var h = Make();
        Assert.Null(h.OffsetNormalized);
    }
}
