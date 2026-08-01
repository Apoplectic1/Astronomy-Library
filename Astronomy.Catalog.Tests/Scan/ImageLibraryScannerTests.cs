using System.Globalization;
using System.Text;
using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Catalog.Tests.Scan;

public class ImageLibraryScannerTests
{
    [Theory]
    [InlineData("L", "L", FilterPurpose.Light)]
    [InlineData("H", "H", FilterPurpose.Light)]
    [InlineData("B", "B", FilterPurpose.Light)]
    [InlineData("Stars B", "B", FilterPurpose.Stars)]
    [InlineData("stars b", "b", FilterPurpose.Stars)]      // case-insensitive prefix
    [InlineData("Stars   R", "R", FilterPurpose.Stars)]    // multi-space tolerated by Trim
    public void ParseFilterDirName_HandlesLightAndStarsVariants(string dirName, string expectedCode, FilterPurpose expectedPurpose)
    {
        // ParseFilterDirName / NormalizeFilterName are internal — access enabled via
        // [InternalsVisibleTo("Astronomy.NINA.Tests")] in Astronomy.NINA.csproj.
        var (code, purpose) = ImageLibraryScanner.ParseFilterDirName(dirName);
        Assert.Equal(expectedCode, code, ignoreCase: true);
        Assert.Equal(expectedPurpose, purpose);
    }

    [Theory]
    // Canonical single-letter forms pass through unchanged.
    [InlineData("L", "L")]
    [InlineData("H", "H")]
    [InlineData("O", "O")]
    [InlineData("S", "S")]
    [InlineData("R", "R")]
    [InlineData("G", "G")]
    [InlineData("B", "B")]
    // Unrecognized codes pass through (custom filters keep their dir-name).
    [InlineData("Custom", "Custom")]
    [InlineData("XYZ-narrowband", "XYZ-narrowband")]
    public void NormalizeFilterName_MapsSingleLetterToCanonical(string code, string expected)
    {
        Assert.Equal(expected, ImageLibraryScanner.NormalizeFilterName(code));
    }

    [Fact]
    public void TargetReport_SplitDirectoryName_WithSeparator()
    {
        var (catalog, common) = TargetReport.SplitDirectoryName("M51 - Whirlpool");
        Assert.Equal("M51", catalog);
        Assert.Equal("Whirlpool", common);
    }

    [Fact]
    public void TargetReport_SplitDirectoryName_NoSeparator()
    {
        var (catalog, common) = TargetReport.SplitDirectoryName("Cassiopia A");
        Assert.Equal("Cassiopia A", catalog);
        Assert.Null(common);
    }

    [Fact]
    public void TargetReport_SplitDirectoryName_MultipleSeparators_SplitsAtFirst()
    {
        // "Abell 6 & HFG 1" has no " - "; ensure we don't choke on similar strings
        var (catalog, common) = TargetReport.SplitDirectoryName("IC 1318 - Sadr Region");
        Assert.Equal("IC 1318", catalog);
        Assert.Equal("Sadr Region", common);
    }

    [Fact]
    public async Task ScanAsync_NonexistentRoot_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => ImageLibraryScanner.ScanAsync(@"Q:\definitely\does\not\exist", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScanAsync_Mosaic_DescendsTheExtraPanelLevel()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcm_scan_" + Guid.NewGuid().ToString("N"));
        // Mosaic: a frame nested one level deeper, under an opaque panel dir.
        string mosaicFrame = Path.Combine(root, "Mosaic - Demo", "Captures", "Z183", "Panel 01of04", "H", "f.xisf");
        // A standard target alongside it: a frame at the normal depth.
        string standardFrame = Path.Combine(root, "M1 - Crab", "Captures", "Z183", "H", "f.xisf");
        Directory.CreateDirectory(Path.GetDirectoryName(mosaicFrame)!);
        Directory.CreateDirectory(Path.GetDirectoryName(standardFrame)!);
        await File.WriteAllTextAsync(mosaicFrame, "not xisf", TestContext.Current.CancellationToken);      // invalid header → recorded in SkippedFiles
        await File.WriteAllTextAsync(standardFrame, "not xisf", TestContext.Current.CancellationToken);
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);

            // Both frames were reached by the walk. The mosaic frame sits one level deeper than a standard one, so
            // reaching it proves the scanner descended the opaque panel level (a non-mosaic walk would look for
            // *.xisf directly under "Panel 01of04" and never find it).
            Assert.Contains(report.SkippedFiles.Keys, k => Slash(k).Contains("Mosaic - Demo/Captures/Z183/Panel 01of04/H/f.xisf"));
            Assert.Contains(report.SkippedFiles.Keys, k => Slash(k).Contains("M1 - Crab/Captures/Z183/H/f.xisf"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanUnitsAsync_Mosaic_ReturnsOneUnitPerPanel_WithOwnCountsAndBinning()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcm_units_" + Guid.NewGuid().ToString("N"));
        string mosaic = Path.Combine(root, "Mosaic - Demo");
        // Panel 1: two H frames @ 2x2; Panel 2: one H frame @ 2x2 — distinct sky positions, same camera.
        WritePanelFrame(mosaic, "Z183", "Panel 01of02", "H", "p1a.xisf", ra: 305.0, dec: 30.5, bin: 2);
        WritePanelFrame(mosaic, "Z183", "Panel 01of02", "H", "p1b.xisf", ra: 305.0, dec: 30.5, bin: 2);
        WritePanelFrame(mosaic, "Z183", "Panel 02of02", "H", "p2a.xisf", ra: 312.0, dec: 31.5, bin: 2);
        try
        {
            IReadOnlyList<TargetReport> units = await ImageLibraryScanner.ScanUnitsAsync(mosaic, TestContext.Current.CancellationToken);

            Assert.Equal(2, units.Count);
            TargetReport p1 = Assert.Single(units, u => u.DirectoryName == "Panel 01of02");
            TargetReport p2 = Assert.Single(units, u => u.DirectoryName == "Panel 02of02");
            Assert.Equal(2, Assert.Single(p1.Filters).ExposureCount);   // not summed with panel 2
            Assert.Equal(1, Assert.Single(p2.Filters).ExposureCount);
            Assert.Equal((2, 2), Assert.Single(p1.Filters).Typical.Binning);
            Assert.Equal(305.0 / 15.0, p1.RaHours, precision: 3);       // the panel's own centroid (RA deg → hours)
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanAsync_Mosaic_RetainsPerPanelReports()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcm_scan_" + Guid.NewGuid().ToString("N"));
        string mosaic = Path.Combine(root, "Mosaic - Demo");
        WritePanelFrame(mosaic, "Z183", "Panel 01of02", "H", "p1a.xisf", ra: 305.0, dec: 30.5, bin: 2);
        WritePanelFrame(mosaic, "Z183", "Panel 01of02", "H", "p1b.xisf", ra: 305.0, dec: 30.5, bin: 2);
        WritePanelFrame(mosaic, "Z183", "Panel 02of02", "H", "p2a.xisf", ra: 312.0, dec: 31.5, bin: 2);
        WriteFrame(Path.Combine(root, "M1 - Crab", "Captures", "Z183", "H"), "n.xisf", ra: 83.6, dec: 22.0, bin: 1);
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);

            // The mosaic's whole-target aggregate still sums the panels (one walk, both granularities)...
            TargetReport parent = Assert.Single(report.Targets, t => t.DirectoryName == "Mosaic - Demo");
            Assert.Equal(3, Assert.Single(parent.Filters).ExposureCount);

            // ...AND each panel survives as a sub-report with its own counts and centroid.
            Assert.Equal(2, parent.Panels.Count);
            TargetReport p1 = Assert.Single(parent.Panels, p => p.DirectoryName == "Panel 01of02");
            TargetReport p2 = Assert.Single(parent.Panels, p => p.DirectoryName == "Panel 02of02");
            Assert.Equal(2, Assert.Single(p1.Filters).ExposureCount);
            Assert.Equal(1, Assert.Single(p2.Filters).ExposureCount);
            Assert.Equal(305.0 / 15.0, p1.RaHours, precision: 3);

            // A normal target carries no panels.
            TargetReport normal = Assert.Single(report.Targets, t => t.DirectoryName == "M1 - Crab");
            Assert.Empty(normal.Panels);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanUnitsAsync_Normal_ReturnsExactlyOneUnit_WithItsCells()
    {
        string root = Path.Combine(Path.GetTempPath(), "tcm_units_" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "M1 - Crab");
        WriteFrame(Path.Combine(target, "Captures", "Z183", "H"), "a.xisf", ra: 83.6, dec: 22.0, bin: 1);
        WriteFrame(Path.Combine(target, "Captures", "Z183", "O"), "b.xisf", ra: 83.6, dec: 22.0, bin: 1);
        try
        {
            IReadOnlyList<TargetReport> units = await ImageLibraryScanner.ScanUnitsAsync(target, TestContext.Current.CancellationToken);

            TargetReport u = Assert.Single(units);
            Assert.Equal("M1 - Crab", u.DirectoryName);
            Assert.Equal(2, u.Filters.Count);   // H and O cells on the one unit
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanUnitsAsync_NonexistentDir_Throws()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => ImageLibraryScanner.ScanUnitsAsync(@"Q:\definitely\does\not\exist", TestContext.Current.CancellationToken));
    }

    // ---- calibration is excluded from the scan -----------------------------------------------------------
    // Long-standing behaviour that had no test until the scan's exclusions were specified.

    [Fact]
    public async Task ScanAsync_CalibrationTree_IsNotReadAsLight()
    {
        string root = NewRoot();
        string captures = Path.Combine(root, "M81 - Bode", "Captures");
        WriteConfiguredFrame(Path.Combine(captures, "Z183", "H"), "light.xisf", "GAIN", "111");
        // Masters sit under Captures/Calibration — counting them would inflate every reported count.
        WriteConfiguredFrame(Path.Combine(captures, "Calibration"), "dark1.xisf", "GAIN", "111");
        WriteConfiguredFrame(Path.Combine(captures, "Calibration"), "dark2.xisf", "GAIN", "111");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);

            FilterAggregate agg = Assert.Single(report.Targets.Single().Filters);
            Assert.Equal(1, agg.ExposureCount);            // the light only
            Assert.Equal("Z183", agg.Camera);              // never "Calibration"
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanAsync_TargetWithOnlyCalibration_YieldsNothing()
    {
        string root = NewRoot();
        WriteConfiguredFrame(
            Path.Combine(root, "M81 - Bode", "Captures", "Calibration"), "dark.xisf", "GAIN", "111");
        try
        {
            Assert.Empty((await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken)).Targets);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- non-sidereal targets are excluded from the scan -------------------------------------------------

    [Theory]
    [InlineData("Comet C2023 A3 - Tsuchinshan")]
    [InlineData("Comet 46P - Wirtanen")]
    [InlineData("Comet C2022 E3 (ZTF)")]
    [InlineData("comet c2020 f3 - neowise")]   // case-insensitive
    public void IsNonSiderealDirectory_MatchesCometNaming(string dirName) =>
        Assert.True(ImageLibraryScanner.IsNonSiderealDirectory(dirName));

    [Theory]
    // The trailing space in the prefix is load-bearing: a sidereal object whose name merely begins with
    // those letters must still be scanned.
    [InlineData("Cometary Globule CG4")]
    [InlineData("NGC 2261 - Comet Nebula")]
    [InlineData("M51 - Whirlpool")]
    [InlineData("Comet")]
    [InlineData("")]
    public void IsNonSiderealDirectory_DoesNotOverMatch(string dirName) =>
        Assert.False(ImageLibraryScanner.IsNonSiderealDirectory(dirName));

    [Fact]
    public async Task ScanAsync_CometTarget_IsNotScanned()
    {
        string root = NewRoot();
        // A comet beside a normal target, both validly populated.
        WriteConfiguredFrame(
            Path.Combine(root, "Comet C2023 A3 - Tsuchinshan", "Captures", "Z183", "2024-10-18 - Track Comet"),
            "c.xisf", "GAIN", "111");
        WriteConfiguredFrame(Path.Combine(root, "M81 - Bode", "Captures", "Z183", "H"), "m.xisf", "GAIN", "111");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);

            // The comet is absent entirely — and with it the session-folder-as-filter-code it would publish.
            TargetReport only = Assert.Single(report.Targets);
            Assert.Equal("M81 - Bode", only.DirectoryName);
            Assert.DoesNotContain(only.Filters, f => f.FilterCode.Contains("Track Comet"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanUnitsAsync_PointedAtAComet_ReturnsNothing()
    {
        string root = NewRoot();
        string comet = Path.Combine(root, "Comet C2023 A3 - Tsuchinshan");
        WriteConfiguredFrame(Path.Combine(comet, "Captures", "Z183", "2024-10-18 - Track Comet"),
            "c.xisf", "GAIN", "111");
        try
        {
            // The surgical entry point honours the exclusion too — both paths funnel through one guard.
            Assert.Empty(await ImageLibraryScanner.ScanUnitsAsync(comet, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- capture configuration is the aggregate identity ------------------------------------------------
    // Frames differing in any dimension that prevents them combining into one integration must land in
    // separate aggregates; frames sharing every dimension must stay one.

    [Theory]
    [InlineData("GAIN", "53", "0")]         // the 2024 broadband switch: two eras, two stacks
    [InlineData("OFFSET", "10", "50")]      // observed scattered through every filter
    [InlineData("XBINNING", "1", "2")]      // bin 1 and bin 2 frames do not stack
    public async Task ScanAsync_ConfigurationDifference_SeparatesAggregates(string keyword, string a, string b)
    {
        string root = NewRoot();
        string filterDir = Path.Combine(root, "M81 - Bode", "Captures", "Z183", "H");
        WriteConfiguredFrame(filterDir, "one.xisf", keyword, a);
        WriteConfiguredFrame(filterDir, "two.xisf", keyword, b);
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);
            IReadOnlyList<FilterAggregate> aggs = report.Targets.Single().Filters;

            Assert.Equal(2, aggs.Count);
            Assert.All(aggs, agg => Assert.Equal(1, agg.ExposureCount));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanAsync_DifferentCameras_SeparateAggregates()
    {
        string root = NewRoot();
        string target = Path.Combine(root, "M81 - Bode", "Captures");
        WriteConfiguredFrame(Path.Combine(target, "Z183", "L"), "one.xisf", "GAIN", "53");
        WriteConfiguredFrame(Path.Combine(target, "Z533", "L"), "two.xisf", "GAIN", "53");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);
            IReadOnlyList<FilterAggregate> aggs = report.Targets.Single().Filters;

            Assert.Equal(2, aggs.Count);
            Assert.Equal(["Z183", "Z533"], aggs.Select(a => a.Camera).OrderBy(c => c).ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanAsync_IdenticalConfiguration_StaysOneAggregate()
    {
        string root = NewRoot();
        string filterDir = Path.Combine(root, "M81 - Bode", "Captures", "Z183", "H");
        WriteConfiguredFrame(filterDir, "one.xisf", "GAIN", "111");
        WriteConfiguredFrame(filterDir, "two.xisf", "GAIN", "111");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);
            FilterAggregate agg = report.Targets.Single().Filters.Single();

            Assert.Equal(2, agg.ExposureCount);
            Assert.Equal("Z183", agg.Camera);
            Assert.False(agg.CameraDisagrees);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanAsync_FrameRecordingAnotherCamera_FlagsTheDisagreement()
    {
        string root = NewRoot();
        string filterDir = Path.Combine(root, "M81 - Bode", "Captures", "Z183", "H");
        // Filed under Z183 but the frame itself says Z533 — filed under the wrong camera.
        WriteConfiguredFrame(filterDir, "wrong.xisf", "GAIN", "111", instrume: "Z533");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);
            FilterAggregate agg = report.Targets.Single().Filters.Single();

            Assert.Equal("Z183", agg.Camera);       // the directory stays authoritative
            Assert.True(agg.CameraDisagrees);       // and the disagreement is reported, not reconciled
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ScanAsync_OffsetIsReadAsRecorded()
    {
        string root = NewRoot();
        string filterDir = Path.Combine(root, "M81 - Bode", "Captures", "Z183", "H");
        // A Z183 frame recording offset 10: it must stay 10, not become 2 by a per-camera divisor.
        WriteConfiguredFrame(filterDir, "one.xisf", "OFFSET", "10");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root, TestContext.Current.CancellationToken);
            Assert.Equal(10, report.Targets.Single().Filters.Single().Typical.Offset);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "tsm_cfg_" + Guid.NewGuid().ToString("N"));

    // A frame with a full, valid capture configuration, with one keyword overridden per call.
    private static void WriteConfiguredFrame(
        string filterDir, string file, string keyword, string value, string instrume = "Z183")
    {
        Directory.CreateDirectory(filterDir);
        Dictionary<string, string> kw = new()
        {
            ["OBJECT"] = "M81",
            ["RA"] = "148.9",
            ["DEC"] = "69.2",
            ["DATE-OBS"] = "2024-02-18T04:51:28",
            ["EXPTIME"] = "300.0",
            ["GAIN"] = "111",
            ["OFFSET"] = "10",
            ["XBINNING"] = "1",
            ["YBINNING"] = "1",
            ["INSTRUME"] = instrume,
        };
        kw[keyword] = value;
        if (keyword == "XBINNING") kw["YBINNING"] = value;   // binning moves as a pair
        WriteSyntheticXisf(Path.Combine(filterDir, file), kw);
    }

    private static string Slash(string p) => p.Replace('\\', '/');

    // ---- synthetic XISF frame writers (header-only; mirrors Astronomy.XISF.Tests) --------------------------

    private static void WritePanelFrame(
        string mosaicDir, string camera, string panel, string filter, string file, double ra, double dec, int bin) =>
        WriteFrame(Path.Combine(mosaicDir, "Captures", camera, panel, filter), file, ra, dec, bin);

    private static void WriteFrame(string filterDir, string file, double ra, double dec, int bin)
    {
        Directory.CreateDirectory(filterDir);
        WriteSyntheticXisf(Path.Combine(filterDir, file), new Dictionary<string, string>
        {
            ["OBJECT"] = "Demo",
            ["RA"] = ra.ToString(CultureInfo.InvariantCulture),
            ["DEC"] = dec.ToString(CultureInfo.InvariantCulture),
            ["DATE-OBS"] = "2024-02-18T04:51:28",
            ["EXPTIME"] = "300.0",
            ["XBINNING"] = bin.ToString(CultureInfo.InvariantCulture),
            ["YBINNING"] = bin.ToString(CultureInfo.InvariantCulture),
        });
    }

    // Minimal valid XISF: 8-byte signature + 4-byte LE XML length + 4 reserved + UTF-8 XML (no image
    // attachment). The geometry attribute is mandatory in the XISF spec and the reader treats it as a
    // contract, so a fixture without one is a corrupt file and never reaches an aggregate.
    private static void WriteSyntheticXisf(
        string path, IDictionary<string, string> fitsKeywords, string geometry = "5496:3672:1")
    {
        StringBuilder xml = new();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
        xml.Append($"<Image geometry=\"{geometry}\">");
        foreach (KeyValuePair<string, string> kv in fitsKeywords)
        {
            string val = double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? kv.Value : $"'{kv.Value}'";
            xml.Append($"<FITSKeyword name=\"{kv.Key}\" value=\"{val}\" comment=\"\" />");
        }
        xml.Append("</Image>");
        xml.Append("</xisf>");

        byte[] xmlBytes = Encoding.UTF8.GetBytes(xml.ToString());
        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
        int len = xmlBytes.Length;
        header[8] = (byte)(len & 0xFF);
        header[9] = (byte)((len >> 8) & 0xFF);
        header[10] = (byte)((len >> 16) & 0xFF);
        header[11] = (byte)((len >> 24) & 0xFF);

        using FileStream fs = new(path, FileMode.Create, FileAccess.Write);
        fs.Write(header, 0, 16);
        fs.Write(xmlBytes, 0, xmlBytes.Length);
    }
}
