namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// An immutable snapshot of the TS plan plane (projects, targets, exposure templates, exposure plans) read in one
/// pass from <c>schedulerdb.sqlite</c>. Decouples the pure <c>TargetResolver</c> from database I/O so resolution
/// can be unit-tested with synthetic plans.
/// </summary>
public sealed record TsPlanData(
    IReadOnlyList<TsProject> Projects,
    IReadOnlyList<TsTarget> Targets,
    IReadOnlyList<TsExposureTemplate> Templates,
    IReadOnlyList<TsExposurePlan> Plans)
{
    /// <summary>An empty plan (no TS database available) — yields an actuals-only catalog.</summary>
    public static TsPlanData Empty { get; } = new([], [], [], []);
}
