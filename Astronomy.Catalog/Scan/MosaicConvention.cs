namespace Astronomy.Catalog.Scan;

/// <summary>
/// The mosaic filing convention. A target directory whose name starts with <see cref="Prefix"/> is a mosaic: its
/// captures nest one extra (opaque) panel level —
/// <c>Mosaic - &lt;Name&gt;/Captures/&lt;Camera&gt;/&lt;panel&gt;/&lt;Filter&gt;/</c> — and it reconciles by
/// <b>name</b> against the same-named N.I.N.A. Target Scheduler <c>isMosaic</c> project, not by coordinates
/// (a mosaic's panels spread well beyond the match tolerance from its centroid).
/// </summary>
public static class MosaicConvention
{
    /// <summary>Top-level directory-name prefix that marks a mosaic target (and equals the TS mosaic project name).</summary>
    public const string Prefix = "Mosaic - ";

    /// <summary>True when <paramref name="directoryName"/> names a mosaic target (case-insensitive).</summary>
    public static bool IsMosaicDirectory(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);
        return directoryName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}
