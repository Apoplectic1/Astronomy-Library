namespace Astronomy.Catalog.Scan;

/// <summary>
/// The mosaic filing convention. A target directory whose name starts with <see cref="Prefix"/> is a mosaic: its
/// captures nest one extra (opaque) panel level —
/// <c>Mosaic - &lt;Name&gt;/Captures/&lt;Camera&gt;/&lt;panel&gt;/&lt;Filter&gt;/</c> — and it reconciles by
/// <b>name</b> against the same-named N.I.N.A. Target Scheduler <c>isMosaic</c> project, not by coordinates
/// (a mosaic's panels spread well beyond the match tolerance from its centroid).
/// </summary>
public static partial class MosaicConvention
{
    /// <summary>Top-level directory-name prefix that marks a mosaic target (and, minus any altitude
    /// clause — see <see cref="StripAltitudeClause"/> — equals the TS mosaic project name).</summary>
    public const string Prefix = "Mosaic - ";

    /// <summary>
    /// Strips one trailing altitude clause — <c>"«name» - 30"</c> (the current short authoring form)
    /// or the legacy <c>"«name» - Above 30"</c> — so the mosaic name-match tolerates the clause's
    /// presence or absence on either side: project names may carry a minimum-altitude suffix while
    /// capture directories stay bare. Case-insensitive, spacing-tolerant, integer or decimal degrees;
    /// the dash is load-bearing (a name merely ending in a number, "Abell 2218", is never stripped);
    /// a name without the clause returns unchanged (trimmed).
    /// </summary>
    public static string StripAltitudeClause(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AltitudeClause().Replace(name, string.Empty).TrimEnd();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*-\s*(Above\s*)?\d+(\.\d+)?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AltitudeClause();

    /// <summary>
    /// Separator used in a panel's catalog directory name, <c>"&lt;mosaic dir&gt;/&lt;panel label&gt;"</c>.
    /// A forward slash is invalid in Windows file names, so the composite is unambiguous and the panel label
    /// is always recoverable.
    /// </summary>
    public const char PanelSeparator = '/';

    /// <summary>
    /// True when <paramref name="directoryName"/> names a mosaic-family entry (case-insensitive). A panel's
    /// composite directory name also starts with the prefix — callers distinguishing the parent from a panel
    /// use <see cref="IsPanelDirectoryName"/>.
    /// </summary>
    public static bool IsMosaicDirectory(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);
        return directoryName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The composite catalog directory name for one panel of a mosaic.</summary>
    public static string PanelDirectoryName(string mosaicDirectoryName, string panelLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mosaicDirectoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(panelLabel);
        return mosaicDirectoryName + PanelSeparator + panelLabel;
    }

    /// <summary>True when <paramref name="directoryName"/> is a panel's composite name rather than a top-level directory.</summary>
    public static bool IsPanelDirectoryName(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);
        return directoryName.Contains(PanelSeparator);
    }

    /// <summary>The bare panel label of a composite panel directory name (the part after the first separator).</summary>
    public static string PanelLabel(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);
        int sep = directoryName.IndexOf(PanelSeparator);
        return sep < 0 ? directoryName : directoryName[(sep + 1)..];
    }
}
