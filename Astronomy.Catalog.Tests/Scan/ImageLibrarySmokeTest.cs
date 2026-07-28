#nullable disable // moved from Astronomy.NINA.Tests (Nullable-disable convention)
using System.Globalization;
using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Catalog.Tests.Scan;

/// <summary>
/// Live smoke test that scans the user's real image library and dumps a summary.
/// Runs only when <c>TP_SMOKE_IMAGE_LIBRARY</c> env var points at a valid directory;
/// otherwise the test is a silent no-op so plain <c>dotnet test</c> stays portable.
/// </summary>
public class ImageLibrarySmokeTest
{
    private readonly ITestOutputHelper mOut;

    public ImageLibrarySmokeTest(ITestOutputHelper output) => mOut = output;

    [Fact]
    public async Task ScanRealLibrary_PrintsReport()
    {
        string root = Environment.GetEnvironmentVariable("TP_SMOKE_IMAGE_LIBRARY");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            mOut.WriteLine("Skip: TP_SMOKE_IMAGE_LIBRARY env var not set or directory missing.");
            return;
        }

        mOut.WriteLine($"Scanning: {root}");
        DateTime t0 = DateTime.UtcNow;
        ImageLibraryReport report = await ImageLibraryScanner.ScanAsync(root);
        TimeSpan dt = DateTime.UtcNow - t0;

        mOut.WriteLine($"Scanned in {dt.TotalSeconds:F1}s.");
        mOut.WriteLine($"Targets found: {report.Targets.Count}");
        mOut.WriteLine($"Files skipped (parse failures): {report.SkippedFiles.Count}");
        mOut.WriteLine("");

        int totalFrames = 0;
        TimeSpan totalIntegration = TimeSpan.Zero;
        foreach (TargetReport t in report.Targets)
        {
            int targetFrames = t.Filters.Sum(f => f.ExposureCount);
            TimeSpan targetIntegration = t.Filters.Aggregate(TimeSpan.Zero, (acc, f) => acc + f.TotalIntegration);
            totalFrames += targetFrames;
            totalIntegration += targetIntegration;

            mOut.WriteLine($"{t.DirectoryName}  (OBJECT={t.ObjectName}, RA={t.RaHours:F3}h, DEC={t.DecDegrees:+0.000;-0.000}°)");
            foreach (FilterAggregate f in t.Filters)
            {
                string p = f.Purpose == FilterPurpose.Stars ? " (Stars)" : "";
                mOut.WriteLine($"    {f.FilterCode,2} {f.FilterName,-9}{p}  {f.ExposureCount,4}× × {f.Typical.ExposureSec,4:F0}s  = {f.TotalIntegration.TotalHours,5:F1}h  gain {f.Typical.Gain,3}  offset {f.Typical.Offset,3}  {f.Typical.SetTempC,5:F1}°C  bin {f.Typical.Binning.X}x{f.Typical.Binning.Y}  cam: {f.Camera}{(f.CameraDisagrees ? " (cam≠)" : "")}");
            }
        }

        mOut.WriteLine("");
        mOut.WriteLine($"TOTAL: {report.Targets.Count} targets, {totalFrames} frames, {totalIntegration.TotalHours.ToString("F1", CultureInfo.InvariantCulture)}h integration");

        if (report.SkippedFiles.Count > 0)
        {
            mOut.WriteLine("");
            mOut.WriteLine("Skipped files:");
            foreach (KeyValuePair<string, string> kv in report.SkippedFiles.Take(20))
            {
                mOut.WriteLine($"  {kv.Key}: {kv.Value}");
            }
        }

        Assert.NotEmpty(report.Targets);
    }
}
