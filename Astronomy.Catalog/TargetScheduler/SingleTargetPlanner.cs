using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// Pure planner (no database I/O) for the surgical single-target write-back path: turns a single target's
/// freshly-scanned <i>units</i> into a <see cref="WriteBackPlan"/> the shared <see cref="TargetSchedulerWriter"/>
/// applies. Unlike <see cref="WriteBackPlanner"/> (which rebuilds the whole catalog and works off the resolved
/// graph), this matches one disk target directly against the TS plan plane:
/// <list type="bullet">
///   <item><b>Unit → TS target</b> by coordinates: a normal target is one unit anchored to the nearest TS target;
///   a mosaic is one unit per panel, each anchored to the nearest panel <i>within the same-named isMosaic
///   project</i> (name-matched first, exactly like <see cref="TargetResolver"/>).</item>
///   <item><b>Cell → TS plan</b> by <c>(filter, purpose, whole-second exposure)</c> plus the shared
///   capture-configuration pairing rule (<see cref="CaptureConfigPairing"/> — gain/offset/binning value
///   equality, a camera-default sentinel pairs with nothing): a 2×2 cell can't write a 1×1 plan, gain-53
///   frames can't satisfy a gain-0 plan, and 600 s frames can't satisfy a 900 s plan.</item>
/// </list>
/// Disk is the single source of truth, so a matched cell takes the disk count verbatim (the writer ratchets
/// <c>desired</c> up). A cell whose duration no plan targets is an informational
/// <see cref="ReconcileNote.UnplannedFramesKind"/> note (write-back updates existing plan rows only); a cell whose
/// only same-duration plans sit at a different configuration, or with several plans at the full key, is
/// <b>reported for manual resolution, never forced</b>. Unlike the bulk planner, plans with no matching cell are
/// left untouched (never zeroed): this is a per-cell push tool, and a partial or unconventional directory scan
/// must not be silently destructive to the anchored target's other plans.
/// Transitional, like all TS interop: retires at the IS cutover.
/// </summary>
public static class SingleTargetPlanner
{
    /// <summary>Builds the write-back plan for one scanned target.</summary>
    /// <param name="units">The target's units from <see cref="ImageLibraryScanner.ScanUnitsAsync"/> (1 for a normal target, N panels for a mosaic).</param>
    /// <param name="isMosaic">Whether <paramref name="dirName"/> is a <see cref="MosaicConvention">mosaic</see> directory.</param>
    /// <param name="dirName">The target's top-level directory name (used to name-match the TS isMosaic project for a mosaic).</param>
    /// <param name="ts">The TS plan snapshot.</param>
    /// <param name="options">Match tolerances (default 0.5°; an unaligned mosaic-panel claim is limited to the
    /// tighter panel radius — see <see cref="ResolveOptions.PanelMatchToleranceDegrees"/>).</param>
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
        ResolveOptions opts = options ?? ResolveOptions.Default;
        double tolerance = opts.MatchToleranceDegrees;

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
                    TargetResolver.MosaicKey(p.Name), TargetResolver.MosaicKey(dirName), StringComparison.Ordinal));
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
            // Panels gate their radius on name alignment, mirroring TargetResolver: an aligned TS panel
            // (its name ends with the directory's derived token) anchors within the full tolerance, an
            // unaligned one only within the tight panel radius.
            string unitToken = TargetResolver.PanelToken(unit.DirectoryName.Split('/', '\\')[^1]);
            bool IsAligned(TsTarget c) => TargetResolver.TokenAligned(c.Name, unitToken);

            List<(TsTarget Ts, double Sep)> near = [.. candidates
                .Where(c => c.Ra is double && c.Dec is double)
                .Select(c => (Ts: c, Sep: TargetResolver.SeparationDegrees(
                    unit.RaHours, unit.DecDegrees, c.Ra!.Value, c.Dec!.Value)))
                .Where(x => x.Sep <= (isMosaic && !IsAligned(x.Ts) ? opts.PanelMatchToleranceDegrees : tolerance))
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

        // A cell whose framing does not serve the target's rotation (the shared ServesPlanRotation rule)
        // must not stamp its count onto the plan — those frames belong to an abandoned framing, and
        // crediting them would leave TS believing progress that does not exist for the re-framed field.
        // Surfaced as a note rather than dropped silently: this is the surgical per-target path, and a
        // count that visibly did not move deserves its stated reason.
        if (!FramingCluster.ServesPlanRotation(
                cell.Framing.Expression, cell.Framing.FoldAngleDegrees, matched.Rotation))
        {
            needs.Add(new ReconcileNote("FramingMismatch", matched.Name,
                $"{cell.FilterName} {cell.Purpose} {cell.ExposureCount} frames @{cellSeconds}s at framing " +
                $"{cell.Framing.FoldAngleDegrees:0.#}° do not serve target rotation {matched.Rotation:0.#}°"));
            return;
        }

        int PlanSeconds(TsExposurePlan p) => EffectiveExposure.Seconds(p, templateById[p.ExposureTemplateId]);

        bool MatchesFilterPurpose(TsExposurePlan p) =>
            templateById.TryGetValue(p.ExposureTemplateId, out TsExposureTemplate? tpl)
            && string.Equals(tpl.FilterName, cell.FilterName, StringComparison.OrdinalIgnoreCase)
            && FilterPurposeClassifier.Classify(tpl.Name) == cell.Purpose;

        // Filter + purpose + seconds + the shared configuration pairing is the full key — a square-binned
        // cell only writes a like-binned plan, frames only count toward a plan at exactly their duration,
        // and a plan's expressed gain/offset must equal the cell's (a sentinel template pairs with nothing).
        bool ConfigPairs(TsExposurePlan p)
        {
            TsExposureTemplate tpl = templateById[p.ExposureTemplateId];
            return CaptureConfigPairing.Pairs(
                tpl.Gain, tpl.Offset, tpl.Bin,
                cell.Typical.Gain, cell.Typical.Offset, cell.Typical.Binning.X, cell.Typical.Binning.Y);
        }
        List<TsExposurePlan> matches = [.. targetPlans.Where(p =>
            MatchesFilterPurpose(p)
            && ConfigPairs(p)
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
            // Same-duration plans at another configuration (binning, gain or offset — including a sentinel
            // template, which pairs with nothing) are shown as context — a human call, with the cell's
            // configuration named so the withheld count states its reason. No plan at this duration at
            // all → informational note.
            List<TsExposurePlan> otherConfig = [.. targetPlans.Where(p =>
                MatchesFilterPurpose(p) && PlanSeconds(p) == cellSeconds)];
            if (otherConfig.Count == 0)
            {
                needs.Add(new ReconcileNote(ReconcileNote.UnplannedFramesKind, matched.Name,
                    $"{cell.FilterName} {cell.Purpose} {cell.ExposureCount} frames @{cellSeconds}s " +
                    $"(gain {cell.Typical.Gain} offset {cell.Typical.Offset} bin {cell.Typical.Binning.X}) " +
                    $"- no TS plan at {cellSeconds}s"));
                return;
            }

            manual.Add(new ManualGroup(
                Guid.Empty, matched.Name, cell.FilterName, cell.Purpose, cellSeconds, cell.ExposureCount,
                ManualReason.NoMatchingPlan,
                [.. otherConfig.Select(p => new ManualPlan(p.Id, PlanSeconds(p), p.Acquired, p.Accepted, p.Desired))]));
            return;
        }

        manual.Add(new ManualGroup(
            Guid.Empty, matched.Name, cell.FilterName, cell.Purpose, cellSeconds, cell.ExposureCount,
            ManualReason.MultiPlan,
            [.. matches.Select(p => new ManualPlan(p.Id, PlanSeconds(p), p.Acquired, p.Accepted, p.Desired))]));
    }
}
