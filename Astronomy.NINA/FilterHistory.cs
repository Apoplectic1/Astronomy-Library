using Astronomy.NINA.Xisf;

namespace Astronomy.NINA;

/// <summary>
/// One filter's-worth of imaging history for a target: how many frames in this
/// filter have been captured, total integration time, first / last imaged dates,
/// and the typical camera settings used. Composed onto
/// <see cref="Target.ImagingHistory"/>.
/// </summary>
/// <remarks>
/// A zero-count <see cref="FilterHistory"/> is semantically nonsense — if a
/// target has never been imaged in some filter, there's no corresponding
/// <see cref="FilterHistory"/> in <see cref="Target.ImagingHistory"/>. The
/// constructor enforces <see cref="ExposureCount"/> &gt; 0.
/// </remarks>
public sealed class FilterHistory
{
    /// <summary>The filter these frames were captured through.</summary>
    public Filter Filter { get; }

    /// <summary>Why these frames were captured — Light (primary subject) or Stars (short-exposure star-only captures for starless-recombination workflow). Separates the two kinds of frames so integration totals aren't muddied.</summary>
    public FilterPurpose Purpose { get; }

    /// <summary>Number of frames captured in this filter for this target.</summary>
    public int ExposureCount { get; }

    /// <summary>Sum of all frame exposure durations.</summary>
    public TimeSpan TotalIntegration { get; }

    /// <summary>UTC instant of the earliest frame.</summary>
    public DateTime FirstImagedUtc { get; }

    /// <summary>UTC instant of the latest frame.</summary>
    public DateTime LastImagedUtc { get; }

    /// <summary>Typical camera config across the aggregated frames (mode-based from the image-library scanner; explicit from other sources).</summary>
    public ExposureSettings TypicalSettings { get; }

    /// <summary>Creates an immutable per-filter history record. Throws on null filter/settings, zero counts, negative integration, non-Utc kinds, or first&gt;last ordering.</summary>
    public FilterHistory(
        Filter filter,
        FilterPurpose purpose,
        int exposureCount,
        TimeSpan totalIntegration,
        DateTime firstImagedUtc,
        DateTime lastImagedUtc,
        ExposureSettings typicalSettings)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(typicalSettings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureCount);
        if (totalIntegration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalIntegration), "TotalIntegration must be > 0.");
        // Normalize to UTC consistently with the rest of the Library. DateTime.ToUniversalTime() does
        // exactly what Astronomy.Core.Time.TimeKindGuard.AsUtc does: Utc no-op, Local→UTC via machine
        // zone, Unspecified treated as Local then converted. (TimeKindGuard itself is internal to Core.)
        firstImagedUtc = firstImagedUtc.ToUniversalTime();
        lastImagedUtc = lastImagedUtc.ToUniversalTime();
        if (firstImagedUtc > lastImagedUtc) throw new ArgumentException("FirstImagedUtc must be ≤ LastImagedUtc.", nameof(firstImagedUtc));

        Filter = filter;
        Purpose = purpose;
        ExposureCount = exposureCount;
        TotalIntegration = totalIntegration;
        FirstImagedUtc = firstImagedUtc;
        LastImagedUtc = lastImagedUtc;
        TypicalSettings = typicalSettings;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public FilterHistory With(
        Filter? filter = null,
        FilterPurpose? purpose = null,
        int? exposureCount = null,
        TimeSpan? totalIntegration = null,
        DateTime? firstImagedUtc = null,
        DateTime? lastImagedUtc = null,
        ExposureSettings? typicalSettings = null)
        => new FilterHistory(
            filter ?? Filter,
            purpose ?? Purpose,
            exposureCount ?? ExposureCount,
            totalIntegration ?? TotalIntegration,
            firstImagedUtc ?? FirstImagedUtc,
            lastImagedUtc ?? LastImagedUtc,
            typicalSettings ?? TypicalSettings);
}
