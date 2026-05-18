using Astronomy.NINA;
using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests.Xisf;

public class ReportToTargetAdapterTests
{
    private static FilterAggregate MakeAgg(string code, FilterPurpose purpose, int count, double exptime)
    {
        DateTime first = new(2024, 2, 18, 4, 0, 0, DateTimeKind.Utc);
        DateTime last = new(2024, 2, 18, 5, 0, 0, DateTimeKind.Utc);
        return new FilterAggregate(
            filterName: ImageLibraryScanner.NormalizeFilterName(code),
            filterCode: code,
            purpose: purpose,
            exposureCount: count,
            totalIntegration: TimeSpan.FromSeconds(count * exptime),
            firstImagedUtc: first,
            lastImagedUtc: last,
            typical: new TypicalSettings(gain: 111, offset: 2, setTempC: -20.0, binning: (1, 1), exposureSec: exptime),
            camerasSeen: new[] { "Z183" });
    }

    private static TargetReport MakeReport(string dirName, double raHours, double decDeg, params FilterAggregate[] aggs)
    {
        var (catalog, common) = TargetReport.SplitDirectoryName(dirName);
        return new TargetReport(
            directoryName: dirName,
            catalog: catalog,
            commonName: common,
            objectName: catalog,
            raHours: raHours,
            decDegrees: decDeg,
            filters: aggs);
    }

    [Theory]
    [InlineData("L", "L", FilterKind.Luminance)]
    [InlineData("H", "Ha", FilterKind.Narrowband)]
    [InlineData("O", "OIII", FilterKind.Narrowband)]
    [InlineData("S", "SII", FilterKind.Narrowband)]
    [InlineData("R", "R", FilterKind.Broadband)]
    [InlineData("G", "G", FilterKind.Broadband)]
    [InlineData("B", "B", FilterKind.Broadband)]
    public void FilterFromCode_MapsStandardCodesToPresets(string code, string expectedName, FilterKind expectedKind)
    {
        Filter f = ReportToTargetAdapter.FilterFromCode(code);
        Assert.Equal(expectedName, f.Name);
        Assert.Equal(expectedKind, f.Kind);
    }

    [Fact]
    public void FilterFromCode_CustomCodesPassThroughAsUnknown()
    {
        Filter f = ReportToTargetAdapter.FilterFromCode("Custom-NB");
        Assert.Equal("Custom-NB", f.Name);
        Assert.Equal(FilterKind.Unknown, f.Kind);
    }

    [Fact]
    public void ToTarget_PopulatesGeometryAndImagingHistory()
    {
        TargetReport r = MakeReport("M51 - Whirlpool", raHours: 13.498, decDeg: 47.195,
            MakeAgg("L", FilterPurpose.Light, 100, 600),
            MakeAgg("H", FilterPurpose.Light, 30, 600));

        Astronomy.NINA.Target t = r.ToTarget();

        Assert.Equal("M51", t.Name);
        Assert.Equal("M51", t.Geometry.Name);       // Geometry uses OBJECT keyword (== Catalog in this synthetic case)
        Assert.Equal(13.498, t.Geometry.RightAscension);
        Assert.Equal(47.195, t.Geometry.Declination);
        Assert.True(t.Geometry.North);
        Assert.Equal("M51 - Whirlpool", t.Geometry.Directory);

        Assert.Equal(2, t.ImagingHistory.Count);
        Assert.Contains(t.ImagingHistory, h => h.Filter.Name == "L" && h.ExposureCount == 100);
        Assert.Contains(t.ImagingHistory, h => h.Filter.Name == "Ha" && h.ExposureCount == 30);
        Assert.Null(t.PlannedExposures);
    }

    [Fact]
    public void ToTarget_SouthernDecFlipsNorthFlag()
    {
        // Astronomy.Core.Targets.Target ctor normalizes negative dec to (positive magnitude, North=false).
        TargetReport r = MakeReport("Some Southern Target", raHours: 6.0, decDeg: -42.5,
            MakeAgg("L", FilterPurpose.Light, 10, 600));
        Astronomy.NINA.Target t = r.ToTarget();
        Assert.Equal(42.5, t.Geometry.Declination);
        Assert.False(t.Geometry.North);
    }

    [Fact]
    public void ToTarget_PreservesFilterPurpose()
    {
        TargetReport r = MakeReport("M51 - Whirlpool", raHours: 13.5, decDeg: 47.2,
            MakeAgg("B", FilterPurpose.Light, 50, 300),
            MakeAgg("B", FilterPurpose.Stars, 30, 30));
        Astronomy.NINA.Target t = r.ToTarget();
        Assert.Equal(2, t.ImagingHistory.Count);
        Assert.Single(t.ImagingHistory, h => h.Filter.Name == "B" && h.Purpose == FilterPurpose.Light);
        Assert.Single(t.ImagingHistory, h => h.Filter.Name == "B" && h.Purpose == FilterPurpose.Stars);
    }

    [Fact]
    public void ToTarget_FilterHistoryCarriesTypicalSettings()
    {
        TargetReport r = MakeReport("M51 - Whirlpool", raHours: 13.5, decDeg: 47.2,
            MakeAgg("H", FilterPurpose.Light, 30, 600));
        Astronomy.NINA.Target t = r.ToTarget();
        FilterHistory h = t.ImagingHistory[0];
        Assert.Equal(111, h.TypicalSettings.Gain);
        Assert.Equal(2, h.TypicalSettings.Offset);
        Assert.Equal(-20.0, h.TypicalSettings.SetTempC);
        Assert.Equal((1, 1), h.TypicalSettings.Binning);
        Assert.Equal(600.0, h.TypicalSettings.ExposureSec);
    }

    [Fact]
    public void ToTargets_ConvertsEntireReport()
    {
        ImageLibraryReport report = new(
            libraryRoot: @"C:\fake",
            scannedAtUtc: DateTime.UtcNow,
            targets: new[] {
                MakeReport("M51 - Whirlpool", 13.5, 47.2, MakeAgg("L", FilterPurpose.Light, 100, 600)),
                MakeReport("M42 - Orion", 5.6, -5.4, MakeAgg("L", FilterPurpose.Light, 50, 60)),
            },
            skippedFiles: new Dictionary<string, string>());

        IReadOnlyList<Astronomy.NINA.Target> targets = report.ToTargets();
        Assert.Equal(2, targets.Count);
        Assert.Equal("M51", targets[0].Name);
        Assert.Equal("M42", targets[1].Name);
        Assert.False(targets[1].Geometry.North);
    }

    [Fact]
    public void ToTargets_NullReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((ImageLibraryReport)null).ToTargets());
    }
}
