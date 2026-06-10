namespace Astronomy.Catalog.Scan;

/// <summary>
/// Per-target summary derived from scanning one top-level directory under the
/// image library root. Carries the canonical identity (directory name), the
/// confirming FITS OBJECT keyword, computed coordinates, and per-filter
/// aggregates.
/// </summary>
public sealed class TargetReport
{
    /// <summary>Top-level directory name under the library root — canonical identity (e.g. <c>"M51 - Whirlpool"</c>).</summary>
    public string DirectoryName { get; }

    /// <summary>Portion before the first <c>" - "</c> in <see cref="DirectoryName"/> (e.g. <c>"M51"</c>). Equals <see cref="DirectoryName"/> when no separator present.</summary>
    public string Catalog { get; }

    /// <summary>Portion after the first <c>" - "</c> in <see cref="DirectoryName"/> (e.g. <c>"Whirlpool"</c>). <see langword="null"/> when no separator present.</summary>
    public string? CommonName { get; }

    /// <summary>The OBJECT FITS keyword value observed in frames. Should match <see cref="Catalog"/> (case-insensitive); mismatch is a discipline-drift warning at scan time.</summary>
    public string ObjectName { get; }

    /// <summary>Right ascension in decimal hours in <c>[0, 24)</c>, converted from FITS-spec degrees. Most-common value across frames after sanity check.</summary>
    public double RaHours { get; }

    /// <summary>Declination in decimal degrees signed (<c>[-90, +90]</c>). Most-common value across frames.</summary>
    public double DecDegrees { get; }

    /// <summary>Per-filter / per-purpose aggregates. Empty list when no LIGHT frames found in <c>Captures/</c>.</summary>
    public IReadOnlyList<FilterAggregate> Filters { get; }

    /// <summary>
    /// Per-panel sub-reports for a mosaic target, each carrying its own consensus centroid and per-filter
    /// aggregates; empty for a normal target. A panel report's <see cref="DirectoryName"/> is the bare panel
    /// directory label (e.g. <c>"Panel 01of16"</c>). <see cref="Filters"/> on the parent remains the
    /// panel-summed whole-target aggregate.
    /// </summary>
    public IReadOnlyList<TargetReport> Panels { get; }

    /// <summary>Creates an immutable per-target report.</summary>
    public TargetReport(
        string directoryName,
        string catalog,
        string? commonName,
        string objectName,
        double raHours,
        double decDegrees,
        IReadOnlyList<FilterAggregate> filters,
        IReadOnlyList<TargetReport>? panels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentNullException.ThrowIfNull(filters);

        if (raHours < 0 || raHours >= 24) throw new ArgumentOutOfRangeException(nameof(raHours), "RaHours must be in [0, 24).");
        if (decDegrees < -90 || decDegrees > 90) throw new ArgumentOutOfRangeException(nameof(decDegrees), "DecDegrees must be in [-90, +90].");

        DirectoryName = directoryName;
        Catalog = catalog;
        CommonName = commonName;
        ObjectName = objectName;
        RaHours = raHours;
        DecDegrees = decDegrees;
        Filters = filters;
        Panels = panels ?? [];
    }

    /// <summary>
    /// Splits a directory name into (Catalog, CommonName) on the first <c>" - "</c> separator.
    /// CommonName is <see langword="null"/> when no separator is present.
    /// </summary>
    public static (string Catalog, string? CommonName) SplitDirectoryName(string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        int sep = directoryName.IndexOf(" - ", StringComparison.Ordinal);
        return sep < 0
            ? (directoryName, null)
            : (directoryName[..sep].TrimEnd(), directoryName[(sep + 3)..].TrimStart());
    }
}
