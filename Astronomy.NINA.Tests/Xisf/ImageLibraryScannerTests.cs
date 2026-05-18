using Astronomy.NINA.Xisf;
using Xunit;

namespace Astronomy.NINA.Tests.Xisf;

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
    [InlineData("L", "Luminance")]
    [InlineData("H", "Ha")]
    [InlineData("O", "OIII")]
    [InlineData("S", "SII")]
    [InlineData("R", "Red")]
    [InlineData("G", "Green")]
    [InlineData("B", "Blue")]
    [InlineData("Custom", "Custom")]    // unknown passes through
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
}
