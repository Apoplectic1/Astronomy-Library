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
/// ├─ Comet &lt;designation&gt;/                skipped entirely — non-sidereal (see IsNonSiderealDirectory)
/// └─ &lt;Catalog&gt; - &lt;Common&gt;/              one target per dir; e.g. "M51 - Whirlpool"
///    ├─ Captures/                          ONLY this matters for scanning
///    │  ├─ Calibration/                    skipped entirely (bias/dark/flat masters)
///    │  └─ &lt;Camera&gt;/                       e.g. Z183, Z533
///    │     ├─ &lt;Filter&gt;/                    B, G, H, L, R, O, S — light frames
///    │     └─ Stars &lt;Filter&gt;/              short-exposure star-only frames
///    └─ &lt;Camera&gt; - &lt;Filter&gt; - N, H.h       XFM marker FILES (not dirs); ignored
/// </code>
/// A <c>Mosaic - &lt;Name&gt;</c> target nests one extra panel level under the camera
/// (<c>.../&lt;Camera&gt;/&lt;panel&gt;/&lt;Filter&gt;/</c>); the whole-target aggregate sums all panels AND each
/// panel is retained as its own sub-report (<see cref="TargetReport.Panels"/>) with its own centroid.
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

    /// <summary>
    /// Scans a <b>single</b> target directory into its write-back <i>units</i> — the granularity at which counts
    /// anchor to a TS target. A normal target is one unit (one <see cref="TargetReport"/>, the whole-target
    /// aggregate); a <see cref="MosaicConvention">mosaic</see> is one unit <b>per panel</b> (each panel's own
    /// per-filter aggregates and its own plate-solved centroid), so a panel's counts can land on that panel's TS
    /// plan rather than the mosaic aggregate. Unlike <see cref="ScanAsync"/> this does not rebuild the catalog and
    /// surfaces no <see cref="ImageLibraryReport.SkippedFiles"/> — it is the surgical per-target path.
    /// </summary>
    /// <param name="targetDir">Absolute path to one target directory under the library root (normal or <c>Mosaic - …</c>).</param>
    /// <param name="ct">Cancellation token; observed at file-I/O boundaries.</param>
    /// <returns>The target's units; empty when the directory has no usable frames.</returns>
    public static async Task<IReadOnlyList<TargetReport>> ScanUnitsAsync(
        string targetDir, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        targetDir = Path.TrimEndingDirectorySeparator(targetDir);
        if (!Directory.Exists(targetDir))
            throw new DirectoryNotFoundException($"Target directory not found: '{targetDir}'.");

        // A surgical scan reports nothing globally; bad frames are simply absent from the aggregate.
        ConcurrentDictionary<string, string> skipped = new(StringComparer.OrdinalIgnoreCase);

        if (!MosaicConvention.IsMosaicDirectory(Path.GetFileName(targetDir)))
        {
            // Normal target: a single unit — the whole-target aggregate the bulk path also produces.
            TargetReport? one = await ScanTargetAsync(targetDir, skipped, ct).ConfigureAwait(false);
            return one is null ? [] : [one];
        }

        // Mosaic: one unit per panel. Frames live at Captures/<camera>/<panel>/<filter>/; group by panel name across
        // cameras so a panel shot on two rigs stays one unit.
        string capturesDir = Path.Combine(targetDir, "Captures");
        if (!Directory.Exists(capturesDir)) return [];

        Dictionary<string, List<FrameReading>> byPanel =
            await ReadMosaicPanelsAsync(capturesDir, skipped, ct).ConfigureAwait(false);

        List<TargetReport> units = [];
        foreach ((string panelName, List<FrameReading> readings) in
                 byPanel.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            TargetReport? unit = BuildReport(panelName, readings);
            if (unit is not null) units.Add(unit);
        }
        return units;
    }

    // The mosaic panel walk: frames grouped by panel directory name across cameras (a panel shot on two rigs
    // stays one group), Calibration/ skipped. Shared by the bulk scan (which also retains the groups as
    // per-panel sub-reports) and the surgical per-unit scan.
    private static async Task<Dictionary<string, List<FrameReading>>> ReadMosaicPanelsAsync(
        string capturesDir,
        ConcurrentDictionary<string, string> skipped,
        CancellationToken ct)
    {
        Dictionary<string, List<FrameReading>> byPanel = new(StringComparer.OrdinalIgnoreCase);
        foreach (string cameraDir in Directory.EnumerateDirectories(capturesDir))
        {
            string cameraName = Path.GetFileName(cameraDir);
            if (string.Equals(cameraName, "Calibration", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (string panelDir in Directory.EnumerateDirectories(cameraDir))
            {
                string panelName = Path.GetFileName(panelDir);
                List<FrameReading> readings = await ReadFramesAsync(
                    Directory.EnumerateDirectories(panelDir), cameraName, skipped, ct).ConfigureAwait(false);
                if (readings.Count == 0) continue;
                if (!byPanel.TryGetValue(panelName, out List<FrameReading>? list))
                    byPanel[panelName] = list = [];
                list.AddRange(readings);
            }
        }
        return byPanel;
    }

    // -----------------------------------------------------------------------

    /// <summary>One frame's scan-relevant facts. <paramref name="CameraDirName"/> is the containing capture
    /// directory — authoritative for which camera took the frame, known before the file is opened — while the
    /// header carries the camera identifier the writer recorded inside it. Both are kept so the two can be
    /// compared: a disagreement means the frame is filed under the wrong camera.</summary>
    private sealed record FrameReading(
        XisfHeader Header,
        string FilterCode,
        FilterPurpose Purpose,
        string CameraDirName)
    {
        /// <summary>True when the frame records a camera identifier that disagrees with its containing
        /// directory. A frame recording nothing is silent, not in disagreement.</summary>
        public bool CameraDisagrees =>
            Header.Instrument is string recorded
            && recorded.Trim().Length > 0
            && !string.Equals(recorded.Trim(), CameraDirName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TargetReport?> ScanTargetAsync(
        string targetDir,
        ConcurrentDictionary<string, string> skipped,
        CancellationToken ct)
    {
        string dirName = Path.GetFileName(targetDir);
        // Non-sidereal targets never enter the scan (see IsNonSiderealDirectory). Guarded here rather than at
        // the walk so both entry points honour it from one place — the bulk ScanAsync and the surgical
        // ScanUnitsAsync both arrive through this method.
        if (IsNonSiderealDirectory(dirName)) return null;

        string capturesDir = Path.Combine(targetDir, "Captures");
        if (!Directory.Exists(capturesDir)) return null;

        // A mosaic nests one extra panel level under the camera. One walk serves both granularities: the
        // panel groups become per-panel sub-reports AND their union feeds the whole-target aggregate.
        if (MosaicConvention.IsMosaicDirectory(dirName))
        {
            Dictionary<string, List<FrameReading>> byPanel =
                await ReadMosaicPanelsAsync(capturesDir, skipped, ct).ConfigureAwait(false);

            List<TargetReport> panels = [];
            foreach ((string panelName, List<FrameReading> panelReadings) in
                     byPanel.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                TargetReport? panel = BuildReport(panelName, panelReadings);
                if (panel is not null) panels.Add(panel);
            }

            return BuildReport(dirName, [.. byPanel.Values.SelectMany(r => r)], panels);
        }

        List<FrameReading> readings = new();
        foreach (string cameraDir in Directory.EnumerateDirectories(capturesDir))
        {
            string cameraName = Path.GetFileName(cameraDir);
            // Skip Calibration/ — holds master bias/dark/flat frames, not light captures.
            if (string.Equals(cameraName, "Calibration", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            readings.AddRange(await ReadFramesAsync(
                Directory.EnumerateDirectories(cameraDir), cameraName, skipped, ct).ConfigureAwait(false));
        }

        return BuildReport(dirName, readings);
    }

    // Reads every *.xisf under each filter directory into frame readings, recording header-parse failures in
    // <paramref name="skipped"/> rather than aborting. Shared by the whole-target walk and the per-panel walk.
    private static async Task<List<FrameReading>> ReadFramesAsync(
        IEnumerable<string> filterDirs,
        string cameraName,
        ConcurrentDictionary<string, string> skipped,
        CancellationToken ct)
    {
        List<FrameReading> readings = new();
        foreach (string filterDir in filterDirs)
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
        return readings;
    }

    // Folds a set of frame readings into one TargetReport labelled <paramref name="label"/> (a target dir name, or a
    // panel name for a mosaic unit), optionally carrying per-panel sub-reports for a mosaic parent. Returns null
    // when nothing aggregates (no frames, or none with EXPTIME+DATE-OBS).
    private static TargetReport? BuildReport(
        string label, IReadOnlyList<FrameReading> readings, IReadOnlyList<TargetReport>? panels = null)
    {
        if (readings.Count == 0) return null;

        (string catalog, string? commonName) = TargetReport.SplitDirectoryName(label);
        string objectName = ConsensusObjectName(readings, catalog);
        (double raHours, double decDegrees) = ConsensusCoordinates(readings);

        // The aggregate identity is the CAPTURE CONFIGURATION — everything that decides whether frames
        // combine into one integration: filter, purpose, sub length, gain, offset, binning, and the camera
        // that took them. Frames differing in any of these are separate aggregates because they are separate
        // stacks (e.g. HDR 120 s + 300 s, or the 2024 broadband move from gain 53 to gain 0). Bucketing the
        // exposure to the nearest second matches ComputeTypical's clustering, so within a bucket
        // Typical.ExposureSec IS the bucket value — and likewise gain/offset/binning are now uniform.
        List<FilterAggregate> aggregates = readings
            .GroupBy(r => (
                r.FilterCode,
                r.Purpose,
                Seconds: ExposureBucket(r),
                Gain: r.Header.Gain ?? 0,
                Offset: r.Header.OffsetRaw ?? 0,
                BinX: Math.Max(1, r.Header.XBinning ?? 1),
                BinY: Math.Max(1, r.Header.YBinning ?? 1),
                r.CameraDirName))
            .Select(g => BuildAggregate(g.Key.FilterCode, g.Key.Purpose, g.Key.CameraDirName, g.ToList()))
            .Where(a => a is not null)
            .Cast<FilterAggregate>()
            .OrderBy(a => a.FilterCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Purpose)
            .ThenBy(a => a.Typical.ExposureSec)
            .ThenBy(a => a.Typical.Gain)
            .ThenBy(a => a.Typical.Offset)
            .ThenBy(a => a.Camera, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (aggregates.Count == 0) return null;

        return new TargetReport(label, catalog, commonName, objectName, raHours, decDegrees, aggregates, panels);
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
        FilterPurpose purpose = FilterPurposeClassifier.Classify(dirName);
        string code = purpose == FilterPurpose.Stars
            ? dirName[FilterPurposeClassifier.StarsPrefix.Length..].Trim()
            : dirName.Trim();
        return (code, purpose);
    }

    /// <summary>
    /// True when a target directory names a <b>non-sidereal</b> object — one whose coordinates change from
    /// night to night, so no sidereal plan can describe it and every frame of it is acquired by hand at the
    /// telescope. Such targets are excluded from the scan entirely, like the calibration tree.
    /// </summary>
    /// <remarks>
    /// The evidence for the fact is the directory-naming convention: a comet directory is prefixed
    /// <c>"Comet "</c> (e.g. <c>"Comet C2023 A3 - Tsuchinshan"</c>). The trailing space is load-bearing — it
    /// keeps a sidereal object whose name merely begins with those letters from matching.
    /// <para>
    /// Excluding them is not merely tidiness: their capture trees also break the
    /// <c>Captures/&lt;Camera&gt;/&lt;Filter&gt;/</c> convention, nesting date-named session folders
    /// (<c>"2024-10-18 - Track Comet"</c>) where a filter directory belongs, so a scan would publish those
    /// session names as filter codes.
    /// </para>
    /// </remarks>
    public static bool IsNonSiderealDirectory(string directoryName) =>
        directoryName is not null
        && directoryName.TrimStart().StartsWith("Comet ", StringComparison.OrdinalIgnoreCase);

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
        string filterCode, FilterPurpose purpose, string cameraDirName, IReadOnlyList<FrameReading> frames)
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

        return new FilterAggregate(
            filterName: NormalizeFilterName(filterCode),
            filterCode: filterCode,
            purpose: purpose,
            exposureCount: count,
            totalIntegration: total,
            firstImagedUtc: dates[0],
            lastImagedUtc: dates[^1],
            typical: typical,
            camera: cameraDirName,
            // Any frame recording a camera that disagrees with its containing directory means the frame is
            // filed under the wrong camera — reported, never silently reconciled.
            cameraDisagrees: withExp.Any(f => f.CameraDisagrees));
    }

    // Whole-second exposure bucket for aggregate identity (599.97 and 600.00 share a bucket). Frames without
    // EXPTIME land in bucket 0 and are dropped by BuildAggregate's EXPTIME filter, exactly as before.
    private static int ExposureBucket(FrameReading r) => (int)Math.Round(r.Header.ExposureSec ?? 0.0);

    private static TypicalSettings ComputeTypical(IReadOnlyList<FrameReading> frames)
    {
        int gain = Mode(frames.Select(f => f.Header.Gain ?? 0));
        // OFFSET is read exactly as recorded. XFM writes the value unchanged (its per-camera "divided by N"
        // comment is descriptive, not an operation), so a frame's offset is already in the same scale TS's
        // exposure templates use — any further conversion would produce a number comparable to neither plane.
        int offset = Mode(frames.Select(f => f.Header.OffsetRaw ?? 0));
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
