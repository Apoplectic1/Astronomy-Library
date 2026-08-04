using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// <param name="PanelMatchToleranceDegrees">
/// The tighter radius for a mosaic panel's claim when the disk directory's name does NOT align with the TS
/// panel. Panels sit a fraction of a field apart, so coordinates alone are only trusted at plate-solve
/// precision — an unrelated framing that merely lands nearby stays unclaimed instead of arriving as a
/// flagged coordinate match. A name-aligned directory (its derived panel token validates) anchors within
/// the full <paramref name="MatchToleranceDegrees"/>: the name confirms identity, so real recenter drift
/// beyond this radius still matches.
/// </param>
public sealed record ResolveOptions(double MatchToleranceDegrees = 0.5, double PanelMatchToleranceDegrees = 0.1)
{
    /// <summary>The default options (0.5° target tolerance, 0.1° panel tolerance).</summary>
    public static ResolveOptions Default { get; } = new();
}

/// <summary>
/// Reconciles the two source planes into one canonical target list. The disk library is ACTUAL (truth); TS is the
/// PLAN. Matching is <b>coordinate-primary</b>: each TS target is anchored to the nearest disk target within
/// <see cref="ResolveOptions.MatchToleranceDegrees"/> (name only validates the match); when they merge, the disk
/// (plate-solved) coordinates win and the TS guid is retained for write-back. TS targets with no disk match become
/// planned-only (goals, 0 actual); disk targets with no TS match become actual-only. TS duplicates that collapse
/// onto one disk target, name disagreements, ambiguous matches, and un-anchorable TS targets are reported, not
/// silently dropped — a multi-claim is always a <see cref="DuplicateTsTarget"/> (one TS row per position, no
/// exceptions; the alias-fold escape was removed 2026-07-23 — adjudicated 2026-07-08 after it masked an
/// unintentional twin).
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
    /// <param name="ct">Cancellation token, observed at each resolve phase and per TS target while anchoring
    /// (the one super-linear pass). Cancellation throws; no partial graph is returned.</param>
    public static (CatalogGraph Graph, CatalogBuildReport Report) Resolve(
        IReadOnlyList<TargetReport> diskTargets,
        TsPlanData ts,
        long createdAtUnix,
        ResolveOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diskTargets);
        ArgumentNullException.ThrowIfNull(ts);
        ResolveOptions opts = options ?? ResolveOptions.Default;
        double tolerance = opts.MatchToleranceDegrees;
        double panelTolerance = opts.PanelMatchToleranceDegrees;

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
        ct.ThrowIfCancellationRequested();
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
        ct.ThrowIfCancellationRequested();
        Dictionary<long, Guid> templateIds = [];
        List<ExposureTemplate> templates = new(ts.Templates.Count);
        foreach (TsExposureTemplate t in ts.Templates)
        {
            if (!profileIds.TryGetValue(t.ProfileId, out Guid profileGuid)) continue;
            Guid id = DeterministicGuid($"template:{t.Id}");
            templateIds[t.Id] = id;
            templates.Add(new ExposureTemplate(
                id, profileGuid, t.Name, t.FilterName, Gain: t.Gain, OffsetAdu: t.Offset, Binning: t.Bin,
                ReadoutMode: t.ReadoutMode, DefaultExposureSeconds: t.DefaultExposure,
                ImportedFromTsGuid: t.Id.ToString(CultureInfo.InvariantCulture)));
        }

        // ---- Disk working set (one canonical id per directory). A mosaic contributes its parent unit plus one
        //      ordinary unit per panel (composite key "<mosaic dir>/<panel label>"), appended right after the
        //      parent so the graph keeps FK order. Panels are normal units from here on — same anchoring, same
        //      validation, same classification; only the key construction differs. Every unit carries a SCOPE
        //      key: top-level units share the default scope, a panel is in its mosaic's scope, and a mosaic
        //      parent is in no coordinate scope at all (matching by name IS its scope mechanism).
        ct.ThrowIfCancellationRequested();
        List<WorkingTarget> diskWorking = [];
        foreach (TargetReport d in diskTargets)
        {
            bool isMosaicDir = MosaicConvention.IsMosaicDirectory(d.DirectoryName);
            WorkingTarget top = new(
                DeterministicGuid($"disk:{d.DirectoryName}"), d, canonicalDir: d.DirectoryName,
                scopeKey: isMosaicDir ? null : DefaultScope);
            diskWorking.Add(top);

            if (!isMosaicDir) continue;
            string mosaicScope = Normalize(d.DirectoryName);
            foreach (TargetReport panel in d.Panels)
            {
                string childDir = MosaicConvention.PanelDirectoryName(d.DirectoryName, panel.DirectoryName);
                diskWorking.Add(new WorkingTarget(
                    DeterministicGuid($"disk:{childDir}"), panel, canonicalDir: childDir,
                    scopeKey: mosaicScope, parentId: top.Id, panelToken: PanelToken(panel.DirectoryName)));
            }
        }

        // ---- Resolve each TS target spatially onto the disk set. --------------------------------------------
        ct.ThrowIfCancellationRequested();
        Dictionary<long, Guid> tsTargetToCanonical = [];
        List<Target> plannedTargets = [];
        List<NameMismatch> nameMismatches = [];
        List<AmbiguousMatch> ambiguousMatches = [];
        List<UnanchoredTsTarget> unanchored = [];
        List<InvalidTsTarget> invalidTsTargets = [];

        // ---- Mosaic pre-pass: name-match each disk "Mosaic - X" to the same-named isMosaic project (the
        //      mosaic itself never coordinate-matches — its panels spread too far). The match drives the
        //      parent's classification and gives the project's unshot panel targets their parentage; the
        //      panels themselves anchor through the standard loop below, scoped to the mosaic's panel units.
        Dictionary<string, TsProject> mosaicProjectsByName = new(StringComparer.Ordinal);
        Dictionary<long, TsProject> mosaicProjectById = [];
        foreach (TsProject mp in ts.Projects.Where(p => p.IsMosaic != 0))
        {
            mosaicProjectsByName[Normalize(mp.Name)] = mp;
            mosaicProjectById[mp.Id] = mp;
        }

        Dictionary<long, Guid> mosaicParentByProject = [];
        int mosaicsResolved = 0;
        foreach (WorkingTarget mw in diskWorking)
        {
            if (mw.IsPanel || !MosaicConvention.IsMosaicDirectory(mw.Disk.DirectoryName)) continue;
            if (!mosaicProjectsByName.TryGetValue(Normalize(mw.Disk.DirectoryName), out TsProject? proj)) continue;

            mw.MosaicProject = proj;
            mosaicParentByProject[proj.Id] = mw.Id;
            mosaicsResolved++;
        }

        int panelsPlannedOnly = 0;

        foreach (TsTarget tst in ts.Targets)
        {
            ct.ThrowIfCancellationRequested();   // each target scans the disk set — the super-linear pass
            if (tst.ProjectId is not long projectId || !projectIds.ContainsKey(projectId))
                continue; // orphan TS target (no project) — skip

            // Units anchor within their key-space: the TS side's scope comes from its own grouping (an
            // isMosaic project = that mosaic's scope; anything else = the default scope), the disk side's
            // from its path shape. One equality — no mosaic conditional in the matching itself, and a
            // cross-scope match (e.g. a panel's goals landing on a sky-overlapping standalone dir) is
            // impossible by construction.
            bool isPanelTarget = mosaicProjectById.TryGetValue(projectId, out TsProject? mosaicProj);
            string tsScope = isPanelTarget ? Normalize(mosaicProj!.Name) : DefaultScope;
            Guid? plannedParent = isPanelTarget && mosaicParentByProject.TryGetValue(projectId, out Guid pp)
                ? pp : null;

            if (tst.Ra is not double raHours || tst.Dec is not double decDegrees)
            {
                // No coordinates — cannot anchor to disk; keep as planned-only and flag.
                AddPlanned(tst, projectId, plannedParent);
                unanchored.Add(new UnanchoredTsTarget(tst.TsGuid, tst.Name));
                continue;
            }

            // A panel's directory label ("Panel 01of16") never textually matches its TS name, but its
            // derived token does ("P1" suffixes "CygnusLoop P1") — same validation, panel-shaped facet.
            bool IsAligned(WorkingTarget w) => NameAligned(tst.Name, w.Disk)
                || (w.PanelToken is string token && TokenAligned(tst.Name, token));

            // Panels gate their radius on name alignment: an aligned directory anchors within the full
            // tolerance (the name confirms identity, so real recenter drift is absorbed), while an unaligned
            // claim must sit within the tight panel radius (coordinates alone are only trusted at plate-solve
            // precision — a merely-nearby framing under the same mosaic is a different panel, not drift).
            List<(WorkingTarget Work, double Sep)> candidates = [.. diskWorking
                .Where(w => w.ScopeKey == tsScope)
                .Select(w => (Work: w, Sep: SeparationDegrees(raHours, decDegrees, w.Disk.RaHours, w.Disk.DecDegrees)))
                .Where(x => x.Sep <= (isPanelTarget && !IsAligned(x.Work) ? panelTolerance : tolerance))
                .OrderBy(x => x.Sep)];

            if (candidates.Count == 0)
            {
                AddPlanned(tst, projectId, plannedParent);
                continue;
            }

            (WorkingTarget nearest, double nearestSep) = candidates[0];
            bool aligned = IsAligned(nearest);
            nearest.AssignedTs.Add((tst, nearestSep, aligned));
            tsTargetToCanonical[tst.Id] = nearest.Id;

            if (candidates.Count > 1)
            {
                ambiguousMatches.Add(new AmbiguousMatch(
                    tst.TsGuid, tst.Name, [.. candidates.Select(c => c.Work.CanonicalDir)], nearestSep));
            }
        }

        // An aligned claim outranks an unaligned one: when a unit accumulates several TS assignments and
        // only SOME correspond to it by name (or panel token), the others release back to planned — a
        // nearby-but-differently-named target never piles onto a directory a correctly-named target owns
        // (e.g. an unshot panel inside tolerance of its shot neighbour). With no aligned claim at all, the
        // nearest unaligned match stands and is flagged below; the rule only ever demotes, never invents.
        foreach (WorkingTarget w in diskWorking)
        {
            if (w.AssignedTs.Count < 2) continue;
            int alignedCount = w.AssignedTs.Count(a => a.Aligned);
            if (alignedCount == 0 || alignedCount == w.AssignedTs.Count) continue;

            foreach ((TsTarget released, _, _) in w.AssignedTs.Where(a => !a.Aligned).ToList())
                AddPlanned(released, released.ProjectId!.Value, PlannedParentOf(released));
            w.AssignedTs.RemoveAll(a => !a.Aligned);
        }

        // Name validation reporting happens after the outranking pass so released claims don't flag.
        foreach (WorkingTarget w in diskWorking)
        {
            foreach ((TsTarget tst, double sep, bool aligned) in w.AssignedTs)
            {
                if (!aligned)
                {
                    nameMismatches.Add(new NameMismatch(
                        tst.TsGuid, tst.Name, w.CanonicalDir, w.Disk.ObjectName, sep));
                }
            }
        }

        Guid? PlannedParentOf(TsTarget tst) =>
            tst.ProjectId is long pid && mosaicParentByProject.TryGetValue(pid, out Guid parent)
                ? parent : null;

        void AddPlanned(TsTarget tst, long projectId, Guid? parentId)
        {
            Guid id = ParseOrDerive(tst.TsGuid, $"target:{tst.Id}");
            tsTargetToCanonical[tst.Id] = id;
            plannedTargets.Add(BuildPlanned(tst, id, projectIds[projectId], createdAtUnix, parentId));
            if (parentId is not null) panelsPlannedOnly++;
            FlagIfSuspect(tst, invalidTsTargets);
        }

        // ---- Build canonical disk targets (Actual or Both) + their inventory. Panels flow the SAME branches
        //      as normal targets (their builders just carry the composite key and parent link); the mosaic
        //      parent is a grouping node — classified by its project name-match, no plans, and no inventory
        //      unless the report carried no per-panel detail (degradation keeps the aggregate on the parent).
        ct.ThrowIfCancellationRequested();
        List<Target> targets = new(diskWorking.Count + plannedTargets.Count);
        List<InventoryFilter> inventory = [];
        List<DuplicateTsTarget> duplicates = [];
        int bothCount = 0;
        int actualOnly = 0;
        int panelsMatched = 0;
        int panelsActualOnly = 0;

        foreach (WorkingTarget w in diskWorking)
        {
            if (w.ScopeKey is null)
            {
                // The name-matched grouping node (a mosaic parent): classified by its project match,
                // carrying inventory only when the report had no per-panel detail (degradation).
                if (w.MosaicProject is TsProject mosaic)
                {
                    targets.Add(BuildBothMosaic(w.Disk, mosaic, w.Id, projectIds, createdAtUnix));
                    bothCount++;
                }
                else
                {
                    targets.Add(BuildActual(w.Disk, w.Id, createdAtUnix));
                    actualOnly++;
                }

                if (w.Disk.Panels.Count == 0)
                {
                    foreach (FilterAggregate f in w.Disk.Filters)
                        inventory.Add(ToInventoryFilter(w.Id, f));
                }
                continue;
            }

            if (w.AssignedTs.Count == 0)
            {
                targets.Add(w.IsPanel
                    ? BuildActualPanel(w.Disk, w.CanonicalDir, w.Id, w.ParentId!.Value, createdAtUnix)
                    : BuildActual(w.Disk, w.Id, createdAtUnix));
                if (w.IsPanel) panelsActualOnly++; else actualOnly++;
            }
            else
            {
                TsTarget primary = w.AssignedTs.OrderBy(a => a.Sep).First().Ts;
                targets.Add(w.IsPanel
                    ? BuildBothPanel(w.Disk, w.CanonicalDir, primary, w.Id, w.ParentId!.Value, projectIds, createdAtUnix)
                    : BuildBoth(w.Disk, primary, w.Id, projectIds, createdAtUnix));
                if (w.IsPanel) panelsMatched++; else bothCount++;
                if (w.AssignedTs.Count > 1)
                {
                    duplicates.Add(new DuplicateTsTarget(
                        w.CanonicalDir, [.. w.AssignedTs.Select(a => a.Ts.Name)]));
                }
            }

            foreach (FilterAggregate f in w.Disk.Filters)
                inventory.Add(ToInventoryFilter(w.Id, f));
        }

        targets.AddRange(plannedTargets);

        // ---- Exposure plans, rewired to the canonical target id. --------------------------------------------
        ct.ThrowIfCancellationRequested();
        List<ExposurePlan> plans = new(ts.Plans.Count);
        foreach (TsExposurePlan p in ts.Plans)
        {
            if (!tsTargetToCanonical.TryGetValue(p.TargetId, out Guid targetGuid)) continue;
            if (!templateIds.TryGetValue(p.ExposureTemplateId, out Guid templateGuid)) continue;
            plans.Add(new ExposurePlan(
                DeterministicGuid($"plan:{p.Id}"), targetGuid, templateGuid,
                ExposureSeconds: p.Exposure < 0 ? null : p.Exposure,
                DesiredCount: p.Desired, AcquiredCount: p.Acquired, AcceptedCount: p.Accepted,
                Enabled: p.Enabled, ImportedFromTsGuid: p.Id.ToString(CultureInfo.InvariantCulture)));
        }

        CatalogGraph graph = new(profiles, projects, templates, targets, plans, inventory);
        CatalogBuildReport report = new(
            DiskTargetCount: diskTargets.Count, TsTargetCount: ts.Targets.Count,
            BothCount: bothCount,
            // Top-level only — planned panel children are counted via PanelsPlannedOnly instead.
            PlannedOnlyCount: plannedTargets.Count(t => t.ParentTargetId is null),
            ActualOnlyCount: actualOnly,
            NameMismatches: nameMismatches, AmbiguousMatches: ambiguousMatches,
            DuplicateTsTargets: duplicates,
            UnanchoredTsTargets: unanchored, InvalidTsTargets: invalidTsTargets,
            MosaicsResolved: mosaicsResolved, PanelsMatched: panelsMatched,
            PanelsPlannedOnly: panelsPlannedOnly, PanelsActualOnly: panelsActualOnly);
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
        TargetReport panel, string canonicalDir, TsTarget ts, Guid id, Guid parentId,
        Dictionary<long, Guid> projectIds, long now) => new(
        id, TargetSource.Both, projectIds[ts.ProjectId!.Value], ts.Name, Enabled: ts.Active != 0,
        RaHours: panel.RaHours, DecDegreesSigned: panel.DecDegrees, Epoch.J2000,
        RotationDeg: ts.Rotation, RoiPercent: ts.Roi, Priority: SafeTargetPriority(ts.Priority),
        DirectoryName: canonicalDir,
        Catalog: panel.Catalog, CommonName: panel.CommonName, ObjectName: panel.ObjectName,
        ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: Provenance(ts.TsGuid, ts.Id),
        ParentTargetId: parentId);

    // A disk panel with no TS panel target: shot but unplanned, one level down.
    private static Target BuildActualPanel(TargetReport panel, string canonicalDir, Guid id, Guid parentId, long now) => new(
        id, TargetSource.Actual, ProjectId: null, panel.DirectoryName, Enabled: true,
        RaHours: panel.RaHours, DecDegreesSigned: panel.DecDegrees, Epoch.J2000, RotationDeg: null, RoiPercent: null,
        Priority: null, DirectoryName: canonicalDir,
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
        f.Typical.ExposureSec, f.Camera, f.Framing.Ordinal, f.Framing.Expression, f.Framing.FoldAngleDegrees,
        f.CameraDisagrees,
        f.Framing.CentroidRaHours, f.Framing.CentroidDecDegrees,
        f.Framing.FieldWidthDeg, f.Framing.FieldHeightDeg, f.Framing.SpansMultipleSensors);

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

    /// <summary>Reduces a name to an alphanumeric, upper-cased key for case/punctuation-insensitive matching (shared with the surgical <c>--target</c> mosaic-project name-match).</summary>
    internal static string Normalize(string? value) =>
        value is null ? string.Empty : new string([.. value.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    /// <summary>
    /// The compact identity token of a panel directory label, mirroring the established filing convention:
    /// <c>"Panel 01of16"</c> → <c>"P1"</c> (the number, zeros stripped); <c>"Panel Foo"</c> → <c>"Foo"</c>;
    /// anything else passes through unchanged.
    /// </summary>
    internal static string PanelToken(string panelLabel)
    {
        Match numbered = Regex.Match(panelLabel, @"^Panel\s*0*(\d+)of\d+$", RegexOptions.IgnoreCase);
        if (numbered.Success) return "P" + numbered.Groups[1].Value;
        Match named = Regex.Match(panelLabel, @"^Panel\s+(.+)$", RegexOptions.IgnoreCase);
        return named.Success ? named.Groups[1].Value : panelLabel;
    }

    /// <summary>True when the TS name ends with the panel's identity token (normalized) — the panel-shaped
    /// counterpart of <see cref="NameAligned"/>: <c>"CygnusLoop P1"</c> aligns with token <c>"P1"</c>.</summary>
    internal static bool TokenAligned(string tsName, string token)
    {
        string a = Normalize(tsName);
        string b = Normalize(token);
        return b.Length > 0 && a.EndsWith(b, StringComparison.Ordinal);
    }

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

    // TS persists NINA's Epoch enum ints (JNOW=0, B1950=1, J2000=2, J2050=3) — the REVERSE of this
    // library's epoch lookup for codes 0/1 — so translate explicitly: a raw cast would silently swap
    // JNow and B1950. J2050 has no lookup row here; like any unrecognized code it coerces to J2000
    // (FlagIfSuspect reports it) per the coerce-don't-abort rule above.
    private static Epoch SafeEpoch(int code) => code switch {
        0 => Epoch.JNow,
        1 => Epoch.B1950,
        2 => Epoch.J2000,
        _ => Epoch.J2000,
    };

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

    /// <summary>The coordinate scope shared by all top-level units (and TS targets outside isMosaic projects).</summary>
    private const string DefaultScope = "";

    // A disk unit accumulating the TS targets that resolved onto it (usually 0 or 1; >1 = a TS duplicate).
    // A unit is a top-level directory OR one panel of a mosaic — panels are ordinary units whose key is
    // composite and which carry their parent link plus the scoping/validation facets.
    private sealed class WorkingTarget(
        Guid id, TargetReport disk, string canonicalDir, string? scopeKey,
        Guid? parentId = null, string? panelToken = null)
    {
        public Guid Id { get; } = id;
        public TargetReport Disk { get; } = disk;

        /// <summary>The catalog directory name: the dir itself, or <c>"&lt;mosaic dir&gt;/&lt;panel label&gt;"</c> for a panel.</summary>
        public string CanonicalDir { get; } = canonicalDir;

        /// <summary>
        /// The unit's coordinate key-space: <see cref="DefaultScope"/> for a top-level unit, the mosaic's
        /// normalized name for a panel, and null for a mosaic parent (which matches by name, never by
        /// coordinates). Anchoring only ever happens scope-equal.
        /// </summary>
        public string? ScopeKey { get; } = scopeKey;

        /// <summary>The mosaic parent's canonical id when this unit is a panel.</summary>
        public Guid? ParentId { get; } = parentId;

        /// <summary>The panel's identity token (e.g. <c>"P1"</c>) for name validation.</summary>
        public string? PanelToken { get; } = panelToken;

        public bool IsPanel => ParentId is not null;

        public List<(TsTarget Ts, double Sep, bool Aligned)> AssignedTs { get; } = [];

        /// <summary>Set when this dir is a <c>"Mosaic - X"</c> parent name-matched to a TS isMosaic project.</summary>
        public TsProject? MosaicProject { get; set; }
    }
}
