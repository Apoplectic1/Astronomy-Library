using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;

namespace Astronomy.Catalog.Build;

/// <summary>Knobs for <see cref="TargetResolver.Resolve"/>.</summary>
/// <param name="MatchToleranceDegrees">
/// Maximum angular separation for a TS target to be considered the same object as a disk target. Generous enough
/// for framing/recenter drift between the planned and plate-solved positions, tight enough to rarely collide with
/// a neighbouring target. A large mosaic whose panels spread beyond this from the folded centroid may miss — tune
/// after reviewing the first real-data <see cref="CatalogBuildReport"/>.
/// </param>
public sealed record ResolveOptions(double MatchToleranceDegrees = 0.5)
{
    /// <summary>The default options (0.5° tolerance).</summary>
    public static ResolveOptions Default { get; } = new();
}

/// <summary>
/// Reconciles the two source planes into one canonical target list. The disk library is ACTUAL (truth); TS is the
/// PLAN. Matching is <b>coordinate-primary</b>: each TS target is anchored to the nearest disk target within
/// <see cref="ResolveOptions.MatchToleranceDegrees"/> (name only validates the match); when they merge, the disk
/// (plate-solved) coordinates win and the TS guid is retained for write-back. TS targets with no disk match become
/// planned-only (goals, 0 actual); disk targets with no TS match become actual-only. TS duplicates that collapse
/// onto one disk target, name disagreements, ambiguous matches, and un-anchorable TS targets are reported, not
/// silently dropped. A fold whose every TS name exactly matches a disk identity facet is reported as an
/// <see cref="AliasTsTarget"/> (same object under different names) rather than a duplicate.
/// Pure and deterministic (no I/O, no clock) — pass the timestamp and disk/TS data in.
/// </summary>
public static class TargetResolver
{
    private const double DegPerRad = 180.0 / Math.PI;
    private const double RadPerDeg = Math.PI / 180.0;

    /// <summary>Resolves <paramref name="diskTargets"/> (actuals) against <paramref name="ts"/> (the plan).</summary>
    /// <param name="diskTargets">Per-target scanner aggregates from the image library.</param>
    /// <param name="ts">The TS plan snapshot (may be <see cref="TsPlanData.Empty"/>).</param>
    /// <param name="createdAtUnix">Build timestamp (UNIX seconds) stamped as created_at/scanned_at.</param>
    /// <param name="options">Match tolerance; defaults to <see cref="ResolveOptions.Default"/>.</param>
    public static (CatalogGraph Graph, CatalogBuildReport Report) Resolve(
        IReadOnlyList<TargetReport> diskTargets,
        TsPlanData ts,
        long createdAtUnix,
        ResolveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(diskTargets);
        ArgumentNullException.ThrowIfNull(ts);
        double tolerance = (options ?? ResolveOptions.Default).MatchToleranceDegrees;

        // ---- Profiles: one per distinct TS profileId (a NINA profile GUID string). --------------------------
        Dictionary<string, Guid> profileIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string profileId in ts.Projects.Select(p => p.ProfileId)
                     .Concat(ts.Templates.Select(t => t.ProfileId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            profileIds[profileId] = ParseOrDerive(profileId, $"profile:{profileId}");
        }

        List<Profile> profiles = [.. profileIds.Select(kv => new Profile(kv.Value, kv.Key, kv.Key, createdAtUnix))];

        // ---- Projects. --------------------------------------------------------------------------------------
        Dictionary<long, Guid> projectIds = [];
        List<Project> projects = new(ts.Projects.Count);
        foreach (TsProject p in ts.Projects)
        {
            Guid id = ParseOrDerive(p.TsGuid, $"project:{p.Id}");
            projectIds[p.Id] = id;
            projects.Add(new Project(
                id, profileIds[p.ProfileId], p.Name, Description: null,
                State: SafeState(p.State), Priority: SafeProjectPriority(p.Priority),
                MinimumAltitudeDeg: p.MinimumAltitude, MaximumAltitudeDeg: null, MinimumTimeMinutes: null,
                UseCustomHorizon: false, HorizonOffsetDeg: null, MeridianWindowMinutes: null,
                IsMosaic: p.IsMosaic != 0, EnableGrader: true,
                CreatedAt: createdAtUnix, ActiveAt: null, InactiveAt: null, ImportedFromTsGuid: Provenance(p.TsGuid, p.Id)));
        }

        // ---- Exposure templates. ----------------------------------------------------------------------------
        Dictionary<long, Guid> templateIds = [];
        List<ExposureTemplate> templates = new(ts.Templates.Count);
        foreach (TsExposureTemplate t in ts.Templates)
        {
            if (!profileIds.TryGetValue(t.ProfileId, out Guid profileGuid)) continue;
            Guid id = DeterministicGuid($"template:{t.Id}");
            templateIds[t.Id] = id;
            templates.Add(new ExposureTemplate(
                id, profileGuid, t.Name, t.FilterName, Gain: t.Gain, OffsetAdu: t.Offset, Binning: t.Bin,
                ReadoutMode: null, DefaultExposureSeconds: t.DefaultExposure,
                ImportedFromTsGuid: t.Id.ToString(CultureInfo.InvariantCulture)));
        }

        // ---- Disk working set (one canonical id per directory). ---------------------------------------------
        List<WorkingTarget> diskWorking = [.. diskTargets.Select(d =>
            new WorkingTarget(DeterministicGuid($"disk:{d.DirectoryName}"), d))];

        // ---- Resolve each TS target spatially onto the disk set. --------------------------------------------
        Dictionary<long, Guid> tsTargetToCanonical = [];
        List<Target> plannedTargets = [];
        List<NameMismatch> nameMismatches = [];
        List<AmbiguousMatch> ambiguousMatches = [];
        List<UnanchoredTsTarget> unanchored = [];
        List<InvalidTsTarget> invalidTsTargets = [];

        // ---- Mosaic pre-pass: name-match each disk "Mosaic - X" to the same-named isMosaic project (panels
        //      spread too far for the mosaic itself to coordinate-match). The project's panel targets are
        //      reserved here — skipped by the standalone loop below so they can't mis-anchor onto a
        //      spatially-overlapping standalone dir — and resolved per panel in BuildMosaicFamily.
        Dictionary<string, TsProject> mosaicProjectsByName = new(StringComparer.Ordinal);
        foreach (TsProject mp in ts.Projects.Where(p => p.IsMosaic != 0))
            mosaicProjectsByName[Normalize(mp.Name)] = mp;

        ILookup<long?, TsTarget> tsTargetsByProject = ts.Targets.ToLookup(t => t.ProjectId);
        HashSet<long> foldedPanelIds = [];
        int mosaicsResolved = 0;
        foreach (WorkingTarget mw in diskWorking)
        {
            if (!MosaicConvention.IsMosaicDirectory(mw.Disk.DirectoryName)) continue;
            if (!mosaicProjectsByName.TryGetValue(Normalize(mw.Disk.DirectoryName), out TsProject? proj)) continue;

            mw.MosaicProject = proj;
            mosaicsResolved++;
            foreach (TsTarget panel in tsTargetsByProject[proj.Id])
                foldedPanelIds.Add(panel.Id);
        }

        foreach (TsTarget tst in ts.Targets)
        {
            if (foldedPanelIds.Contains(tst.Id)) continue; // a folded mosaic panel — handled above
            if (tst.ProjectId is not long projectId || !projectIds.ContainsKey(projectId))
                continue; // orphan TS target (no project) — skip

            if (tst.Ra is not double raHours || tst.Dec is not double decDegrees)
            {
                // No coordinates — cannot anchor to disk; keep as planned-only and flag.
                Guid id = ParseOrDerive(tst.TsGuid, $"target:{tst.Id}");
                tsTargetToCanonical[tst.Id] = id;
                plannedTargets.Add(BuildPlanned(tst, id, projectIds[projectId], createdAtUnix));
                unanchored.Add(new UnanchoredTsTarget(tst.TsGuid, tst.Name));
                FlagIfSuspect(tst, invalidTsTargets);
                continue;
            }

            List<(WorkingTarget Work, double Sep)> candidates = [.. diskWorking
                .Where(w => !MosaicConvention.IsMosaicDirectory(w.Disk.DirectoryName))  // mosaics match by name, never coords
                .Select(w => (Work: w, Sep: SeparationDegrees(raHours, decDegrees, w.Disk.RaHours, w.Disk.DecDegrees)))
                .Where(x => x.Sep <= tolerance)
                .OrderBy(x => x.Sep)];

            if (candidates.Count == 0)
            {
                Guid id = ParseOrDerive(tst.TsGuid, $"target:{tst.Id}");
                tsTargetToCanonical[tst.Id] = id;
                plannedTargets.Add(BuildPlanned(tst, id, projectIds[projectId], createdAtUnix));
                FlagIfSuspect(tst, invalidTsTargets);
                continue;
            }

            (WorkingTarget nearest, double nearestSep) = candidates[0];
            nearest.AssignedTs.Add((tst, nearestSep));
            tsTargetToCanonical[tst.Id] = nearest.Id;

            if (candidates.Count > 1)
            {
                ambiguousMatches.Add(new AmbiguousMatch(
                    tst.TsGuid, tst.Name, [.. candidates.Select(c => c.Work.Disk.DirectoryName)], nearestSep));
            }

            if (!NameAligned(tst.Name, nearest.Disk))
            {
                nameMismatches.Add(new NameMismatch(
                    tst.TsGuid, tst.Name, nearest.Disk.DirectoryName, nearest.Disk.ObjectName, nearestSep));
            }
        }

        // ---- Build canonical disk targets (Actual or Both) + their inventory. -------------------------------
        List<Target> targets = new(diskWorking.Count + plannedTargets.Count);
        List<InventoryFilter> inventory = [];
        List<DuplicateTsTarget> duplicates = [];
        List<AliasTsTarget> aliases = [];
        List<AmbiguousPanel> ambiguousPanels = [];
        int bothCount = 0;
        int actualOnly = 0;
        int panelsMatched = 0;
        int panelsPlannedOnly = 0;
        int panelsActualOnly = 0;

        foreach (WorkingTarget w in diskWorking)
        {
            if (MosaicConvention.IsMosaicDirectory(w.Disk.DirectoryName))
            {
                BuildMosaicFamily(w);
                continue;
            }

            if (w.AssignedTs.Count == 0)
            {
                targets.Add(BuildActual(w.Disk, w.Id, createdAtUnix));
                actualOnly++;
            }
            else
            {
                TsTarget primary = w.AssignedTs.OrderBy(a => a.Sep).First().Ts;
                targets.Add(BuildBoth(w.Disk, primary, w.Id, projectIds, createdAtUnix));
                bothCount++;
                if (w.AssignedTs.Count > 1)
                {
                    string[] names = [.. w.AssignedTs.Select(a => a.Ts.Name)];
                    if (w.AssignedTs.All(a => IsAliasName(a.Ts.Name, w.Disk)))
                        aliases.Add(new AliasTsTarget(w.Disk.DirectoryName, names));
                    else
                        duplicates.Add(new DuplicateTsTarget(w.Disk.DirectoryName, names));
                }
            }

            foreach (FilterAggregate f in w.Disk.Filters)
                inventory.Add(ToInventoryFilter(w.Id, f));
        }

        targets.AddRange(plannedTargets);

        // One mosaic = one parent target (no plans, no inventory) + one child target per panel, appended
        // immediately after the parent (the self-referencing FK requires parents-before-children). Disk
        // panels coordinate-match the project's TS panel targets 1:1 (greedy nearest within tolerance);
        // unmatched sides become Actual / Planned children. Plans rewire to the children via
        // tsTargetToCanonical exactly like normal targets.
        void BuildMosaicFamily(WorkingTarget w)
        {
            string parentDir = w.Disk.DirectoryName;
            TsProject? proj = w.MosaicProject;

            if (proj is TsProject mosaic)
            {
                targets.Add(BuildBothMosaic(w.Disk, mosaic, w.Id, projectIds, createdAtUnix));
                bothCount++;
            }
            else
            {
                targets.Add(BuildActual(w.Disk, w.Id, createdAtUnix));
                actualOnly++;
            }

            IReadOnlyList<TargetReport> diskPanels = w.Disk.Panels;
            List<TsTarget> tsPanels = proj is null ? [] : [.. tsTargetsByProject[proj.Id]];

            // Degradation (no per-panel detail in the report): keep the aggregate inventory on the parent
            // and represent the project's TS panels as planned children.
            if (diskPanels.Count == 0)
            {
                foreach (FilterAggregate f in w.Disk.Filters)
                    inventory.Add(ToInventoryFilter(w.Id, f));
                foreach (TsTarget ts in tsPanels.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                    AddPlannedPanel(ts);
                return;
            }

            // Greedy nearest 1:1: every in-tolerance (disk panel, TS panel) pair sorted by separation; each
            // side is claimed once. A disk panel that saw 2+ candidates anchors to the nearest but is
            // reported as ambiguous (write-back holds it for manual resolution).
            List<TsTarget> coordful = [.. tsPanels.Where(t => t.Ra is double && t.Dec is double)];
            List<(TargetReport Panel, TsTarget Ts, double Sep)> pairs = [];
            Dictionary<string, int> candidateCounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (TargetReport p in diskPanels)
            {
                int count = 0;
                foreach (TsTarget t in coordful)
                {
                    double sep = SeparationDegrees(p.RaHours, p.DecDegrees, t.Ra!.Value, t.Dec!.Value);
                    if (sep > tolerance) continue;
                    pairs.Add((p, t, sep));
                    count++;
                }
                candidateCounts[p.DirectoryName] = count;
            }

            Dictionary<string, TsTarget> matchedTs = new(StringComparer.OrdinalIgnoreCase);
            HashSet<long> claimedTs = [];
            foreach ((TargetReport p, TsTarget t, double _) in pairs
                .OrderBy(x => x.Sep)
                .ThenBy(x => x.Panel.DirectoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Ts.Id))
            {
                if (matchedTs.ContainsKey(p.DirectoryName) || claimedTs.Contains(t.Id)) continue;
                matchedTs[p.DirectoryName] = t;
                claimedTs.Add(t.Id);
            }

            foreach (TargetReport p in diskPanels.OrderBy(p => p.DirectoryName, StringComparer.OrdinalIgnoreCase))
            {
                string childDir = MosaicConvention.PanelDirectoryName(parentDir, p.DirectoryName);
                Guid childId = DeterministicGuid($"disk:{childDir}");
                if (matchedTs.TryGetValue(p.DirectoryName, out TsTarget? ts))
                {
                    targets.Add(BuildBothPanel(p, parentDir, ts, childId, w.Id, projectIds, createdAtUnix));
                    tsTargetToCanonical[ts.Id] = childId;
                    panelsMatched++;
                    if (candidateCounts[p.DirectoryName] > 1)
                    {
                        List<(TargetReport Panel, TsTarget Ts, double Sep)> mine =
                            [.. pairs.Where(x => ReferenceEquals(x.Panel, p)).OrderBy(x => x.Sep)];
                        ambiguousPanels.Add(new AmbiguousPanel(
                            parentDir, childDir, [.. mine.Select(x => x.Ts.Name)], mine[0].Sep));
                    }
                }
                else
                {
                    targets.Add(BuildActualPanel(p, parentDir, childId, w.Id, createdAtUnix));
                    panelsActualOnly++;
                }

                foreach (FilterAggregate f in p.Filters)
                    inventory.Add(ToInventoryFilter(childId, f));
            }

            foreach (TsTarget ts in tsPanels
                .Where(t => !claimedTs.Contains(t.Id))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                AddPlannedPanel(ts);
            }

            void AddPlannedPanel(TsTarget ts)
            {
                Guid id = ParseOrDerive(ts.TsGuid, $"target:{ts.Id}");
                targets.Add(BuildPlanned(ts, id, projectIds[ts.ProjectId!.Value], createdAtUnix, w.Id));
                tsTargetToCanonical[ts.Id] = id;
                panelsPlannedOnly++;
                if (ts.Ra is not double || ts.Dec is not double)
                    unanchored.Add(new UnanchoredTsTarget(ts.TsGuid, ts.Name));
                FlagIfSuspect(ts, invalidTsTargets);
            }
        }

        // ---- Exposure plans, rewired to the canonical target id. --------------------------------------------
        List<ExposurePlan> plans = new(ts.Plans.Count);
        foreach (TsExposurePlan p in ts.Plans)
        {
            if (!tsTargetToCanonical.TryGetValue(p.TargetId, out Guid targetGuid)) continue;
            if (!templateIds.TryGetValue(p.ExposureTemplateId, out Guid templateGuid)) continue;
            plans.Add(new ExposurePlan(
                DeterministicGuid($"plan:{p.Id}"), targetGuid, templateGuid,
                ExposureSeconds: p.Exposure < 0 ? null : p.Exposure,
                DesiredCount: p.Desired, AcquiredCount: p.Acquired, AcceptedCount: p.Accepted,
                Enabled: true, ImportedFromTsGuid: p.Id.ToString(CultureInfo.InvariantCulture)));
        }

        CatalogGraph graph = new(profiles, projects, templates, targets, plans, inventory);
        CatalogBuildReport report = new(
            DiskTargetCount: diskTargets.Count, TsTargetCount: ts.Targets.Count,
            BothCount: bothCount, PlannedOnlyCount: plannedTargets.Count, ActualOnlyCount: actualOnly,
            NameMismatches: nameMismatches, AmbiguousMatches: ambiguousMatches,
            DuplicateTsTargets: duplicates, AliasTsTargets: aliases,
            UnanchoredTsTargets: unanchored, InvalidTsTargets: invalidTsTargets,
            MosaicsResolved: mosaicsResolved, PanelsMatched: panelsMatched,
            PanelsPlannedOnly: panelsPlannedOnly, PanelsActualOnly: panelsActualOnly,
            AmbiguousPanels: ambiguousPanels);
        return (graph, report);
    }

    // ---- Target builders ------------------------------------------------------------------------------------

    private static Target BuildActual(TargetReport d, Guid id, long now) => new(
        id, TargetSource.Actual, ProjectId: null, d.DirectoryName, Enabled: true,
        RaHours: d.RaHours, DecDegreesSigned: d.DecDegrees, Epoch.J2000, RotationDeg: null, RoiPercent: null,
        Priority: null, DirectoryName: d.DirectoryName, Catalog: d.Catalog, CommonName: d.CommonName,
        ObjectName: d.ObjectName, ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: null);

    private static Target BuildBoth(TargetReport d, TsTarget ts, Guid id, Dictionary<long, Guid> projectIds, long now) => new(
        id, TargetSource.Both, projectIds[ts.ProjectId!.Value], d.DirectoryName, Enabled: ts.Active != 0,
        // Disk coordinates win (plate-solved truth); the disk frames are J2000 astrometry.
        RaHours: d.RaHours, DecDegreesSigned: d.DecDegrees, Epoch.J2000, RotationDeg: ts.Rotation, RoiPercent: ts.Roi,
        Priority: SafeTargetPriority(ts.Priority),
        DirectoryName: d.DirectoryName, Catalog: d.Catalog, CommonName: d.CommonName, ObjectName: d.ObjectName,
        ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: Provenance(ts.TsGuid, ts.Id));

    // A mosaic disk target name-matched to an isMosaic project. The parent is a grouping node: its panel
    // children carry the plans and inventory, so it has no single TS target (ImportedFromTsGuid null) and the
    // project link carries the TS association. Disk coordinates are the panel-summed centroid (descriptive;
    // the match was by name).
    private static Target BuildBothMosaic(TargetReport d, TsProject mosaic, Guid id, Dictionary<long, Guid> projectIds, long now) => new(
        id, TargetSource.Both, projectIds[mosaic.Id], d.DirectoryName, Enabled: true,
        RaHours: d.RaHours, DecDegreesSigned: d.DecDegrees, Epoch.J2000, RotationDeg: null, RoiPercent: null,
        Priority: null, DirectoryName: d.DirectoryName, Catalog: d.Catalog, CommonName: d.CommonName,
        ObjectName: d.ObjectName, ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: null);

    // A panel anchored to its TS panel target: an ordinary Both target one level down. Disk coordinates win
    // and provenance is the TS panel target, so write-back addresses it like any other Both target.
    private static Target BuildBothPanel(
        TargetReport panel, string parentDir, TsTarget ts, Guid id, Guid parentId,
        Dictionary<long, Guid> projectIds, long now) => new(
        id, TargetSource.Both, projectIds[ts.ProjectId!.Value], ts.Name, Enabled: ts.Active != 0,
        RaHours: panel.RaHours, DecDegreesSigned: panel.DecDegrees, Epoch.J2000,
        RotationDeg: ts.Rotation, RoiPercent: ts.Roi, Priority: SafeTargetPriority(ts.Priority),
        DirectoryName: MosaicConvention.PanelDirectoryName(parentDir, panel.DirectoryName),
        Catalog: panel.Catalog, CommonName: panel.CommonName, ObjectName: panel.ObjectName,
        ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: Provenance(ts.TsGuid, ts.Id),
        ParentTargetId: parentId);

    // A disk panel with no TS panel target at its position: shot but unplanned, one level down.
    private static Target BuildActualPanel(TargetReport panel, string parentDir, Guid id, Guid parentId, long now) => new(
        id, TargetSource.Actual, ProjectId: null, panel.DirectoryName, Enabled: true,
        RaHours: panel.RaHours, DecDegreesSigned: panel.DecDegrees, Epoch.J2000, RotationDeg: null, RoiPercent: null,
        Priority: null, DirectoryName: MosaicConvention.PanelDirectoryName(parentDir, panel.DirectoryName),
        Catalog: panel.Catalog, CommonName: panel.CommonName, ObjectName: panel.ObjectName,
        ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: null, ParentTargetId: parentId);

    private static Target BuildPlanned(TsTarget ts, Guid id, Guid projectGuid, long now, Guid? parentId = null) => new(
        id, TargetSource.Planned, projectGuid, ts.Name, Enabled: ts.Active != 0,
        // TS is a hand-maintained external plan: normalize/clamp before its values hit CHECK/FK columns.
        RaHours: NormalizeRaHours(ts.Ra), DecDegreesSigned: ClampDec(ts.Dec), SafeEpoch(ts.EpochCode),
        RotationDeg: ts.Rotation, RoiPercent: ts.Roi,
        Priority: SafeTargetPriority(ts.Priority),
        DirectoryName: null, Catalog: null, CommonName: null, ObjectName: null,
        ScannedAt: null, CreatedAt: now, ImportedFromTsGuid: Provenance(ts.TsGuid, ts.Id),
        ParentTargetId: parentId);

    private static InventoryFilter ToInventoryFilter(Guid targetId, FilterAggregate f) => new(
        targetId, f.FilterCode, f.Purpose, f.FilterName, f.ExposureCount, f.TotalIntegration.TotalSeconds,
        new DateTimeOffset(f.FirstImagedUtc).ToUnixTimeSeconds(), new DateTimeOffset(f.LastImagedUtc).ToUnixTimeSeconds(),
        f.Typical.Gain, f.Typical.Offset, f.Typical.SetTempC, f.Typical.Binning.X, f.Typical.Binning.Y,
        f.Typical.ExposureSec, string.Join(",", f.CamerasSeen));

    // ---- Matching helpers -----------------------------------------------------------------------------------

    /// <summary>Great-circle angular separation in degrees (haversine; RA inputs are decimal hours).</summary>
    internal static double SeparationDegrees(double raHours1, double dec1, double raHours2, double dec2)
    {
        double ra1 = raHours1 * 15.0 * RadPerDeg;
        double ra2 = raHours2 * 15.0 * RadPerDeg;
        double d1 = dec1 * RadPerDeg;
        double d2 = dec2 * RadPerDeg;
        double dRa = ra2 - ra1;
        double dDec = d2 - d1;
        double h = (Math.Sin(dDec / 2.0) * Math.Sin(dDec / 2.0))
                 + (Math.Cos(d1) * Math.Cos(d2) * Math.Sin(dRa / 2.0) * Math.Sin(dRa / 2.0));
        return 2.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(h))) * DegPerRad;
    }

    /// <summary>True if the TS name reasonably corresponds to the disk identity (alphanumeric, case-insensitive).</summary>
    internal static bool NameAligned(string tsName, TargetReport disk)
    {
        string a = Normalize(tsName);
        if (a.Length == 0) return true; // nothing to disagree with

        foreach (string? candidate in new[] { disk.Catalog, disk.ObjectName, disk.DirectoryName, disk.CommonName })
        {
            string b = Normalize(candidate);
            if (b.Length == 0) continue;
            if (a == b) return true;
            if (a.Length >= 2 && b.Length >= 2 &&
                (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the TS name exactly equals one of the disk identity facets (directory / catalog / common /
    /// object name) after normalization. Strict equality — unlike <see cref="NameAligned"/>'s substring
    /// tolerance — so an alias (another name for the same object, e.g. <c>"Dumbell"</c> on
    /// <c>"M27 - Dumbell"</c>) is distinguished from a genuine sub-target variant (e.g. <c>"M42 core"</c> on
    /// <c>"M42 - Orion"</c>), which stays a duplicate.
    /// </summary>
    internal static bool IsAliasName(string tsName, TargetReport disk)
    {
        string a = Normalize(tsName);
        if (a.Length == 0) return false;

        foreach (string? candidate in new[] { disk.DirectoryName, disk.Catalog, disk.CommonName, disk.ObjectName })
        {
            string b = Normalize(candidate);
            if (b.Length > 0 && a == b) return true;
        }
        return false;
    }

    /// <summary>Reduces a name to an alphanumeric, upper-cased key for case/punctuation-insensitive matching (shared with the surgical <c>--target</c> mosaic-project name-match).</summary>
    internal static string Normalize(string? value) =>
        value is null ? string.Empty : new string([.. value.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    // ---- Stable id helpers ----------------------------------------------------------------------------------

    private static string Provenance(string? tsGuid, long tsId) =>
        tsGuid ?? tsId.ToString(CultureInfo.InvariantCulture);

    private static Guid ParseOrDerive(string? tsGuid, string fallbackKey) =>
        Guid.TryParse(tsGuid, out Guid g) ? g : DeterministicGuid(fallbackKey);

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 derives a stable GUID from a row key (UUIDv3 style); not a security mechanism.")]
    private static Guid DeterministicGuid(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    // ---- Defensive coercion of TS values into FK/CHECK-bound columns ----------------------------------------
    // The disk path validates/clamps at the scanner; the TS plan does not, so a single out-of-range external row
    // must never abort the whole rebuild. Unknown enum codes map to a safe default; coordinates are normalized.

    private static Epoch SafeEpoch(int code) => code is 0 or 1 or 2 ? (Epoch)code : Epoch.J2000;

    private static ProjectState SafeState(int state) =>
        state is 0 or 1 or 2 or 3 ? (ProjectState)state : ProjectState.Draft;

    private static ProjectPriority SafeProjectPriority(int priority) =>
        priority is 0 or 1 or 2 ? (ProjectPriority)priority : ProjectPriority.Normal;

    // Target priority: -1 = inherit and any unknown value both collapse to NULL (inherit from project).
    private static ProjectPriority? SafeTargetPriority(int priority) =>
        priority is 0 or 1 or 2 ? (ProjectPriority)priority : null;

    private static double? NormalizeRaHours(double? ra) => ra is double r ? (((r % 24.0) + 24.0) % 24.0) : null;

    private static double? ClampDec(double? dec) => dec is double d ? Math.Clamp(d, -90.0, 90.0) : null;

    // Records (without rejecting) any TS target whose raw coordinates/epoch were out of range, so the coercion is
    // visible in the build report rather than silently hidden.
    private static void FlagIfSuspect(TsTarget t, List<InvalidTsTarget> sink)
    {
        List<string> issues = [];
        if (t.Ra is double ra && (ra < 0.0 || ra >= 24.0))
            issues.Add(FormattableString.Invariant($"RA {ra:0.###}h out of [0,24)"));
        if (t.Dec is double dec && (dec < -90.0 || dec > 90.0))
            issues.Add(FormattableString.Invariant($"Dec {dec:0.###} out of [-90,90]"));
        if (t.EpochCode is < 0 or > 2)
            issues.Add(FormattableString.Invariant($"epoch code {t.EpochCode} unknown"));
        if (issues.Count > 0)
            sink.Add(new InvalidTsTarget(t.TsGuid, t.Name, string.Join("; ", issues)));
    }

    // A disk target accumulating the TS targets that resolved onto it (usually 0 or 1; >1 = a TS duplicate).
    private sealed class WorkingTarget(Guid id, TargetReport disk)
    {
        public Guid Id { get; } = id;
        public TargetReport Disk { get; } = disk;
        public List<(TsTarget Ts, double Sep)> AssignedTs { get; } = [];

        /// <summary>Set when this disk dir is a <c>"Mosaic - X"</c> target name-matched to a TS isMosaic project (its panels fold here).</summary>
        public TsProject? MosaicProject { get; set; }
    }
}
