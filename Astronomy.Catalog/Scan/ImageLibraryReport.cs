namespace Astronomy.Catalog.Scan;

/// <summary>
/// Top-level output of <c>ImageLibraryScanner</c>: every target found under the
/// scanned library root, with per-filter aggregates populated.
/// </summary>
public sealed class ImageLibraryReport
{
    /// <summary>Absolute path of the library root that was scanned.</summary>
    public string LibraryRoot { get; }

    /// <summary>UTC instant the scan completed.</summary>
    public DateTime ScannedAtUtc { get; }

    /// <summary>All target reports, sorted by <c>DirectoryName</c>.</summary>
    public IReadOnlyList<TargetReport> Targets { get; }

    /// <summary>Files the scanner skipped because of XISF parse failures (path → reason). Empty when the scan ran cleanly.</summary>
    public IReadOnlyDictionary<string, string> SkippedFiles { get; }

    /// <summary>Creates an immutable scan report.</summary>
    public ImageLibraryReport(
        string libraryRoot,
        DateTime scannedAtUtc,
        IReadOnlyList<TargetReport> targets,
        IReadOnlyDictionary<string, string> skippedFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(skippedFiles);
        if (scannedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("ScannedAtUtc must be Utc kind.", nameof(scannedAtUtc));

        LibraryRoot = libraryRoot;
        ScannedAtUtc = scannedAtUtc;
        Targets = targets;
        SkippedFiles = skippedFiles;
    }
}
