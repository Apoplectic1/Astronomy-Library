using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// One-shot import of N.I.N.A. Target Scheduler data into the catalog plan plane (Option B — import with
/// provenance). Catalog ids are stable: TS's own GUID is reused for profile/project/target; templates and plans
/// (which have no TS GUID) get a deterministic GUID derived from their TS row key, so a re-import maps to the
/// same ids. Sentinels become NULL (target priority -1 → inherit; exposure -1 → inherit template). Orphan rows
/// (a target with no project, a plan with no target/template) are skipped. Re-importing fully replaces the plan.
/// </summary>
public static class TsCatalogImporter
{
    /// <summary>Reads <paramref name="ts"/> and replaces the catalog plan plane via <see cref="CatalogStore.ImportPlan"/>.</summary>
    public static void Import(CatalogStore store, TargetSchedulerReader ts)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ts);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        IReadOnlyList<TsProject> tsProjects = ts.ReadProjects();
        IReadOnlyList<TsTarget> tsTargets = ts.ReadTargets();
        IReadOnlyList<TsExposureTemplate> tsTemplates = ts.ReadExposureTemplates();
        IReadOnlyList<TsExposurePlan> tsPlans = ts.ReadExposurePlans();

        // One profile per distinct TS profileId (a NINA profile GUID string).
        Dictionary<string, Guid> profileIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string profileId in tsProjects.Select(p => p.ProfileId)
                     .Concat(tsTemplates.Select(t => t.ProfileId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            profileIds[profileId] = ParseOrDerive(profileId, $"profile:{profileId}");
        }

        List<Profile> profiles = [.. profileIds.Select(kv => new Profile(kv.Value, kv.Key, kv.Key, now))];

        Dictionary<long, Guid> projectIds = [];
        List<Project> projects = new(tsProjects.Count);
        foreach (TsProject p in tsProjects)
        {
            Guid id = ParseOrDerive(p.TsGuid, $"project:{p.Id}");
            projectIds[p.Id] = id;
            projects.Add(new Project(
                id, profileIds[p.ProfileId], p.Name, Description: null,
                State: (ProjectState)p.State, Priority: (ProjectPriority)p.Priority,
                MinimumAltitudeDeg: p.MinimumAltitude, MaximumAltitudeDeg: null, MinimumTimeMinutes: null,
                UseCustomHorizon: false, HorizonOffsetDeg: null, MeridianWindowMinutes: null,
                IsMosaic: p.IsMosaic != 0, EnableGrader: true,
                CreatedAt: now, ActiveAt: null, InactiveAt: null, ImportedFromTsGuid: Provenance(p.TsGuid, p.Id)));
        }

        Dictionary<long, Guid> targetIds = [];
        List<Target> targets = new(tsTargets.Count);
        foreach (TsTarget t in tsTargets)
        {
            if (t.ProjectId is not long projectId || !projectIds.TryGetValue(projectId, out Guid projectGuid))
                continue; // orphan target — skip

            Guid id = ParseOrDerive(t.TsGuid, $"target:{t.Id}");
            targetIds[t.Id] = id;
            targets.Add(new Target(
                id, projectGuid, t.Name, Enabled: t.Active != 0,
                RaHours: t.Ra, DecDegreesSigned: t.Dec, Epoch: (Epoch)t.EpochCode,
                RotationDeg: t.Rotation, RoiPercent: t.Roi,
                Priority: t.Priority < 0 ? null : (ProjectPriority)t.Priority,
                CreatedAt: now, ImportedFromTsGuid: Provenance(t.TsGuid, t.Id)));
        }

        Dictionary<long, Guid> templateIds = [];
        List<ExposureTemplate> templates = new(tsTemplates.Count);
        foreach (TsExposureTemplate t in tsTemplates)
        {
            if (!profileIds.TryGetValue(t.ProfileId, out Guid profileGuid))
                continue;

            Guid id = DeterministicGuid($"template:{t.Id}");
            templateIds[t.Id] = id;
            templates.Add(new ExposureTemplate(
                id, profileGuid, t.Name, t.FilterName, Gain: t.Gain, OffsetAdu: t.Offset, Binning: t.Bin,
                ReadoutMode: null, DefaultExposureSeconds: t.DefaultExposure,
                ImportedFromTsGuid: t.Id.ToString(CultureInfo.InvariantCulture)));
        }

        List<ExposurePlan> plans = new(tsPlans.Count);
        foreach (TsExposurePlan p in tsPlans)
        {
            if (!targetIds.TryGetValue(p.TargetId, out Guid targetGuid)) continue;
            if (!templateIds.TryGetValue(p.ExposureTemplateId, out Guid templateGuid)) continue;

            plans.Add(new ExposurePlan(
                DeterministicGuid($"plan:{p.Id}"), targetGuid, templateGuid,
                ExposureSeconds: p.Exposure < 0 ? null : p.Exposure,
                DesiredCount: p.Desired, AcquiredCount: p.Acquired, AcceptedCount: p.Accepted,
                Enabled: true, ImportedFromTsGuid: p.Id.ToString(CultureInfo.InvariantCulture)));
        }

        store.ImportPlan(profiles, projects, targets, templates, plans);
    }

    private static string Provenance(string? tsGuid, long tsId) =>
        tsGuid ?? tsId.ToString(CultureInfo.InvariantCulture);

    private static Guid ParseOrDerive(string? tsGuid, string fallbackKey) =>
        Guid.TryParse(tsGuid, out Guid g) ? g : DeterministicGuid(fallbackKey);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 here derives a stable GUID from a TS row key (UUIDv3 style); not a security mechanism.")]
    private static Guid DeterministicGuid(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));
}
