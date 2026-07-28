using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;

namespace Astronomy.Catalog.Reconcile;

/// <summary>
/// One reconciliation cell: a <b>capture configuration</b> bucket — (filter, purpose, whole-second exposure,
/// gain, offset, binning) — joining the plan side (summed <see cref="Desired"/>/<see cref="Acquired"/>/
/// <see cref="Accepted"/> over <see cref="PlanCount"/> plans) to the disk side (<see cref="Disk"/> frames).
/// The filter casing is the first spelling seen for the bucket.
/// <para>
/// A cell carries both planes only when they agree on <b>every</b> dimension both can express; frames captured
/// at a gain, offset or binning the plan does not specify form their own cell with no plan side, which is how
/// the consumer learns that captured history does not describe planned capture.
/// <see cref="Camera"/> is disk-side only — a plan cannot name a camera — so it never prevents a cell from
/// carrying both planes; it is <c>null</c> on a plan-only cell.
/// </para>
/// UI-agnostic by design: pairing cells into planes, rolling up mixed sub lengths, and pricing frames
/// into hours are all the consumer's presentation — the projection stops at the counts.
/// <para>
/// <see cref="PlanTsKey"/> / <see cref="TemplateTsKey"/> are the write-back addresses for a <b>single-plan</b>
/// cell (<see cref="PlanCount"/> == 1): the lone plan's and its template's <c>imported_from_ts_guid</c>, so a
/// consumer can edit that plan's <c>desired</c>/<c>exposure</c> or the template's gain/offset/exposure. Both are
/// <c>null</c> when the cell aggregates several plans (no unambiguous single row to write) or has no plan side.
/// </para>
/// </summary>
public sealed record ReconciliationCell(
    string Filter,
    FilterPurpose Purpose,
    int Seconds,
    int Desired,
    int Acquired,
    int Accepted,
    int Disk,
    int PlanCount,
    string? PlanTsKey = null,
    string? TemplateTsKey = null,
    bool? PlanEnabled = null,
    int Gain = 0,
    int Offset = 0,
    int BinningX = 1,
    int BinningY = 1,
    string? Camera = null,
    bool CameraDisagrees = false);

/// <summary>
/// One canonical target's reconciliation: its identity + match-state plus its <see cref="Cells"/>, which are
/// empty for a mosaic parent (a grouping node with no plans or inventory) or a target with neither a plan nor
/// scanned frames. Finer than the per-filter <see cref="TargetReconciliation"/>: this is the
/// (filter, purpose, seconds) projection a grid-style consumer shapes. A mosaic panel is emitted as its own
/// <see cref="TargetCells"/> with <see cref="ParentTargetId"/> set; the consumer reconstructs the parent/child
/// grouping (panels follow their parent in graph order). <see cref="Enabled"/> and <see cref="TsTargetKey"/>
/// carry the target's TS-enable state and write-back provenance; a <see cref="TsTargetKey"/> of <c>null</c> means
/// there is no TS target behind this row (a disk-only target or a mosaic parent grouping node).
/// <see cref="ProjectTsKey"/> is the write-back address for the target's TS project (its
/// <c>imported_from_ts_guid</c>), so a consumer can edit project-scope knobs (state/priority/altitudes/…);
/// <c>null</c> when the target has no TS project.
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
    bool Enabled,
    string? TsTargetKey,
    string? ProjectTsKey,
    IReadOnlyList<ReconciliationCell> Cells);

/// <summary>
/// Projects a resolved <see cref="CatalogGraph"/> (with its <see cref="CatalogBuildReport"/>) into per-target
/// reconciliation cells: for each canonical target, the plan commitments and disk actuals aggregated per
/// (filter, purpose, whole-second exposure), tagged with the target's match-state. This is the reusable
/// "goal vs actual at cell granularity" join; pairing the cells into planes, rollups, and hours is the
/// consumer's concern. (Lifted out of a consumer's grid loader so the join is library-tested and reusable.)
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
            string project = "—";
            string? projectTsKey = null;
            if (t.ProjectId is Guid pid && projects.TryGetValue(pid, out Project? proj))
            {
                project = proj.Name;
                projectTsKey = proj.ImportedFromTsGuid;
            }
            string? dir = t.DirectoryName;
            bool isMosaic = dir is not null && MosaicConvention.IsMosaicDirectory(dir);
            bool isUnanchored = t.Source == TargetSource.Planned && report.IsUnanchoredName(t.Name);

            // Aggregate plans and inventory per CAPTURE CONFIGURATION — (filter, purpose, exposure seconds,
            // gain, offset, binning) — filter case-insensitive; the first original-case spelling seen wins as
            // the cell's display Filter. A plan and a disk aggregate share a cell only when they agree on every
            // one of those dimensions, so a plan specifying gain 0 never absorbs frames captured at gain 53.
            // Camera is deliberately absent from the key: a plan cannot name one, so including it would split
            // cells the plan can never be matched against.
            Dictionary<CellKey, CellAccumulator> cells = [];
            foreach (ExposurePlan p in plansByTarget[t.Id])
            {
                if (!templates.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl)) continue;
                int seconds = EffectiveExposure.Seconds(p, tpl);
                // A template's gain/offset may carry TS's "use the camera's default" sentinel (-1). That is a
                // plan which does not specify the value, and what the camera would default to is unknowable
                // here — so it is kept as its own key rather than assumed to match whatever was captured. Such
                // a plan forms its own cell and does not pair, which is the honest reading: nothing can be
                // asserted to agree with an unspecified value.
                int bin = tpl.Binning is int b && b > 0 ? b : 1;
                CellAccumulator c = Cell(cells, tpl.FilterName, FilterPurposeClassifier.Classify(tpl.Name),
                    seconds, tpl.Gain ?? -1, tpl.OffsetAdu ?? -1, bin, bin);
                c.AddPlan(p, tpl);
            }
            foreach (InventoryFilter f in invByTarget[t.Id])
            {
                // The scanner already buckets aggregates to whole seconds (ExposureSeconds is identity) and
                // every configuration field is uniform within an aggregate.
                CellAccumulator c = Cell(cells, f.FilterName, f.Purpose, (int)Math.Round(f.ExposureSeconds),
                    f.TypicalGain, f.TypicalOffset, f.TypicalBinningX, f.TypicalBinningY);
                c.AddDisk(f);
            }

            result.Add(new TargetCells(
                t.Id, t.ParentTargetId, t.Name, t.Source, project, dir, isMosaic,
                report.IssuesFor(dir), isUnanchored, t.Enabled, t.ImportedFromTsGuid, projectTsKey,
                [.. cells.Values.Select(c => c.ToCell())]));
        }
        return result;
    }

    /// <summary>The capture configuration identifying one cell. Camera is excluded deliberately — see
    /// <see cref="ReconciliationCell"/>.</summary>
    private readonly record struct CellKey(
        string Filter, FilterPurpose Purpose, int Seconds, int Gain, int Offset, int BinX, int BinY);

    private static CellAccumulator Cell(
        Dictionary<CellKey, CellAccumulator> cells,
        string filter, FilterPurpose purpose, int seconds, int gain, int offset, int binX, int binY)
    {
        CellKey key = new(filter.ToUpperInvariant(), purpose, seconds, gain, offset, binX, binY);
        if (!cells.TryGetValue(key, out CellAccumulator? cell))
            cells[key] = cell = new CellAccumulator(filter, purpose, seconds, gain, offset, binX, binY);
        return cell;
    }

    /// <summary>Mutable per-bucket tally; sealed to a <see cref="ReconciliationCell"/> once all rows are folded in.</summary>
    private sealed class CellAccumulator(
        string filter, FilterPurpose purpose, int seconds, int gain, int offset, int binX, int binY)
    {
        public int Desired;
        public int Acquired;
        public int Accepted;
        public int Disk;
        public int PlanCount;
        private string? _planTsKey;
        private string? _templateTsKey;
        private bool? _planEnabled;
        private string? _camera;
        private bool _cameraDisagrees;

        /// <summary>Folds one disk aggregate in. The camera is disk-side only; the configuration key already
        /// guarantees these frames share one, so the first seen is the cell's.</summary>
        public void AddDisk(InventoryFilter f)
        {
            Disk += f.ExposureCount;
            _camera ??= f.Camera;
            _cameraDisagrees |= f.CameraDisagrees;
        }

        // Fold one plan (and its resolved template) into the bucket; remember the TS keys, which only become a
        // usable write-back address when the bucket ends up with exactly one plan.
        public void AddPlan(ExposurePlan p, ExposureTemplate tpl)
        {
            Desired += p.DesiredCount;
            Acquired += p.AcquiredCount;
            Accepted += p.AcceptedCount;
            PlanCount++;
            _planTsKey = p.ImportedFromTsGuid;
            _templateTsKey = tpl.ImportedFromTsGuid;
            _planEnabled = p.Enabled;
        }

        public ReconciliationCell ToCell() =>
            new(filter, purpose, seconds, Desired, Acquired, Accepted, Disk, PlanCount,
                PlanCount == 1 ? _planTsKey : null,
                PlanCount == 1 ? _templateTsKey : null,
                PlanCount == 1 ? _planEnabled : null,
                gain, offset, binX, binY, _camera, _cameraDisagrees);
    }
}
