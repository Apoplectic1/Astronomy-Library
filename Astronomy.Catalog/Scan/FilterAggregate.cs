namespace Astronomy.Catalog.Scan;

/// <summary>
/// Per-filter rollup of a target's imaging history. One instance per
/// (target, filter directory, purpose, exposure-time bucket) — Light and Stars variants of the same
/// filter become separate aggregates (<c>"B"</c> Light vs <c>"B"</c> Stars), and so do different sub
/// lengths of the same filter (<c>"H"</c> 120 s vs <c>"H"</c> 300 s). Within an aggregate the exposure
/// time is therefore uniform: <see cref="Typical"/>.<c>ExposureSec</c> is the bucket's whole-second value.
/// </summary>
public sealed class FilterAggregate
{
    /// <summary>Canonical filter name -- single-letter ("L", "H", "O", "S", "R", "G", "B") for the standard set; equal to <see cref="FilterCode"/> for custom codes. Derived from <see cref="FilterCode"/>.</summary>
    public string FilterName { get; }

    /// <summary>Single-letter filter code from the directory path (e.g. "L", "H", "O", "S", "R", "G", "B"). The user's XFM-enforced convention.</summary>
    public string FilterCode { get; }

    /// <summary>Whether these frames are primary Light captures or Stars-only companion captures.</summary>
    public FilterPurpose Purpose { get; }

    /// <summary>Number of LIGHT frames aggregated (matches what XFM's marker-file <c>&lt;count&gt;</c> would show).</summary>
    public int ExposureCount { get; }

    /// <summary>Sum of all frames' EXPTIME — total integration on this filter/purpose for this target.</summary>
    public TimeSpan TotalIntegration { get; }

    /// <summary>UTC instant of the earliest DATE-OBS in the aggregate.</summary>
    public DateTime FirstImagedUtc { get; }

    /// <summary>UTC instant of the latest DATE-OBS in the aggregate.</summary>
    public DateTime LastImagedUtc { get; }

    /// <summary>Mode-based exposure settings across the aggregate.</summary>
    public TypicalSettings Typical { get; }

    /// <summary>Distinct INSTRUME values observed in this aggregate, sorted. Usually one camera; multi-camera entries indicate target was reshot on different equipment.</summary>
    public IReadOnlyList<string> CamerasSeen { get; }

    /// <summary>Creates an immutable per-filter aggregate. All counts/totals validated for sanity.</summary>
    public FilterAggregate(
        string filterName,
        string filterCode,
        FilterPurpose purpose,
        int exposureCount,
        TimeSpan totalIntegration,
        DateTime firstImagedUtc,
        DateTime lastImagedUtc,
        TypicalSettings typical,
        IReadOnlyList<string> camerasSeen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterCode);
        ArgumentNullException.ThrowIfNull(typical);
        ArgumentNullException.ThrowIfNull(camerasSeen);

        if (exposureCount <= 0) throw new ArgumentOutOfRangeException(nameof(exposureCount), "ExposureCount must be > 0; zero-frame aggregates are invalid.");
        if (totalIntegration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalIntegration), "TotalIntegration must be > 0.");
        if (firstImagedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("FirstImagedUtc must be Utc kind.", nameof(firstImagedUtc));
        if (lastImagedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("LastImagedUtc must be Utc kind.", nameof(lastImagedUtc));
        if (firstImagedUtc > lastImagedUtc) throw new ArgumentException("FirstImagedUtc must be ≤ LastImagedUtc.", nameof(firstImagedUtc));

        FilterName = filterName;
        FilterCode = filterCode;
        Purpose = purpose;
        ExposureCount = exposureCount;
        TotalIntegration = totalIntegration;
        FirstImagedUtc = firstImagedUtc;
        LastImagedUtc = lastImagedUtc;
        Typical = typical;
        CamerasSeen = camerasSeen;
    }
}
