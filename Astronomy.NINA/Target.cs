using Astronomy.Core.Horizons;

namespace Astronomy.NINA;

/// <summary>
/// Rich planning-side target: wraps the <see cref="Astronomy.Core.Targets.Target"/>
/// geometry primitive with composed planning data (per-filter imaging history,
/// forward-looking planned exposures, custom horizon, rotation angle).
/// Immutable; mutations produce new instances via <see cref="With"/>.
/// </summary>
/// <remarks>
/// <para>
/// Naming note: this is <c>Astronomy.NINA.Target</c>, distinct from
/// <c>Astronomy.Core.Targets.Target</c>. Consumers that need both visible in one
/// file should use a <c>using</c> alias (e.g.
/// <c>using NinaTarget = Astronomy.NINA.Target;</c>).
/// </para>
/// <para>
/// <see cref="ImagingHistory"/> is an <see cref="IReadOnlyList{T}"/> that's empty
/// when the target has never been imaged (explicit absence, not <see langword="null"/>).
/// <see cref="PlannedExposures"/> is <see langword="null"/> when no forward-looking
/// plan source carries this target.
/// </para>
/// </remarks>
public sealed class Target
{
    /// <summary>Human-readable name. Often matches <see cref="Geometry"/>.Name but may differ (e.g. directory-derived display name).</summary>
    public string Name { get; }

    /// <summary>The underlying geometry primitive from Astronomy.Core (RA, Dec, hemisphere flag).</summary>
    public Astronomy.Core.Targets.Target Geometry { get; }

    /// <summary>Per-filter imaging history (one entry per (filter, purpose) combination actually captured). Empty list when the target has never been imaged.</summary>
    public IReadOnlyList<FilterHistory> ImagingHistory { get; }

    /// <summary>Forward-looking planned exposure blocks from a sequence source (e.g. NINA .json). <see langword="null"/> when no such source covers this target.</summary>
    public IReadOnlyList<PlannedExposure>? PlannedExposures { get; }

    /// <summary>Per-target horizon obstruction profile; <see langword="null"/> when the target uses the site-level horizon.</summary>
    public IHorizonProfile? CustomHorizon { get; }

    /// <summary>Camera rotation angle in degrees (matches NINA's <c>InputTarget.PositionAngle</c>). Normalized into <c>[0, 360)</c> by the constructor.</summary>
    public double RotationDeg { get; }

    /// <summary>Creates an immutable rich target. Throws on null/empty name, null geometry, or null imagingHistory; rotation is wrapped mod 360.</summary>
    public Target(
        string name,
        Astronomy.Core.Targets.Target geometry,
        IReadOnlyList<FilterHistory>? imagingHistory = null,
        IReadOnlyList<PlannedExposure>? plannedExposures = null,
        IHorizonProfile? customHorizon = null,
        double rotationDeg = 0.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(geometry);

        // ImagingHistory: null collapses to empty list (explicit absence). Caller can also pass an empty list.
        imagingHistory ??= Array.Empty<FilterHistory>();

        // Rotation: mod-360 normalize for lenient user input. Negative inputs wrap into [0, 360).
        rotationDeg %= 360.0;
        if (rotationDeg < 0) rotationDeg += 360.0;

        Name = name;
        Geometry = geometry;
        ImagingHistory = imagingHistory;
        PlannedExposures = plannedExposures;
        CustomHorizon = customHorizon;
        RotationDeg = rotationDeg;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public Target With(
        string? name = null,
        Astronomy.Core.Targets.Target? geometry = null,
        IReadOnlyList<FilterHistory>? imagingHistory = null,
        IReadOnlyList<PlannedExposure>? plannedExposures = null,
        IHorizonProfile? customHorizon = null,
        double? rotationDeg = null)
        => new Target(
            name ?? Name,
            geometry ?? Geometry,
            imagingHistory ?? ImagingHistory,
            plannedExposures ?? PlannedExposures,
            customHorizon ?? CustomHorizon,
            rotationDeg ?? RotationDeg);
}
