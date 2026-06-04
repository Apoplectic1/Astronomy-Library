using System.Collections.Concurrent;
using Astronomy.XISF;

namespace Astronomy.Catalog.Scan;

/// <summary>
/// Scans a user-disciplined image library (per-target top-level directories,
/// each with a <c>Captures/&lt;Camera&gt;/&lt;Filter&gt;/</c> tree) and produces an
/// <see cref="ImageLibraryReport"/> with per-target / per-filter aggregates.
/// </summary>
/// <remarks>
/// <para>
/// Convention assumed (matches Dan's library at <c>E:\Photography\Astro Photography\Processing\</c>):
/// <code>
/// LibraryRoot/
/// └─ &lt;Catalog&gt; - &lt;Common&gt;/              one target per dir; e.g. "M51 - Whirlpool"
///    ├─ Captures/                          ONLY this matters for scanning
///    │  ├─ Calibration/                    skipped entirely (bias/dark/flat masters)
///    │  └─ &lt;Camera&gt;/                       e.g. Z183, Z533
///    │     ├─ &lt;Filter&gt;/                    B, G, H, L, R, O, S — light frames
///    │     └─ Stars &lt;Filter&gt;/              short-exposure star-only frames
///    └─ &lt;Camera&gt; - &lt;Filter&gt; - N, H.h       XFM marker FILES (not dirs); ignored
/// </code>
/// </para>
/// <para>
/// Per-target scans run in parallel. .xisf header parsing failures are recorded
/// in <see cref="ImageLibraryReport.SkippedFiles"/> rather than aborting the scan.
/// </para>
/// </remarks>
public static class ImageLibraryScanner
{
    /// <summary>
    /// Scans <paramref name="libraryRoot"/> and returns a populated
    /// <see cref="ImageLibraryReport"/>.
    /// </summary>
    /// <param name="libraryRoot">Absolute path to the image library root directory.</param>
    /// <param name="ct">Cancellation token; observed at file-I/O boundaries.</param>
    public static async Task<ImageLibraryReport> ScanAsync(
        string libraryRoot, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        if (!Directory.Exists(libraryRoot))
        {
            throw new DirectoryNotFoundException($"Image library root not found: '{libraryRoot}'.");
        }

        ConcurrentDictionary<string, string> skipped = new(StringComparer.OrdinalIgnoreCase);
        ConcurrentBag<TargetReport> targets = new();

        IEnumerable<string> targetDirs = Directory.EnumerateDirectories(libraryRoot);
        await Parallel.ForEachAsync(targetDirs, ct, async (targetDir, token) =>
        {
            TargetReport? report = await ScanTargetAsync(targetDir, skipped, token).ConfigureAwait(false);
            if (report is not null)
            {
                targets.Add(report);
            }
        }).ConfigureAwait(false);

        List<TargetReport> sorted = targets
            .OrderBy(t => t.DirectoryName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ImageLibraryReport(
            libraryRoot,
            DateTime.UtcNow,
            sorted,
            skipped.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------

    private sealed record FrameReading(
        XisfHeader Header,
        string FilterCode,
        FilterPurpose Purpose,
        string CameraDirName);

    private static async Task<TargetReport?> ScanTargetAsync(
        string targetDir,
        ConcurrentDictionary<string, string> skipped,
        CancellationToken ct)
    {
        string dirName = Path.GetFileName(targetDir);
        string capturesDir = Path.Combine(targetDir, "Captures");
        if (!Directory.Exists(capturesDir)) return null;

        List<FrameReading> readings = new();

        foreach (string cameraDir in Directory.EnumerateDirectories(capturesDir))
        {
            string cameraName = Path.GetFileName(cameraDir);
            // Skip Calibration/ — holds master bias/dark/flat frames, not light captures.
            if (string.Equals(cameraName, "Calibration", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string filterDir in Directory.EnumerateDirectories(cameraDir))
            {
                ct.ThrowIfCancellationRequested();
                (string code, FilterPurpose purpose) = ParseFilterDirName(Path.GetFileName(filterDir));

                foreach (string xisfPath in Directory.EnumerateFiles(filterDir, "*.xisf", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        XisfHeader header = await XisfHeaderReader.ReadAsync(xisfPath, ct).ConfigureAwait(false);
                        readings.Add(new FrameReading(header, code, purpose, cameraName));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        skipped.TryAdd(xisfPath, ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        if (readings.Count == 0) return null;

        (string catalog, string? commonName) = TargetReport.SplitDirectoryName(dirName);
        string objectName = ConsensusObjectName(readings, catalog);
        (double raHours, double decDegrees) = ConsensusCoordinates(readings);

        List<FilterAggregate> aggregates = readings
            .GroupBy(r => (r.FilterCode, r.Purpose))
            .Select(g => BuildAggregate(g.Key.FilterCode, g.Key.Purpose, g.ToList()))
            .Where(a => a is not null)
            .Cast<FilterAggregate>()
            .OrderBy(a => a.FilterCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Purpose)
            .ToList();

        if (aggregates.Count == 0) return null;

        return new TargetReport(dirName, catalog, commonName, objectName, raHours, decDegrees, aggregates);
    }

    // -----------------------------------------------------------------------
    // Dir-name parsing + filter normalization
    // -----------------------------------------------------------------------

    /// <summary>
    /// Splits a filter directory name into <c>(code, purpose)</c>. Examples:
    /// <c>"B"</c> → <c>("B", Light)</c>; <c>"Stars B"</c> → <c>("B", Stars)</c>.
    /// </summary>
    internal static (string Code, FilterPurpose Purpose) ParseFilterDirName(string dirName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dirName);
        const string starsPrefix = "Stars ";
        if (dirName.StartsWith(starsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (dirName[starsPrefix.Length..].Trim(), FilterPurpose.Stars);
        }
        return (dirName.Trim(), FilterPurpose.Light);
    }

    /// <summary>
    /// Normalize a filter directory-name to canonical form. The canonical set is
    /// single-letter ("H", "O", "S", "L", "R", "G", "B") matching the standard
    /// filter presets (Astronomy.NINA.Filter) and TargetPlanner's FilterLibrary.
    /// Unrecognized codes pass through unchanged (custom filters keep their
    /// dir-name).
    /// </summary>
    public static string NormalizeFilterName(string code) => code switch
    {
        "L" => "L",
        "H" => "H",
        "O" => "O",
        "S" => "S",
        "R" => "R",
        "G" => "G",
        "B" => "B",
        _ => code,
    };

    // -----------------------------------------------------------------------
    // Aggregation
    // -----------------------------------------------------------------------

    private static FilterAggregate? BuildAggregate(
        string filterCode, FilterPurpose purpose, IReadOnlyList<FrameReading> frames)
    {
        if (frames.Count == 0) return null;

        // EXPTIME-bearing frames only — without it we can't compute integration or typical exposure.
        List<FrameReading> withExp = frames.Where(f => f.Header.ExposureSec is > 0).ToList();
        if (withExp.Count == 0) return null;

        int count = withExp.Count;
        double totalSec = withExp.Sum(f => f.Header.ExposureSec!.Value);
        TimeSpan total = TimeSpan.FromSeconds(totalSec);

        // First/last imaged — DATE-OBS bearing frames only. If none have it, fall
        // back to TimeSpan.Zero / DateTime.MinValue won't satisfy ctor invariants,
        // so refuse the aggregate. Caller-side this means an aggregate is published
        // only when at least one frame had DATE-OBS.
        List<DateTime> dates = withExp
            .Select(f => f.Header.DateObsUtc)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(d => d)
            .ToList();
        if (dates.Count == 0) return null;

        TypicalSettings typical = ComputeTypical(withExp);
        IReadOnlyList<string> cameras = withExp
            .Select(f => f.Header.Instrument ?? f.CameraDirName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FilterAggregate(
            filterName: NormalizeFilterName(filterCode),
            filterCode: filterCode,
            purpose: purpose,
            exposureCount: count,
            totalIntegration: total,
            firstImagedUtc: dates[0],
            lastImagedUtc: dates[^1],
            typical: typical,
            camerasSeen: cameras);
    }

    private static TypicalSettings ComputeTypical(IReadOnlyList<FrameReading> frames)
    {
        int gain = Mode(frames.Select(f => f.Header.Gain ?? 0));
        int offset = Mode(frames.Select(f => f.Header.OffsetNormalized ?? f.Header.OffsetRaw ?? 0));
        double setTemp = ModeDouble(frames.Select(f => f.Header.SetTempC ?? 0.0));
        int xBin = Mode(frames.Select(f => f.Header.XBinning ?? 1));
        int yBin = Mode(frames.Select(f => f.Header.YBinning ?? 1));
        // Cluster exposure to nearest second so 599.97 and 600.00 share a bucket.
        double exposureSec = ModeDouble(
            frames.Select(f => Math.Round(f.Header.ExposureSec ?? 0.0)));

        return new TypicalSettings(
            gain: gain,
            offset: offset,
            setTempC: setTemp,
            binning: (Math.Max(1, xBin), Math.Max(1, yBin)),
            exposureSec: exposureSec > 0 ? exposureSec : 1.0);  // ctor requires > 0
    }

    private static int Mode(IEnumerable<int> values)
    {
        var counts = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0) return 0;
        return counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
    }

    private static double ModeDouble(IEnumerable<double> values)
    {
        var counts = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0) return 0.0;
        return counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
    }

    // -----------------------------------------------------------------------
    // Coordinate consensus + OBJECT-name consensus
    // -----------------------------------------------------------------------

    private static (double RaHours, double DecDegrees) ConsensusCoordinates(
        IReadOnlyList<FrameReading> readings)
    {
        // FITS RA is decimal degrees; AL convention is decimal hours. Convert at boundary.
        List<double> ras = readings
            .Select(r => r.Header.RaDegrees)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();
        List<double> decs = readings
            .Select(r => r.Header.DecDegrees)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        // Median is more robust than mode for noisy float coords. Fallback (0, 0)
        // if no frames carried coords — caller can sanity-check downstream.
        double raDeg = Median(ras);
        double decDeg = Median(decs);

        double raHours = (raDeg / 15.0) % 24.0;
        if (raHours < 0) raHours += 24.0;
        // Clamp DEC into [-90, 90] in case of slight wrap from sensor reporting noise.
        decDeg = Math.Clamp(decDeg, -90.0, 90.0);

        return (raHours, decDeg);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        double[] sorted = values.OrderBy(d => d).ToArray();
        int mid = sorted.Length / 2;
        return (sorted.Length & 1) == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static string ConsensusObjectName(
        IReadOnlyList<FrameReading> readings, string fallback)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (FrameReading r in readings)
        {
            string? name = r.Header.ObjectName;
            if (string.IsNullOrWhiteSpace(name)) continue;
            counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
        }
        if (counts.Count == 0) return fallback;
        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }
}
