using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// Pure planner (no database I/O) for the surgical <c>tcm writeback --target</c> path: turns a single target's
/// freshly-scanned <i>units</i> into a <see cref="WriteBackPlan"/> the shared <see cref="TargetSchedulerWriter"/>
/// applies. Unlike <see cref="WriteBackPlanner"/> (which rebuilds the whole catalog and works off the resolved
/// graph), this matches one disk target directly against the TS plan plane:
/// <list type="bullet">
///   <item><b>Unit → TS target</b> by coordinates: a normal target is one unit anchored to the nearest TS target;
///   a mosaic is one unit per panel, each anchored to the nearest panel <i>within the same-named isMosaic
///   project</i> (name-matched first, exactly like <see cref="TargetResolver"/>).</item>
///   <item><b>Cell → TS plan</b> by <c>(filter, purpose, binning, whole-second exposure)</c>: each per-sub-length
///   aggregate lands on the plan whose template matches all four — a 2×2 <c>Stars B</c> cell can't write a 1×1
///   plan, and 600 s frames can't satisfy a 900 s plan.</item>
/// </list>
/// Disk is the single source of truth, so a matched cell takes the disk count verbatim (the writer ratchets
/// <c>desired</c> up). A cell whose duration no plan targets is an informational
/// <see cref="ReconcileNote.UnplannedFramesKind"/> note (write-back updates existing plan rows only); a cell whose
/// only same-duration plans sit at a different binning, or with several plans at the full key, is
/// <b>reported for manual resolution, never forced</b>. Unlike the bulk planner, plans with no matching cell are
/// left untouched (never zeroed): this is a per-cell push tool, and a partial or unconventional directory scan
/// must not be silently destructive to the anchored target's other plans.
/// Transitional, like all TS interop: retires at the IS/ISP cutover.
/// </summary>
public static class SingleTargetPlanner
{
    /// <summary>Builds the write-back plan for one scanned target.</summary>
    /// <param name="units">The target's units from <see cref="ImageLibraryScanner.ScanUnitsAsync"/> (1 for a normal target, N panels for a mosaic).</param>
    /// <param name="isMosaic">Whether <paramref name="dirName"/> is a <see cref="MosaicConvention">mosaic</see> directory.</param>
    /// <param name="dirName">The target's top-level directory name (used to name-match the TS isMosaic project for a mosaic).</param>
    /// <param name="ts">The TS plan snapshot.</param>
    /// <param name="options">Match tolerance (default 0.5°).</param>
    public static WriteBackPlan Plan(
        IReadOnlyList<TargetReport> units,
        bool isMosaic,
        string dirName,
        TsPlanData ts,
        ResolveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentException.ThrowIfNullOrWhiteSpace(dirName);
        ArgumentNullException.ThrowIfNull(ts);
        double tolerance = (options ?? ResolveOptions.Default).MatchToleranceDegrees;

        List<PlannedWrite> writes = [];
        List<ManualGroup> manual = [];
        List<ReconcileNote> needs = [];

        // ---- Candidate TS targets for coordinate anchoring. -------------------------------------------------
        IReadOnlyList<TsTarget> candidates;
        if (isMosaic)
        {
            // A mosaic anchors only within its same-named isMosaic project's panels (panels spread beyond tolerance
            // from the mosaic centroid, so they're name-matched first, then each panel coord-matched within).
            TsProject? project = ts.Projects
                .Where(p => p.IsMosaic != 0)
                .FirstOrDefault(p => string.Equals(
                    TargetResolver.Normalize(p.Name), TargetResolver.Normalize(dirName), StringComparison.Ordinal));
            if (project is null)
            {
                needs.Add(new ReconcileNote("MosaicUnmatched", dirName, "no isMosaic TS project with a matching name"));
                return new WriteBackPlan([], [], needs, IgnoredMissing: 0);
            }
            candidates = [.. ts.Targets.Where(t => t.ProjectId == project.Id)];
        }
        else
        {
            // A normal target anchors against standalone TS targets only — never a mosaic panel (those match by
            // name, mirroring TargetResolver's coordinate exclusion).
            HashSet<long> mosaicProjectIds = [.. ts.Projects.Where(p => p.IsMosaic != 0).Select(p => p.Id)];
            candidates = [.. ts.Targets.Where(t => t.ProjectId is long pid && !mosaicProjectIds.Contains(pid))];
        }

        Dictionary<long, TsExposureTemplate> templateById = ts.Templates.ToDictionary(t => t.Id);
        ILookup<long, TsExposurePlan> plansByTarget = ts.Plans.ToLookup(p => p.TargetId);

        // ---- Anchor each unit, then route each of its cells. ------------------------------------------------
        foreach (TargetReport unit in units)
        {
            List<(TsTarget Ts, double Sep)> near = [.. candidates
                .Where(c => c.Ra is double && c.Dec is double)
                .Select(c => (Ts: c, Sep: TargetResolver.SeparationDegrees(
                    unit.RaHours, unit.DecDegrees, c.Ra!.Value, c.Dec!.Value)))
                .Where(x => x.Sep <= tolerance)
                .OrderBy(x => x.Sep)];

            if (near.Count == 0)
            {
                needs.Add(new ReconcileNote("UnitUnmatched", unit.DirectoryName,
                    $"no TS target within {tolerance:0.00} deg"));
                continue;
            }
            if (near.Count > 1)
            {
                // Two TS targets in tolerance — anchoring is ambiguous; don't guess which one's counts to overwrite.
                needs.Add(new ReconcileNote("UnitAmbiguous", unit.DirectoryName,
                    $"{near.Count} TS targets within {tolerance:0.00} deg (nearest {near[0].Sep:0.000})"));
                continue;
            }

            TsTarget matched = near[0].Ts;
            List<TsExposurePlan> targetPlans = [.. plansByTarget[matched.Id]];

            // Each per-sub-length aggregate routes on its own: the plan's duration is its spec, so a cell
            // only ever writes a plan at exactly its (filter, purpose, binning, seconds).
            foreach (FilterAggregate cell in unit.Filters)
                RouteCell(cell, matched, targetPlans, templateById, writes, manual, needs);
        }

        return new WriteBackPlan(writes, manual, needs, IgnoredMissing: 0);
    }

    // Matches one disk cell (filter, purpose, binning, seconds) to its TS plan on the anchored target.
    // Exactly one match → an auto-write; several → manual; none → a same-seconds plan at another binning is an
    // equipment-identity question (manual with context), otherwise the cell's duration simply has no plan and
    // becomes an informational note (write-back never creates plans).
    private static void RouteCell(
        FilterAggregate cell,
        TsTarget matched,
        List<TsExposurePlan> targetPlans,
        Dictionary<long, TsExposureTemplate> templateById,
        List<PlannedWrite> writes,
        List<ManualGroup> manual,
        List<ReconcileNote> needs)
    {
        int cellSeconds = (int)Math.Round(cell.Typical.ExposureSec);

        int PlanSeconds(TsExposurePlan p) => EffectiveExposure.Seconds(p, templateById[p.ExposureTemplateId]);

        bool MatchesFilterPurpose(TsExposurePlan p) =>
            templateById.TryGetValue(p.ExposureTemplateId, out TsExposureTemplate? tpl)
            && string.Equals(tpl.FilterName, cell.FilterName, StringComparison.OrdinalIgnoreCase)
            && FilterPurposeClassifier.Classify(tpl.Name) == cell.Purpose;

        // Filter + purpose + binning + seconds is the full key — a square-binned cell only writes a
        // like-binned plan, and frames only count toward a plan at exactly their duration.
        List<TsExposurePlan> matches = [.. targetPlans.Where(p =>
            MatchesFilterPurpose(p)
            && templateById[p.ExposureTemplateId].Bin == cell.Typical.Binning.X
            && PlanSeconds(p) == cellSeconds)];

        if (matches.Count == 1)
        {
            writes.Add(new PlannedWrite(
                matches[0].Id, Guid.Empty, matched.Name, cell.FilterName, cell.Purpose, cellSeconds,
                cell.ExposureCount));
            return;
        }

        if (matches.Count == 0)
        {
            // Same-duration plans at another binning are shown as context (e.g. a 1×1 plan when the disk
            // cell is 2×2) — a human call. No plan at this duration at all → informational note.
            List<TsExposurePlan> otherBin = [.. targetPlans.Where(p =>
                MatchesFilterPurpose(p) && PlanSeconds(p) == cellSeconds)];
            if (otherBin.Count == 0)
            {
                needs.Add(new ReconcileNote(ReconcileNote.UnplannedFramesKind, matched.Name,
                    $"{cell.FilterName} {cell.Purpose} {cell.ExposureCount} frames @{cellSeconds}s " +
                    $"(bin {cell.Typical.Binning.X}) - no TS plan at {cellSeconds}s"));
                return;
            }

            manual.Add(new ManualGroup(
                Guid.Empty, matched.Name, cell.FilterName, cell.Purpose, cellSeconds, cell.ExposureCount,
                ManualReason.NoMatchingPlan,
                [.. otherBin.Select(p => new ManualPlan(p.Id, PlanSeconds(p), p.Acquired, p.Accepted, p.Desired))]));
            return;
        }

        manual.Add(new ManualGroup(
            Guid.Empty, matched.Name, cell.FilterName, cell.Purpose, cellSeconds, cell.ExposureCount,
            ManualReason.MultiPlan,
            [.. matches.Select(p => new ManualPlan(p.Id, PlanSeconds(p), p.Acquired, p.Accepted, p.Desired))]));
    }
}
