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

    /// <summary>
    /// Merges several targets' reconciliations into one aggregate under <paramref name="name"/>: per-filter
    /// goals and actuals sum across the inputs (the caller chooses the grouping — e.g. a hierarchical
    /// target's children under their parent). Per-filter remaining still can't be masked by another filter's
    /// overshoot; statuses are recomputed from the merged sums.
    /// </summary>
    public static TargetReconciliation Merge(
        Guid targetId, string name, TargetSource source, IEnumerable<TargetReconciliation> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        List<FilterReconciliation> merged = [.. parts
            .SelectMany(r => r.Filters)
            .GroupBy(f => f.Filter, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                int desired = g.Sum(f => f.DesiredCount);
                int acquired = g.Sum(f => f.AcquiredCount);
                return new FilterReconciliation(g.Key, desired, acquired,
                    g.Sum(f => f.LightCount), g.Sum(f => f.StarsCount), g.Sum(f => f.IntegrationHours),
                    FilterStatus(desired, acquired));
            })];
        return new TargetReconciliation(targetId, name, source, RollUp(merged), merged);
    }

    /// <summary>
    /// Folds hierarchical reconciliations onto their family roots: every reconciliation whose target has a
    /// <see cref="Target.ParentTargetId"/> merges (via <see cref="Merge"/>) with its siblings — and with the
    /// parent's own, typically empty, reconciliation — under the parent's identity, so a hierarchical target
    /// reads as one line. Standalone targets pass through unchanged; result order follows the first appearance
    /// of each family root in <paramref name="reconciliations"/>.
    /// </summary>
    public static IReadOnlyList<TargetReconciliation> MergeFamilies(
        IReadOnlyList<Target> targets, IReadOnlyList<TargetReconciliation> reconciliations)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(reconciliations);

        Dictionary<Guid, Target> byId = targets.ToDictionary(t => t.Id);
        Dictionary<Guid, List<TargetReconciliation>> families = [];
        List<Guid> rootOrder = [];
        foreach (TargetReconciliation r in reconciliations)
        {
            Guid root = byId.TryGetValue(r.TargetId, out Target? t) && t.ParentTargetId is Guid p ? p : r.TargetId;
            if (!families.TryGetValue(root, out List<TargetReconciliation>? members))
            {
                families[root] = members = [];
                rootOrder.Add(root);
            }
            members.Add(r);
        }

        List<TargetReconciliation> result = new(rootOrder.Count);
        foreach (Guid root in rootOrder)
        {
            List<TargetReconciliation> members = families[root];
            if (members.Count == 1 && members[0].TargetId == root)
            {
                result.Add(members[0]);
                continue;
            }
            Target parent = byId[root];   // self-FK guarantees the root exists when any child does
            result.Add(Merge(parent.Id, parent.Name, parent.Source, members));
        }
        return result;
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
