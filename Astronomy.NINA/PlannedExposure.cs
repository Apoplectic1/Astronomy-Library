namespace Astronomy.NINA;

/// <summary>
/// A planned exposure block from a forward-looking source (typically a NINA
/// <c>.json</c> sequence file): "image this target in this filter at these
/// settings for this many frames." Composed onto <see cref="Target.PlannedExposures"/>.
/// </summary>
public sealed class PlannedExposure
{
    /// <summary>Filter for these exposures.</summary>
    public Filter Filter { get; }

    /// <summary>Number of exposures planned.</summary>
    public int Count { get; }

    /// <summary>Exposure duration in seconds. Carried separately from <see cref="Settings"/>.ExposureSec because plan sources may not carry full settings; <see cref="Settings"/> is null when only the duration is known.</summary>
    public double ExposureSec { get; }

    /// <summary>Full camera settings if specified in the plan; <see langword="null"/> if the source only carries filter+count+duration.</summary>
    public ExposureSettings? Settings { get; }

    /// <summary>Creates an immutable planned-exposure block. Throws on null filter, non-positive count or exposure.</summary>
    public PlannedExposure(Filter filter, int count, double exposureSec, ExposureSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureSec);

        Filter = filter;
        Count = count;
        ExposureSec = exposureSec;
        Settings = settings;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public PlannedExposure With(
        Filter? filter = null,
        int? count = null,
        double? exposureSec = null,
        ExposureSettings? settings = null)
        => new PlannedExposure(
            filter ?? Filter,
            count ?? Count,
            exposureSec ?? ExposureSec,
            settings ?? Settings);
}
