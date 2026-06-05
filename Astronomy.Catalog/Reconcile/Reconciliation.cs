using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.Reconcile;

/// <summary>Which actual frames count toward a filter's goal.</summary>
public enum ReconcilePolicy
{
    /// <summary>Light + Stars frames both count (default) — fits shooting RGB only as Stars for star colour.</summary>
    Combined = 0,

    /// <summary>Only deep Light frames count; Stars are reported but excluded from completion.</summary>
    LightOnly = 1,
}

/// <summary>Completion state of a goal (or of a target rolled up across its filters).</summary>
public enum ReconcileStatus
{
    /// <summary>A goal exists but nothing counted has been shot.</summary>
    NotStarted = 0,

    /// <summary>Some counted frames shot, goal not yet met.</summary>
    InProgress = 1,

    /// <summary>Counted frames meet or exceed the goal.</summary>
    Complete = 2,

    /// <summary>Frames shot for a filter with no goal — shot beyond or without a plan.</summary>
    Unplanned = 3,
}

/// <summary>
/// Goal (TS <c>desired_count</c>) vs actual (disk frames) for one (target, filter). <see cref="AcquiredCount"/> is
/// counted per the chosen <see cref="ReconcilePolicy"/>; <see cref="LightCount"/>/<see cref="StarsCount"/> always
/// carry the breakdown.
/// </summary>
public sealed record FilterReconciliation(
    string Filter,
    int DesiredCount,
    int AcquiredCount,
    int LightCount,
    int StarsCount,
    double IntegrationHours,
    ReconcileStatus Status)
{
    /// <summary>Frames still needed to hit the goal (0 once met or when there is no goal).</summary>
    public int RemainingCount => Math.Max(0, DesiredCount - AcquiredCount);

    /// <summary>Acquired / desired, clamped to [0,1]; 1.0 when complete or when there is no goal but frames exist.</summary>
    public double FractionComplete => DesiredCount <= 0
        ? (AcquiredCount > 0 ? 1.0 : 0.0)
        : Math.Min(1.0, (double)AcquiredCount / DesiredCount);
}

/// <summary>
/// Goal vs actual rolled up for one target, with the per-filter detail. Totals are derived from
/// <see cref="Filters"/> so an over-shot filter can't mask another filter's gap (remaining is summed per filter).
/// </summary>
public sealed record TargetReconciliation(
    Guid TargetId,
    string Name,
    TargetSource Source,
    ReconcileStatus Status,
    IReadOnlyList<FilterReconciliation> Filters)
{
    /// <summary>Sum of all filters' goals.</summary>
    public int TotalDesired => Filters.Sum(f => f.DesiredCount);

    /// <summary>Sum of all filters' counted actuals (may exceed <see cref="TotalDesired"/> when over-shot).</summary>
    public int TotalAcquired => Filters.Sum(f => f.AcquiredCount);

    /// <summary>Sum of per-filter remaining — the true outstanding frame count.</summary>
    public int TotalRemaining => Filters.Sum(f => f.RemainingCount);

    /// <summary>Met portion (desired − remaining) over desired, clamped to [0,1].</summary>
    public double FractionComplete => TotalDesired <= 0
        ? (TotalAcquired > 0 ? 1.0 : 0.0)
        : (double)(TotalDesired - TotalRemaining) / TotalDesired;
}
