using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.Reconcile;

/// <summary>
/// Computes goal vs actual per target/filter. Goals come from TS <c>exposure_plan.desired_count</c> (summed over
/// the plans whose template wears that filter); actuals come from the disk <c>inventory_filter</c> aggregates —
/// <b>not</b> from TS's own <c>acquired_count</c>, which is frequently stale (the whole reason the catalog anchors
/// to disk). The join key is <c>(target, filter_name)</c>; both planes already use the same single-letter filter
/// names. Pure and deterministic.
/// </summary>
public static class Reconciler
{
    /// <summary>Reconciles every target's goals against its disk actuals under <paramref name="policy"/>.</summary>
    public static IReadOnlyList<TargetReconciliation> Reconcile(
        IReadOnlyList<Target> targets,
        IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates,
        IReadOnlyList<InventoryFilter> inventory,
        ReconcilePolicy policy = ReconcilePolicy.Combined)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(inventory);

        Dictionary<Guid, string> templateFilter = [];
        foreach (ExposureTemplate t in templates)
            templateFilter[t.Id] = t.FilterName;

        // target -> (filter -> desired)
        Dictionary<Guid, Dictionary<string, int>> goals = [];
        foreach (ExposurePlan p in plans)
        {
            if (!templateFilter.TryGetValue(p.ExposureTemplateId, out string? filter)) continue;
            Dictionary<string, int> byFilter = ByFilter(goals, p.TargetId, () => new(StringComparer.OrdinalIgnoreCase));
            byFilter[filter] = byFilter.GetValueOrDefault(filter, 0) + p.DesiredCount;
        }

        // target -> (filter -> light/stars frame counts + integration hours)
        Dictionary<Guid, Dictionary<string, Actual>> actuals = [];
        foreach (InventoryFilter f in inventory)
        {
            Dictionary<string, Actual> byFilter = ByFilter(actuals, f.TargetId, () => new(StringComparer.OrdinalIgnoreCase));
            Actual a = byFilter.GetValueOrDefault(f.FilterName);
            byFilter[f.FilterName] = f.Purpose == FilterPurpose.Stars
                ? a with { Stars = a.Stars + f.ExposureCount, Hours = a.Hours + (f.TotalIntegrationSeconds / 3600.0) }
                : a with { Light = a.Light + f.ExposureCount, Hours = a.Hours + (f.TotalIntegrationSeconds / 3600.0) };
        }

        List<TargetReconciliation> result = new(targets.Count);
        foreach (Target t in targets)
        {
            Dictionary<string, int>? g = goals.GetValueOrDefault(t.Id);
            Dictionary<string, Actual>? a = actuals.GetValueOrDefault(t.Id);

            IEnumerable<string> filterKeys = (g?.Keys ?? Enumerable.Empty<string>())
                .Union(a?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            List<FilterReconciliation> filters = [];
            foreach (string key in filterKeys)
            {
                int desired = g?.GetValueOrDefault(key, 0) ?? 0;
                Actual act = a?.GetValueOrDefault(key) ?? default;
                int acquired = policy == ReconcilePolicy.LightOnly ? act.Light : act.Light + act.Stars;
                filters.Add(new FilterReconciliation(
                    key, desired, acquired, act.Light, act.Stars, act.Hours, FilterStatus(desired, acquired)));
            }

            result.Add(new TargetReconciliation(t.Id, t.Name, t.Source, RollUp(filters), filters));
        }
        return result;
    }

    private readonly record struct Actual(int Light, int Stars, double Hours);

    private static Dictionary<string, TValue> ByFilter<TValue>(
        Dictionary<Guid, Dictionary<string, TValue>> map, Guid key, Func<Dictionary<string, TValue>> create)
    {
        if (!map.TryGetValue(key, out Dictionary<string, TValue>? inner))
            map[key] = inner = create();
        return inner;
    }

    private static ReconcileStatus FilterStatus(int desired, int acquired) =>
        desired <= 0 ? (acquired > 0 ? ReconcileStatus.Unplanned : ReconcileStatus.NotStarted)
        : acquired <= 0 ? ReconcileStatus.NotStarted
        : acquired >= desired ? ReconcileStatus.Complete
        : ReconcileStatus.InProgress;

    private static ReconcileStatus RollUp(List<FilterReconciliation> filters)
    {
        List<FilterReconciliation> planned = filters.Where(f => f.DesiredCount > 0).ToList();
        if (planned.Count == 0)
            return filters.Exists(f => f.AcquiredCount > 0) ? ReconcileStatus.Unplanned : ReconcileStatus.NotStarted;
        if (planned.TrueForAll(f => f.Status == ReconcileStatus.Complete)) return ReconcileStatus.Complete;
        if (planned.TrueForAll(f => f.AcquiredCount == 0)) return ReconcileStatus.NotStarted;
        return ReconcileStatus.InProgress;
    }
}
