using Astronomy.Diagnostics;
using Xunit;

namespace Astronomy.Diagnostics.Tests;

// Smoke coverage for the Windows capture backend. ToPng's contract is best-effort (path on success,
// null on ANY failure, never a throw), and a locked or headless session can legitimately fail the
// grab — so these tests assert the contract, not a successful screenshot: no exception escapes, and
// IF a path comes back the PNG really landed. Resolves the design.md open question in favor of
// having the encode + folder-creation path exercised on every test run.
public sealed class ScreenCaptureSmokeTests : IDisposable
{
    private readonly string mRoot = Path.Combine(Path.GetTempPath(), "sc-smoke-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(mRoot, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    [InlineData(-1, 8)]
    public void ToPng_NonPositiveSize_ReturnsNull(int width, int height)
    {
        Assert.Null(ScreenCapture.ToPng(0, 0, width, height, Path.Combine(mRoot, "never.png")));
        Assert.False(File.Exists(Path.Combine(mRoot, "never.png")));
    }

    [Fact]
    public void ToPng_BestEffort_NeverThrows_And_WritesPngWhenItReportsSuccess()
    {
        string path = Path.Combine(mRoot, "nested", "grab.png");   // nested: exercises folder creation

        string result = ScreenCapture.ToPng(0, 0, 8, 8, path);     // must not throw, whatever the session state

        if (result != null)
        {
            Assert.Equal(path, result);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }
    }
}
