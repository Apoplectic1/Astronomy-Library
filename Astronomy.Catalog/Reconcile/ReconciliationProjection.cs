using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;

namespace Astronomy.Catalog.Reconcile;

/// <summary>
/// One reconciliation cell: a (filter, purpose, whole-second exposure) bucket joining the plan side
/// (summed <see cref="Desired"/>/<see cref="Acquired"/>/<see cref="Accepted"/> over <see cref="PlanCount"/>
/// plans) to the disk side (<see cref="Disk"/> frames). The filter casing is the first spelling seen for the
/// bucket. UI-agnostic by design: pairing cells into planes, rolling up mixed sub lengths, and pricing frames
/// into hours are all the consumer's presentation — the projection stops at the counts.
/// </summary>
public sealed record ReconciliationCell(
    string Filter,
    FilterPurpose Purpose,
    int Seconds,
    int Desired,
    int Acquired,
    int Accepted,
    int Disk,
    int PlanCount);

/// <summary>
/// One canonical target's reconciliation: its identity + match-state plus its <see cref="Cells"/>, which are
/// empty for a mosaic parent (a grouping node with no plans or inventory) or a target with neither a plan nor
/// scanned frames. Finer than the per-filter <see cref="TargetReconciliation"/>: this is the
/// (filter, purpose, seconds) projection a grid-style consumer shapes. A mosaic panel is emitted as its own
/// <see cref="TargetCells"/> with <see cref="ParentTargetId"/> set; the consumer reconstructs the parent/child
/// grouping (panels follow their parent in graph order).
/// </summary>
public sealed record TargetCells(
    Guid TargetId,
    Guid? ParentTargetId,
    string Name,
    TargetSource Source,
    string ProjectName,
    string? DirectoryName,
    bool IsMosaicDirectory,
    TargetMatchIssues Issues,
    bool IsUnanchored,
    IReadOnlyList<ReconciliationCell> Cells);

/// <summary>
/// Projects a resolved <see cref="CatalogGraph"/> (with its <see cref="CatalogBuildReport"/>) into per-target
/// reconciliation cells: for each canonical target, the plan commitments and disk actuals aggregated per
/// (filter, purpose, whole-second exposure), tagged with the target's match-state. This is the reusable
/// "goal vs actual at cell granularity" join; pairing the cells into planes, rollups, and hours is the
/// consumer's concern. (Lifted out of TCM's grid loader so the join is library-tested and reusable.)
/// </summary>
public static class ReconciliationProjection
{
    /// <summary>Projects one <see cref="TargetCells"/> per target in <paramref name="graph"/>, in graph order
    /// (so a mosaic's panels follow their parent). Pure; safe to call on a background thread.</summary>
    public static IReadOnlyList<TargetCells> Project(CatalogGraph graph, CatalogBuildReport report)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<Guid, Project> projects = graph.Projects.ToDictionary(p => p.Id);
        Dictionary<Guid, ExposureTemplate> templates = graph.Templates.ToDictionary(t => t.Id);
        ILookup<Guid, ExposurePlan> plansByTarget = graph.Plans.ToLookup(p => p.TargetId);
        ILookup<Guid, InventoryFilter> invByTarget = graph.InventoryFilters.ToLookup(i => i.TargetId);

        List<TargetCells> result = new(graph.Targets.Count);
        foreach (Target t in graph.Targets)
        {
            string project = t.ProjectId is Guid pid && projects.TryGetValue(pid, out Project? proj)
                ? proj.Name : "—";
            string? dir = t.DirectoryName;
            bool isMosaic = dir is not null && MosaicConvention.IsMosaicDirectory(dir);
            bool isUnanchored = t.Source == TargetSource.Planned && report.IsUnanchoredName(t.Name);

            // Aggregate plans and inventory per (filter, purpose, exposure seconds), filter case-insensitive;
            // the first original-case spelling seen wins as the cell's display Filter.
            Dictionary<(string Filter, FilterPurpose Purpose, int Seconds), CellAccumulator> cells = [];
            foreach (ExposurePlan p in plansByTarget[t.Id])
            {
                if (!templates.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl)) continue;
                int seconds = EffectiveExposure.Seconds(p, tpl);
                CellAccumulator c = Cell(cells, tpl.FilterName, FilterPurposeClassifier.Classify(tpl.Name), seconds);
                c.Desired += p.DesiredCount;
                c.Acquired += p.AcquiredCount;
                c.Accepted += p.AcceptedCount;
                c.PlanCount++;
            }
            foreach (InventoryFilter f in invByTarget[t.Id])
            {
                // The scanner already buckets aggregates to whole seconds (ExposureSeconds is identity).
                CellAccumulator c = Cell(cells, f.FilterName, f.Purpose, (int)Math.Round(f.ExposureSeconds));
                c.Disk += f.ExposureCount;
            }

            result.Add(new TargetCells(
                t.Id, t.ParentTargetId, t.Name, t.Source, project, dir, isMosaic,
                report.IssuesFor(dir), isUnanchored,
                [.. cells.Values.Select(c => c.ToCell())]));
        }
        return result;
    }

    private static CellAccumulator Cell(
        Dictionary<(string, FilterPurpose, int), CellAccumulator> cells,
        string filter, FilterPurpose purpose, int seconds)
    {
        (string, FilterPurpose, int) key = (filter.ToUpperInvariant(), purpose, seconds);
        if (!cells.TryGetValue(key, out CellAccumulator? cell))
            cells[key] = cell = new CellAccumulator(filter, purpose, seconds);
        return cell;
    }

    /// <summary>Mutable per-bucket tally; sealed to a <see cref="ReconciliationCell"/> once all rows are folded in.</summary>
    private sealed class CellAccumulator(string filter, FilterPurpose purpose, int seconds)
    {
        public int Desired;
        public int Acquired;
        public int Accepted;
        public int Disk;
        public int PlanCount;

        public ReconciliationCell ToCell() =>
            new(filter, purpose, seconds, Desired, Acquired, Accepted, Disk, PlanCount);
    }
}
