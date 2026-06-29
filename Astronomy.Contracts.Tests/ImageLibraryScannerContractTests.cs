using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract test for the scanner input/path precondition — CONSUMERS.md
/// "Semantic assumptions" #14 (input / path &amp; process-global class).
/// </summary>
public sealed class ImageLibraryScannerContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #14:
    //   "Scan.ImageLibraryScanner.ScanAsync(root) expects <target>/Captures/<Camera>/<Filter>/;
    //    missing root throws DirectoryNotFoundException."
    // TSM hands ScanAsync a user-chosen library root. A non-existent root is a loud
    // caller error, not an empty result: the scanner must throw DirectoryNotFoundException
    // (NOT return an empty ImageLibraryReport, which would masquerade as "library is empty").
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ScanAsync_MissingRoot_ThrowsDirectoryNotFound()
    {
        // A path guaranteed not to exist (unique temp name, never created).
        string missingRoot = Path.Combine(Path.GetTempPath(), $"astro_no_such_library_{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(missingRoot));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => ImageLibraryScanner.ScanAsync(missingRoot));
    }
}
