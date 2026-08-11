using Astronomy.Core;

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

    /// <summary>
    /// At or above this share of its footprint landing where the plan asked, a framing that <b>serves</b> the
    /// plan is treated as on-footprint and prices nothing. Only pointing differences reach this test — a
    /// framing whose rotation disagrees always reports, however high its overlap.
    /// </summary>
    /// <remarks>
    /// Calibrated against the real library: of 60 serving framings, 52 sit at ≥ 99.5% and the remaining 8
    /// spread from 85.6% to 97.8% — ordinary between-filter pointing scatter, not a hazard. 0.95 leaves the
    /// three genuinely displaced ones (0.05°–0.13° off centre) reporting and silences the rest. The threshold
    /// deliberately never gates a <b>disagreeing</b> framing, whose overlap can sit anywhere: a stray just
    /// past the 5° tolerance still covers ~95% of the plan's footprint, and silencing it for being close
    /// would leave a consumer's framing-disagreement indicator pointing at a row with nothing to read.
    /// </remarks>
    public const double OnFootprintFraction = 0.95;

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

    /// <summary>
    /// Angular width of the field this cluster's frames cover, in degrees; <see langword="null"/> when the
    /// frames do not carry enough to derive it. Taken from the cluster's <b>dominant</b> sensor when its
    /// frames span more than one — see <see cref="SpansMultipleSensors"/>.
    /// </summary>
    public double? FieldWidthDeg { get; }

    /// <summary>Angular height of the same field, in degrees; present or absent together with
    /// <see cref="FieldWidthDeg"/>.</summary>
    public double? FieldHeightDeg { get; }

    /// <summary>
    /// Whether this cluster's frames span more than one sensor geometry. Camera is not a clustering key, so
    /// one framing can hold frames from two sensors; the footprint then describes the dominant one and this
    /// flag says so, because averaging two sensors would describe a field that was never imaged.
    /// </summary>
    public bool SpansMultipleSensors { get; }

    /// <summary>Creates an immutable framing cluster.</summary>
    public FramingCluster(
        int ordinal,
        RotationExpression expression,
        double? foldAngleDegrees,
        double? centroidRaHours,
        double? centroidDecDegrees,
        int frameCount,
        double? fieldWidthDeg = null,
        double? fieldHeightDeg = null,
        bool spansMultipleSensors = false)
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
        if ((fieldWidthDeg is null) != (fieldHeightDeg is null))
            throw new ArgumentException("Field width and height must be present or absent together.", nameof(fieldHeightDeg));
        if (fieldWidthDeg is double fw && fw <= 0)
            throw new ArgumentOutOfRangeException(nameof(fieldWidthDeg), "Field width must be positive.");
        if (fieldHeightDeg is double fh && fh <= 0)
            throw new ArgumentOutOfRangeException(nameof(fieldHeightDeg), "Field height must be positive.");

        Ordinal = ordinal;
        Expression = expression;
        FoldAngleDegrees = foldAngleDegrees;
        CentroidRaHours = centroidRaHours;
        CentroidDecDegrees = centroidDecDegrees;
        FrameCount = frameCount;
        FieldWidthDeg = fieldWidthDeg;
        FieldHeightDeg = fieldHeightDeg;
        SpansMultipleSensors = spansMultipleSensors;
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

    /// <summary>
    /// Whether frames of the given rotation expression <b>serve</b> a plan whose target carries
    /// <paramref name="planRotationDegrees"/> — THE rotation-participation rule, shared by pairing, the
    /// disagreement cue, and write-back crediting so they can never drift apart. Rotation participates only
    /// as expressed by both sides: a mechanical or unknown expression is not comparable and never prevents
    /// serving, a plan without a rotation asks nothing, and a sky expression serves iff it agrees fold-180
    /// within <see cref="RotationToleranceDegrees"/>.
    /// </summary>
    /// <param name="expression">The frames' rotation expression.</param>
    /// <param name="foldAngleDegrees">The frames' fold-180 angle (present for Sky/Mechanical, null for Unknown).</param>
    /// <param name="planRotationDegrees">The plan target's rotation; null when it expresses none.</param>
    public static bool ServesPlanRotation(
        RotationExpression expression, double? foldAngleDegrees, double? planRotationDegrees)
    {
        if (expression != RotationExpression.Sky) return true;
        if (planRotationDegrees is not double rot) return true;
        return FoldDelta(foldAngleDegrees!.Value, rot) <= RotationToleranceDegrees;
    }

    /// <summary>
    /// The share of this cluster's own footprint that falls inside the footprint a plan asked for, in
    /// <c>[0, 1]</c>; <see langword="null"/> when there is nothing to price.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It prices being <b>off-footprint for any reason</b> — a rotation the plan did not ask for, a pointing
    /// the plan did not ask for, or both. A cluster whose rotation disagrees always reports. A cluster that
    /// serves the plan's rotation reports only when its pointing still leaves it below
    /// <see cref="OnFootprintFraction"/>, so an ordinary on-plan framing prices nothing rather than
    /// restating 100% on every row. A rotation that is not comparable (mechanical or unknown) and a plan
    /// expressing no rotation both return <see langword="null"/>: no orientation may be invented for the
    /// comparison. It is also null when either side lacks the coordinates or the footprint the geometry
    /// needs — absent, not zero.
    /// </para>
    /// <para>
    /// The plan's rectangle is built from <b>this cluster's own</b> field size, centred on the plan's
    /// coordinates and rotated to the plan's rotation. A plan expresses no sensor, so a size must be
    /// supplied; using the measured cluster's own makes the result depend solely on the centre offset and
    /// the angle difference — the framing error — and never on which camera took the frames.
    /// </para>
    /// <para>
    /// <b>Diagnostic only.</b> Nothing may scale a credited count by this value: a partially overlapping
    /// frame is not a fractional frame. Crediting is the boolean <see cref="ServesPlanRotation"/>.
    /// </para>
    /// </remarks>
    /// <param name="planRaHours">The plan target's RA in decimal hours.</param>
    /// <param name="planDecDegrees">The plan target's declination in signed degrees.</param>
    /// <param name="planRotationDegrees">The plan target's rotation; null when it expresses none.</param>
    public double? OverlapFractionAgainstPlan(
        double? planRaHours, double? planDecDegrees, double? planRotationDegrees) =>
        OverlapFractionAgainstPlan(
            Expression, FoldAngleDegrees, CentroidRaHours, CentroidDecDegrees,
            FieldWidthDeg, FieldHeightDeg,
            planRaHours, planDecDegrees, planRotationDegrees);

    /// <summary>
    /// The same overlap rule for a caller holding a framing's values loose rather than the cluster object —
    /// the reconciliation plane, which carries a cluster's expression, angle, centroid and footprint on its
    /// own rows. Static so the rule has exactly one definition, as with
    /// <see cref="ServesPlanRotation"/>.
    /// </summary>
    /// <param name="expression">The frames' rotation expression.</param>
    /// <param name="foldAngleDegrees">The frames' fold-180 angle (null for Unknown).</param>
    /// <param name="centroidRaHours">The framing's centroid RA in decimal hours; null when unknown.</param>
    /// <param name="centroidDecDegrees">The framing's centroid declination in signed degrees; null when unknown.</param>
    /// <param name="fieldWidthDeg">The framing's angular field width in degrees; null when underivable.</param>
    /// <param name="fieldHeightDeg">The framing's angular field height in degrees; null when underivable.</param>
    /// <param name="planRaHours">The plan target's RA in decimal hours.</param>
    /// <param name="planDecDegrees">The plan target's declination in signed degrees.</param>
    /// <param name="planRotationDegrees">The plan target's rotation; null when it expresses none.</param>
    public static double? OverlapFractionAgainstPlan(
        RotationExpression expression, double? foldAngleDegrees,
        double? centroidRaHours, double? centroidDecDegrees,
        double? fieldWidthDeg, double? fieldHeightDeg,
        double? planRaHours, double? planDecDegrees, double? planRotationDegrees)
    {
        // Only a sky angle can be placed against a plan's: a mechanical zero point is not a sky angle and is
        // never converted into one, and an unknown rotation has nothing to place at all.
        if (expression != RotationExpression.Sky || foldAngleDegrees is not double fold) return null;
        if (planRaHours is not double planRa || planDecDegrees is not double planDec) return null;
        if (planRotationDegrees is not double planRot) return null;
        if (centroidRaHours is not double ra || centroidDecDegrees is not double dec) return null;
        if (fieldWidthDeg is not double width || fieldHeightDeg is not double height) return null;

        double fraction = FieldFootprint.OverlapFraction(
            ra, dec, fold, planRa, planDec, planRot, width, height);

        // A framing that serves the plan and lands on its footprint has nothing to say; one that serves but
        // sits off the plan's pointing does. A DISAGREEING framing always reports, whatever the fraction.
        return ServesPlanRotation(expression, foldAngleDegrees, planRotationDegrees)
               && fraction >= OnFootprintFraction
            ? null
            : fraction;
    }
}
