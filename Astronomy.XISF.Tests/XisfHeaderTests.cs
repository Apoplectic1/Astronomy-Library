using Astronomy.XISF;
using Xunit;

namespace Astronomy.XISF.Tests;

public class XisfHeaderTests
{
    // A real sensor's dimensions, for the cases that don't care about geometry.
    private const int W = 5496;
    private const int H = 3672;

    // Test helper — builds a XisfHeader from (name → value) pairs, no comments.
    private static XisfHeader Make(params (string Name, string Value)[] kv) => Sized(W, H, kv);

    // Test helper — as Make, with explicit pixel dimensions.
    private static XisfHeader Sized(int width, int height, params (string Name, string Value)[] kv)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in kv) d[n] = new XisfHeader.KeywordEntry(v, null);
        return new XisfHeader(d, width, height);
    }

    // Test helper — builds a XisfHeader from (name, value, comment) triples.
    private static XisfHeader MakeWithComments(params (string Name, string Value, string Comment)[] kv)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v, c) in kv) d[n] = new XisfHeader.KeywordEntry(v, c);
        return new XisfHeader(d, W, H);
    }

    [Fact]
    public void Ctor_NullDict_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new XisfHeader(null, W, H));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void Ctor_NonPositiveDimensions_Throws(int width, int height)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        Assert.Throws<ArgumentOutOfRangeException>(() => new XisfHeader(d, width, height));
    }

    [Fact]
    public void PixelDimensions_SurviveVerbatim()
    {
        XisfHeader h = Sized(3008, 3008);
        Assert.Equal(3008, h.PixelWidth);
        Assert.Equal(3008, h.PixelHeight);
    }

    // THE regression that guards 15.8% of the real library. Writers record XPIXSZ already scaled for the
    // binning in use, so a binned frame reports half the dimensions AND double the pixel size — the field it
    // imaged is unchanged. A `* XBINNING` anywhere in the derivation would double this one and pass every
    // other test in this file.
    [Fact]
    public void FieldSize_BinnedFrameMatchesUnbinned_BinningNeverMultiplies()
    {
        XisfHeader unbinned = Sized(5496, 3672,
            ("FOCALLEN", "531"), ("XPIXSZ", "2.40"), ("YPIXSZ", "2.40"), ("XBINNING", "1"), ("YBINNING", "1"));
        XisfHeader binned = Sized(2744, 1836,
            ("FOCALLEN", "531"), ("XPIXSZ", "4.80"), ("YPIXSZ", "4.80"), ("XBINNING", "2"), ("YBINNING", "2"));

        Assert.Equal(unbinned.FieldWidthDeg!.Value, binned.FieldWidthDeg!.Value, 2);
        Assert.Equal(unbinned.FieldHeightDeg!.Value, binned.FieldHeightDeg!.Value, 2);
    }

    [Fact]
    public void FieldSize_MatchesMeasuredRealSensors()
    {
        // Measured over the live library: Z183 5496x3672 @ 2.40 um on f=531 -> 1.423 x 0.951 degrees.
        XisfHeader z183 = Sized(5496, 3672, ("FOCALLEN", "531"), ("XPIXSZ", "2.40"), ("YPIXSZ", "2.40"));
        Assert.Equal(0.932, z183.PixelScaleArcsecX!.Value, 3);
        Assert.Equal(1.423, z183.FieldWidthDeg!.Value, 3);
        Assert.Equal(0.951, z183.FieldHeightDeg!.Value, 3);

        // Z533 is square — 3008x3008 @ 3.76 um -> 1.220 x 1.220 degrees.
        XisfHeader z533 = Sized(3008, 3008, ("FOCALLEN", "531"), ("XPIXSZ", "3.76"), ("YPIXSZ", "3.76"));
        Assert.Equal(1.220, z533.FieldWidthDeg!.Value, 3);
        Assert.Equal(z533.FieldWidthDeg!.Value, z533.FieldHeightDeg!.Value, 6);
    }

    [Fact]
    public void FieldSize_LongerFocalLength_SmallerField()
    {
        XisfHeader shortFl = Sized(5496, 3672, ("FOCALLEN", "531"), ("XPIXSZ", "2.40"));
        XisfHeader longFl = Sized(5496, 3672, ("FOCALLEN", "1062"), ("XPIXSZ", "2.40"));
        Assert.True(longFl.FieldWidthDeg < shortFl.FieldWidthDeg);
        Assert.Equal(shortFl.FieldWidthDeg!.Value / 2.0, longFl.FieldWidthDeg!.Value, 6);
    }

    [Theory]
    [InlineData(null, "2.40")]   // no focal length
    [InlineData("531", null)]    // no pixel size
    [InlineData("0", "2.40")]    // non-positive focal length
    public void FieldSize_MissingOrUnusableKeywords_IsNull(string? focalLen, string? pixelSize)
    {
        List<(string, string)> kv = [];
        if (focalLen is not null) kv.Add(("FOCALLEN", focalLen));
        if (pixelSize is not null) kv.Add(("XPIXSZ", pixelSize));

        XisfHeader h = Sized(W, H, [.. kv]);
        Assert.Null(h.PixelScaleArcsecX);
        Assert.Null(h.FieldWidthDeg);
        Assert.Null(h.FieldHeightDeg);
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
    [InlineData("Z183", 10)]     // as recorded, whatever the camera
    [InlineData("Z533", 50)]
    [InlineData("Unknown Camera", 30)]
    public void OffsetRaw_IsNeverRescaledPerCamera(string instrument, int recorded)
    {
        var h = Make(("OFFSET", recorded.ToString()), ("INSTRUME", instrument));
        Assert.Equal(recorded, h.OffsetRaw);
    }

    [Fact]
    public void OffsetRaw_DescriptiveDividedByCommentDoesNotRescale()
    {
        // The writer's "ADU Offset divided by 5" comment describes the camera's scale; it does NOT mean
        // the writer divided. The recorded value must survive untouched.
        var h = MakeWithComments(
            ("OFFSET", "10", "[#] ADU Offset divided by 5"),
            ("INSTRUME", "Z183", "ZWO ASI183MM Pro"));
        Assert.Equal(10, h.OffsetRaw);
    }

    [Fact]
    public void OffsetRaw_MissingKeyword_ReturnsNull()
    {
        var h = Make();
        Assert.Null(h.OffsetRaw);
    }
}
