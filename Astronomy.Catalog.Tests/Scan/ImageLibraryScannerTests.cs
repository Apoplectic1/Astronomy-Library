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
            () => ImageLibraryScanner.ScanAsync(@"Q:\definitely\does\not\exist"));
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
        await File.WriteAllTextAsync(mosaicFrame, "not xisf");      // invalid header → recorded in SkippedFiles
        await File.WriteAllTextAsync(standardFrame, "not xisf");
        try
        {
            ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root);

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

    private static string Slash(string p) => p.Replace('\\', '/');
}
