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
    /// Strips one trailing altitude clause — exactly the spaced form <c>"«name» - 30"</c> (a space, a
    /// dash, a space, then integer or decimal degrees, at the end) — so a name-match tolerates the
    /// clause's presence or absence on either side: project names may carry a minimum-altitude suffix
    /// while capture directories stay bare. The spaces are load-bearing: a hyphen-digit designation
    /// ("Sh2-155") and a name merely ending in a number ("Abell 2218") are never stripped. The spaced
    /// requirement keeps stripping correct when only ONE side of a compare carries a clause — a loose
    /// dashed match would strip a designation's tail on the bare side alone. A name without the clause
    /// returns unchanged (trimmed).
    /// </summary>
    public static string StripAltitudeClause(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AltitudeClause().Replace(name, string.Empty).TrimEnd();
    }

    /// <summary>
    /// Composes a name from its base and a minimum-altitude value: <c>"«base» - 30"</c> (degrees in
    /// <c>0.#</c> format — integers bare, tenths kept — invariant culture). The inverse of
    /// <see cref="ExtractBaseName"/> + <see cref="TryReadAltitudeClause"/>; callers derive the stored
    /// name from these two facts and never parse a name to obtain an altitude.
    /// </summary>
    public static string ComposeAltitudeName(string baseName, double altitudeDegrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return $"{baseName.TrimEnd()} - {altitudeDegrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Reads the altitude value of a trailing clause in the spaced form recognized by
    /// <see cref="StripAltitudeClause"/>. False when the name carries no clause — including the retired
    /// legacy <c>" - Above N"</c> form, which no longer parses as a clause. Read-only: the value is for
    /// display/comparison, never a write source.
    /// </summary>
    public static bool TryReadAltitudeClause(string name, out double altitudeDegrees)
    {
        ArgumentNullException.ThrowIfNull(name);
        System.Text.RegularExpressions.Match m = AltitudeClause().Match(name);
        altitudeDegrees = m.Success
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        return m.Success;
    }

    /// <summary>
    /// The base name: <paramref name="name"/> minus one trailing altitude clause. Beyond the spaced
    /// clause <see cref="StripAltitudeClause"/> recognizes, base extraction ALONE also strips the
    /// retired legacy <c>" - Above N"</c> suffix (case-insensitive) — so recomposition heals a legacy
    /// name ("Nebulae - Above 45" → base "Nebulae" → "Nebulae - 40") instead of nesting it. Only the
    /// final clause is removed: a base that itself resembles a clause round-trips
    /// ("Veil - 3 - 30" → "Veil - 3").
    /// </summary>
    public static string ExtractBaseName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string stripped = AltitudeClause().Replace(name, string.Empty).TrimEnd();
        if (stripped.Length == name.TrimEnd().Length)
            stripped = LegacyAltitudeClause().Replace(name, string.Empty).TrimEnd();
        return stripped;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+-\s+(\d+(?:\.\d+)?)\s*$")]
    private static partial System.Text.RegularExpressions.Regex AltitudeClause();

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+-\s+Above\s*\d+(\.\d+)?\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LegacyAltitudeClause();

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
