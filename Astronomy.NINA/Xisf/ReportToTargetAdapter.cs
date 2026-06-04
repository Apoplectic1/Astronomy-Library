using Astronomy.Catalog.Scan;

namespace Astronomy.NINA.Xisf;

/// <summary>
/// Adapts the Phase A scan output (<see cref="ImageLibraryReport"/>) into
/// Phase B rich targets (<see cref="Astronomy.NINA.Target"/>). Stateless;
/// deterministic for a given report.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="TargetReport"/> becomes one <see cref="Astronomy.NINA.Target"/>;
/// its <see cref="FilterAggregate"/>s become <see cref="FilterHistory"/> entries
/// on <see cref="Astronomy.NINA.Target.ImagingHistory"/>. <see cref="Astronomy.NINA.Target.PlannedExposures"/>
/// is left <see langword="null"/> — that channel populates from forward-looking
/// sources (NINA .json sequences) in Phase C, not from image-library history.
/// </para>
/// <para>
/// Filter-code → <see cref="Filter"/> mapping uses the static factory presets when
/// the code matches XFM's single-letter convention; otherwise a custom
/// <see cref="Filter"/> with null center/bandwidth is built so the data flows
/// through without loss.
/// </para>
/// </remarks>
public static class ReportToTargetAdapter
{
    /// <summary>
    /// Converts an entire <see cref="ImageLibraryReport"/> into the corresponding
    /// list of rich targets. Order matches <see cref="ImageLibraryReport.Targets"/>.
    /// </summary>
    public static IReadOnlyList<Astronomy.NINA.Target> ToTargets(this ImageLibraryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        List<Astronomy.NINA.Target> result = new(report.Targets.Count);
        foreach (TargetReport tr in report.Targets)
        {
            result.Add(ToTarget(tr));
        }
        return result;
    }

    /// <summary>Converts a single <see cref="TargetReport"/> into a rich target.</summary>
    public static Astronomy.NINA.Target ToTarget(this TargetReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Geometry uses the FITS OBJECT name (what NINA / sequence writers stamp into files).
        // The rich target's Name uses the canonical Catalog from the directory convention
        // (what the user sees in their library tree). The two usually match.
        // Pass signed dec with north=true; Core's ctor normalizes negative dec by flipping
        // both fields (declination=-5→5, north=true→false). Passing a pre-derived "north"
        // flag here would double-flip and silently land in the wrong hemisphere.
        Astronomy.Core.Targets.Target geometry = new(
            name: report.ObjectName,
            rightAscension: report.RaHours,
            declination: report.DecDegrees, north: true,
            directory: report.DirectoryName,
            enabled: true);

        List<FilterHistory> history = new(report.Filters.Count);
        foreach (FilterAggregate agg in report.Filters)
        {
            history.Add(ToFilterHistory(agg));
        }

        return new Astronomy.NINA.Target(
            name: report.Catalog,
            geometry: geometry,
            imagingHistory: history,
            plannedExposures: null,
            customHorizon: null,
            rotationDeg: 0.0);
    }

    /// <summary>Converts a single <see cref="FilterAggregate"/> to a <see cref="FilterHistory"/>.</summary>
    public static FilterHistory ToFilterHistory(this FilterAggregate agg)
    {
        ArgumentNullException.ThrowIfNull(agg);
        return new FilterHistory(
            filter: FilterFromCode(agg.FilterCode),
            purpose: agg.Purpose,
            exposureCount: agg.ExposureCount,
            totalIntegration: agg.TotalIntegration,
            firstImagedUtc: agg.FirstImagedUtc,
            lastImagedUtc: agg.LastImagedUtc,
            typicalSettings: new ExposureSettings(
                gain: agg.Typical.Gain,
                offset: agg.Typical.Offset,
                setTempC: agg.Typical.SetTempC,
                binning: agg.Typical.Binning,
                exposureSec: agg.Typical.ExposureSec));
    }

    /// <summary>
    /// Maps XFM's single-letter filter codes to standard <see cref="Filter"/> presets.
    /// Unknown codes produce a custom <see cref="Filter"/> with the code as
    /// <see cref="Filter.Name"/> and null center/bandwidth so data isn't lost.
    /// </summary>
    public static Filter FilterFromCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return code switch
        {
            "L" => Filter.L,
            "H" => Filter.H,
            "O" => Filter.O,
            "S" => Filter.S,
            "R" => Filter.R,
            "G" => Filter.G,
            "B" => Filter.B,
            _ => new Filter(code),
        };
    }
}
