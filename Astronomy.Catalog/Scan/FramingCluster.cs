namespace Astronomy.Catalog.Scan;

/// <summary>
/// How a framing cluster's rotation is expressed — which decides whether it can be compared against a
/// consuming plan's rotation value.
/// </summary>
public enum RotationExpression
{
    /// <summary>Frames record a sky (position) angle — directly comparable to a plan's rotation.</summary>
    Sky = 0,

    /// <summary>Frames record only the rotator's mechanical position. Real rotation, but the
    /// mechanical-to-sky zero point shifts when the camera is remounted, so it is never converted to a
    /// sky angle and never compared against a plan.</summary>
    Mechanical = 1,

    /// <summary>Frames record no rotation at all.</summary>
    Unknown = 2,
}

/// <summary>
/// One <b>framing</b> within a scan unit: the (field-center, sky-rotation) group of frames that share a
/// footprint and can therefore combine into one integration. Frames group by rotation folded mod 180°
/// (a 180° pier flip covers the identical footprint) plus field-center proximity, so a translated stray
/// at an unchanged rotation is its own framing. A single stray frame is a cluster — low-count
/// off-footprint framings are precisely the hazard worth surfacing.
/// </summary>
public sealed class FramingCluster
{
    /// <summary>Rotation agreement tolerance in degrees, applied to fold-180 deltas — both for grouping
    /// frames into clusters and for comparing a cluster against a plan's rotation. Calibrated against the
    /// real library: genuinely distinct framings sit ≥ 9° apart while within-framing jitter is ≤ 0.2°.</summary>
    public const double RotationToleranceDegrees = 5.0;

    /// <summary>Single-linkage distance in degrees for the field-center grouping step. Wide enough for
    /// dither/pointing scatter across a campaign (measured ≤ 0.54° span), tight enough that a genuine
    /// pointing stray (measured 1.3°+ from its nearest sibling) separates.</summary>
    public const double CentroidLinkDegrees = 0.5;

    /// <summary>Stable per-unit index (0-based; largest cluster first, ties by angle). Part of the
    /// aggregate identity, because two clusters can share a fold angle and differ only by center.</summary>
    public int Ordinal { get; }

    /// <summary>How this cluster's rotation is expressed.</summary>
    public RotationExpression Expression { get; }

    /// <summary>The cluster's rotation folded into <c>[0, 180)</c> — circular mean of its members' angles
    /// (sky angle for <see cref="RotationExpression.Sky"/>, mechanical for
    /// <see cref="RotationExpression.Mechanical"/>); <see langword="null"/> for
    /// <see cref="RotationExpression.Unknown"/>.</summary>
    public double? FoldAngleDegrees { get; }

    /// <summary>The cluster's own plate-solved centroid RA in decimal hours <c>[0, 24)</c>;
    /// <see langword="null"/> when no member carries coordinates.</summary>
    public double? CentroidRaHours { get; }

    /// <summary>The cluster's own plate-solved centroid Dec in signed decimal degrees;
    /// <see langword="null"/> when no member carries coordinates.</summary>
    public double? CentroidDecDegrees { get; }

    /// <summary>Number of frames in the cluster.</summary>
    public int FrameCount { get; }

    /// <summary>Creates an immutable framing cluster.</summary>
    public FramingCluster(
        int ordinal,
        RotationExpression expression,
        double? foldAngleDegrees,
        double? centroidRaHours,
        double? centroidDecDegrees,
        int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        if (expression == RotationExpression.Unknown && foldAngleDegrees is not null)
            throw new ArgumentException("An Unknown-expression cluster carries no angle.", nameof(foldAngleDegrees));
        if (expression != RotationExpression.Unknown && foldAngleDegrees is not double fold)
            throw new ArgumentException("A Sky/Mechanical cluster must carry its fold angle.", nameof(foldAngleDegrees));
        else if (foldAngleDegrees is double f && (f < 0 || f >= 180))
            throw new ArgumentOutOfRangeException(nameof(foldAngleDegrees), "FoldAngleDegrees must be in [0, 180).");
        if ((centroidRaHours is null) != (centroidDecDegrees is null))
            throw new ArgumentException("Centroid RA and Dec must be present or absent together.", nameof(centroidDecDegrees));

        Ordinal = ordinal;
        Expression = expression;
        FoldAngleDegrees = foldAngleDegrees;
        CentroidRaHours = centroidRaHours;
        CentroidDecDegrees = centroidDecDegrees;
        FrameCount = frameCount;
    }

    /// <summary>Folds any angle in degrees into <c>[0, 180)</c> — the flip-equivalent canonical form.</summary>
    public static double Fold180(double degrees)
    {
        double f = degrees % 180.0;
        return f < 0 ? f + 180.0 : f;
    }

    /// <summary>Smallest angular difference between two angles treating θ and θ+180° as identical —
    /// the metric under which a pier flip is the same framing. Result is in <c>[0, 90]</c>.</summary>
    public static double FoldDelta(double aDegrees, double bDegrees)
    {
        double d = Math.Abs(aDegrees - bDegrees) % 180.0;
        return Math.Min(d, 180.0 - d);
    }
}
