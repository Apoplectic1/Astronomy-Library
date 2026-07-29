namespace Astronomy.Catalog.Scan;

/// <summary>
/// Per-configuration rollup of a target's imaging history. One instance per
/// (target, filter directory, purpose, exposure-time bucket, gain, offset, binning, camera, framing) — the
/// full <b>capture configuration</b>, being everything that decides whether frames combine into one
/// integration. Light and Stars variants of the same filter become separate aggregates (<c>"B"</c> Light vs
/// <c>"B"</c> Stars), as do different sub lengths (<c>"H"</c> 120 s vs <c>"H"</c> 300 s), different gains,
/// different offsets, different binnings, different cameras, and different framings (see
/// <see cref="FramingCluster"/>).
/// <para>
/// Every configuration field is therefore <b>uniform within an aggregate</b>: <see cref="Typical"/>'s
/// <c>ExposureSec</c>, <c>Gain</c>, <c>Offset</c> and <c>Binning</c> are the bucket's own values rather than
/// a mode over mixed frames, <see cref="Camera"/> is the one camera that took them, and
/// <see cref="Framing"/> is the one framing they share.
/// </para>
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

    /// <summary>The camera that captured these frames — the containing capture directory's name, which is
    /// authoritative (it is known before a file is opened, and every frame beneath it belongs to that camera
    /// by construction). Part of the aggregate's identity, so this is always exactly one camera.</summary>
    public string Camera { get; }

    /// <summary>True when at least one frame records a camera identifier disagreeing with
    /// <see cref="Camera"/> — the frames are filed under the wrong camera. Reported to the caller rather
    /// than reconciled here.</summary>
    public bool CameraDisagrees { get; }

    /// <summary>The framing cluster these frames share — part of the aggregate's identity, so this is
    /// always exactly one framing.</summary>
    public FramingCluster Framing { get; }

    /// <summary>Creates an immutable per-configuration aggregate. All counts/totals validated for sanity.</summary>
    public FilterAggregate(
        string filterName,
        string filterCode,
        FilterPurpose purpose,
        int exposureCount,
        TimeSpan totalIntegration,
        DateTime firstImagedUtc,
        DateTime lastImagedUtc,
        TypicalSettings typical,
        string camera,
        FramingCluster framing,
        bool cameraDisagrees = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterCode);
        ArgumentNullException.ThrowIfNull(typical);
        ArgumentException.ThrowIfNullOrWhiteSpace(camera);
        ArgumentNullException.ThrowIfNull(framing);

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
        Camera = camera;
        Framing = framing;
        CameraDisagrees = cameraDisagrees;
    }
}
