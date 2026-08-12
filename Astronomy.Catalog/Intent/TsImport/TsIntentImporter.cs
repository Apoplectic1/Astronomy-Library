using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent.TsImport;

/// <summary>Entity counts written by a completed lift, for verification against direct source queries.</summary>
public sealed record TsImportReport(
    int Profiles, int Projects, int Targets, int MosaicParents, int ExposureTemplates, int ExposurePlans);

/// <summary>
/// The one-time Target Scheduler lift: reads a <c>schedulerdb.sqlite</c> read-only and imports the
/// in-scope authored intent (profiles, projects, targets, exposure templates, exposure plans —
/// per the adopted scope table) into an EMPTY intent store, all-or-nothing in one transaction.
/// Every lifted row carries <c>imported_from_ts_guid</c> provenance; enum codes cross through the
/// pinned translation maps; TS sentinels become NULL; any contract violation aborts via
/// <see cref="TsImportException"/> with the store rolled back untouched. Acquisition history and
/// TS scoring machinery are deliberately not lifted — progress truth is the image library on disk.
/// </summary>
public static class TsIntentImporter
{
    /// <summary>
    /// Runs the lift from <paramref name="schedulerDbPath"/> into <paramref name="store"/>.
    /// <paramref name="importedAt"/> stamps <c>created_at</c> on rows whose source carries no
    /// creation time (profiles, targets) — passed in so runs are deterministic and testable.
    /// </summary>
    /// <exception cref="TsImportException">
    /// The store is not empty, a required source field is missing/blank/unparseable, an enum code
    /// has no map entry, or a source reference dangles. The store is unchanged.
    /// </exception>
    public static TsImportReport Import(string schedulerDbPath, IntentStore store, DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(store);

        using TsImportReader source = new(schedulerDbPath);
        IReadOnlyList<TsSourceProject> projects = source.ReadProjects();
        IReadOnlyList<TsSourceTarget> targets = source.ReadTargets();
        IReadOnlyList<TsSourceTemplate> templates = source.ReadExposureTemplates();
        IReadOnlyList<TsSourceExposurePlan> plans = source.ReadExposurePlans();

        long stampUnix = importedAt.ToUnixTimeSeconds();
        SqliteConnection db = store.Connection;
        using SqliteTransaction tx = db.BeginTransaction();

        RequireEmptyStore(db, tx);

        // ---- Profiles: TS has no profile table — the distinct profileId strings ARE the profiles.
        //      The store row keeps the NINA GUID as both correlation and initial name (nothing is
        //      invented; the name is authored intent the user can edit later).
        Dictionary<string, Guid> profileIds = [];
        foreach (string profileGuid in projects.Select(p => RequireText(p.ProfileId, "project", p.Id, "profileId"))
                     .Concat(templates.Select(t => RequireText(t.ProfileId, "exposuretemplate", t.Id, "profileId")))
                     .Distinct(StringComparer.Ordinal))
        {
            Guid id = Guid.NewGuid();
            profileIds.Add(profileGuid, id);
            Insert(db, tx,
                "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, $name, $nina, $created);",
                ("$id", GuidBlob.ToBlob(id)), ("$name", profileGuid), ("$nina", profileGuid), ("$created", stampUnix));
        }

        // ---- Projects.
        Dictionary<long, Guid> projectIds = [];
        Dictionary<long, TsSourceProject> projectsBySourceId = [];
        foreach (TsSourceProject p in projects)
        {
            Guid id = Guid.NewGuid();
            projectIds.Add(p.Id, id);
            projectsBySourceId.Add(p.Id, p);
            Insert(db, tx,
                """
                INSERT INTO project (id, profile_id, name, description, state_id, priority_id, minimum_time_minutes,
                    minimum_altitude_deg, maximum_altitude_deg, use_custom_horizon, horizon_offset_deg,
                    meridian_window_minutes, filter_switch_frequency, dither_every, smart_exposure_order, is_mosaic,
                    created_at, active_at, inactive_at, imported_from_ts_guid)
                VALUES ($id, $profile, $name, $description, $state, $priority, $mintime, $minalt, $maxalt, $uch,
                    $hoffset, $meridian, $fsf, $dither, $seo, $mosaic, $created, $active, $inactive, $ts_guid);
                """,
                ("$id", GuidBlob.ToBlob(id)),
                ("$profile", GuidBlob.ToBlob(profileIds[RequireText(p.ProfileId, "project", p.Id, "profileId")])),
                ("$name", RequireText(p.Name, "project", p.Id, "name")),
                ("$description", Nullable(p.Description)),
                ("$state", TsImportMaps.Map(TsImportMaps.ProjectState, Require(p.State, "project", p.Id, "state"), "project", "state", $"Id={p.Id}")),
                ("$priority", TsImportMaps.Map(TsImportMaps.Priority, Require(p.Priority, "project", p.Id, "priority"), "project", "priority", $"Id={p.Id}")),
                ("$mintime", Require(p.MinimumTime, "project", p.Id, "minimumtime")),
                // 0.0 is TS's no-constraint sentinel on both altitude bounds -> NULL (unset).
                ("$minalt", Nullable(p.MinimumAltitude == 0.0 ? null : p.MinimumAltitude)),
                ("$maxalt", Nullable(p.MaximumAltitude == 0.0 ? null : p.MaximumAltitude)),
                ("$uch", RequireBool(p.UseCustomHorizon, "project", p.Id, "usecustomhorizon")),
                ("$hoffset", Require(p.HorizonOffset, "project", p.Id, "horizonoffset")),
                ("$meridian", Nullable(p.MeridianWindow)),
                ("$fsf", Nullable(p.FilterSwitchFrequency)),
                ("$dither", Nullable(p.DitherEvery)),
                ("$seo", RequireBool(p.SmartExposureOrder, "project", p.Id, "smartexposureorder")),
                ("$mosaic", RequireBool(p.IsMosaic, "project", p.Id, "isMosaic")),
                ("$created", Require(p.CreateDate, "project", p.Id, "createdate")),
                ("$active", Nullable(p.ActiveDate)),
                ("$inactive", Nullable(p.InactiveDate)),
                ("$ts_guid", RequireText(p.Guid, "project", p.Id, "guid")));
        }

        // ---- Mosaic parents: TS keeps panels flat under an isMosaic project; the store's shipped
        //      invariant is one PARENT grouping row plus one child per panel. The parent is
        //      reconstruction, not a lifted row — no coordinates, no provenance GUID of its own
        //      (lineage runs through project_id); synthesized only where panels exist.
        Dictionary<long, Guid> mosaicParentByProject = [];
        int mosaicParents = 0;
        foreach (TsSourceTarget t in targets)
        {
            long projectSourceId = Require(t.ProjectId, "target", t.Id, "projectid");
            if (!projectsBySourceId.TryGetValue(projectSourceId, out TsSourceProject? proj))
                throw new TsImportException($"TS import: target Id={t.Id} references project Id={projectSourceId}, which does not exist in the source.");
            if (proj.IsMosaic is not 1 || mosaicParentByProject.ContainsKey(projectSourceId)) continue;

            Guid parentId = Guid.NewGuid();
            mosaicParentByProject.Add(projectSourceId, parentId);
            mosaicParents++;
            Insert(db, tx,
                """
                INSERT INTO target (id, project_id, parent_target_id, name, enabled, ra_hours, dec_degrees_signed,
                    epoch_id, rotation_deg, priority_id, created_at, imported_from_ts_guid)
                VALUES ($id, $project, NULL, $name, 1, NULL, NULL, 2, NULL, NULL, $created, NULL);
                """,
                ("$id", GuidBlob.ToBlob(parentId)),
                ("$project", GuidBlob.ToBlob(projectIds[projectSourceId])),
                ("$name", RequireText(proj.Name, "project", proj.Id, "name")),
                ("$created", stampUnix));
        }

        // ---- Targets (mosaic panels get their parent link; every leaf carries authored coordinates).
        Dictionary<long, Guid> targetIds = [];
        foreach (TsSourceTarget t in targets)
        {
            Guid id = Guid.NewGuid();
            targetIds.Add(t.Id, id);
            long projectSourceId = t.ProjectId!.Value; // validated in the mosaic pass

            double ra = Require(t.Ra, "target", t.Id, "ra");
            double dec = Require(t.Dec, "target", t.Id, "dec");
            if (ra is < 0.0 or >= 24.0)
                throw new TsImportException($"TS import: target Id={t.Id} ra={ra} is outside [0, 24) hours; expected decimal hours.");
            if (dec is < -90.0 or > 90.0)
                throw new TsImportException($"TS import: target Id={t.Id} dec={dec} is outside [-90, +90] degrees.");

            // TS TargetPriority Default=-1 is the inherit-project sentinel -> NULL; real values map.
            long rawPriority = Require(t.Priority, "target", t.Id, "priority");
            long? priority = rawPriority == -1
                ? null
                : TsImportMaps.Map(TsImportMaps.Priority, rawPriority, "target", "priority", $"Id={t.Id}");

            Insert(db, tx,
                """
                INSERT INTO target (id, project_id, parent_target_id, name, enabled, ra_hours, dec_degrees_signed,
                    epoch_id, rotation_deg, priority_id, created_at, imported_from_ts_guid)
                VALUES ($id, $project, $parent, $name, $enabled, $ra, $dec, $epoch, $rotation, $priority, $created, $ts_guid);
                """,
                ("$id", GuidBlob.ToBlob(id)),
                ("$project", GuidBlob.ToBlob(projectIds[projectSourceId])),
                ("$parent", mosaicParentByProject.TryGetValue(projectSourceId, out Guid parent) ? (object)GuidBlob.ToBlob(parent) : DBNull.Value),
                ("$name", RequireText(t.Name, "target", t.Id, "name")),
                ("$enabled", RequireBool(t.Active, "target", t.Id, "active")),
                ("$ra", ra),
                ("$dec", dec),
                ("$epoch", TsImportMaps.Map(TsImportMaps.Epoch, Require(t.EpochCode, "target", t.Id, "epochcode"), "target", "epochcode", $"Id={t.Id}")),
                ("$rotation", Nullable(t.Rotation)),
                ("$priority", Nullable(priority)),
                ("$created", stampUnix),
                ("$ts_guid", RequireText(t.Guid, "target", t.Id, "guid")));
        }

        // ---- Exposure templates.
        Dictionary<long, Guid> templateIds = [];
        foreach (TsSourceTemplate t in templates)
        {
            Guid id = Guid.NewGuid();
            templateIds.Add(t.Id, id);
            Insert(db, tx,
                """
                INSERT INTO exposure_template (id, profile_id, name, filter_name, gain, offset_adu, binning,
                    readout_mode, default_exposure_seconds, twilight_level_id, moon_avoidance_enabled,
                    moon_avoidance_separation_deg, moon_avoidance_width_days, moon_relax_scale,
                    moon_relax_max_altitude_deg, moon_relax_min_altitude_deg, imported_from_ts_guid)
                VALUES ($id, $profile, $name, $filter, $gain, $offset, $bin, $readout, $exposure, $twilight,
                    $mae, $separation, $width, $relax_scale, $relax_max, $relax_min, $ts_guid);
                """,
                ("$id", GuidBlob.ToBlob(id)),
                ("$profile", GuidBlob.ToBlob(profileIds[RequireText(t.ProfileId, "exposuretemplate", t.Id, "profileId")])),
                ("$name", RequireText(t.Name, "exposuretemplate", t.Id, "name")),
                ("$filter", RequireText(t.FilterName, "exposuretemplate", t.Id, "filtername")),
                ("$gain", Nullable(t.Gain)),
                ("$offset", Nullable(t.Offset)),
                ("$bin", Require(t.Bin, "exposuretemplate", t.Id, "bin")),
                // -1 is TS's use-camera-default sentinel -> NULL.
                ("$readout", Nullable(t.ReadoutMode == -1 ? null : t.ReadoutMode)),
                ("$exposure", Require(t.DefaultExposure, "exposuretemplate", t.Id, "defaultexposure")),
                ("$twilight", TsImportMaps.Map(TsImportMaps.TwilightLevel, Require(t.TwilightLevel, "exposuretemplate", t.Id, "twilightlevel"), "exposuretemplate", "twilightlevel", $"Id={t.Id}")),
                ("$mae", RequireBool(t.MoonAvoidanceEnabled, "exposuretemplate", t.Id, "moonavoidanceenabled")),
                ("$separation", Nullable(t.MoonAvoidanceSeparation)),
                ("$width", Nullable(t.MoonAvoidanceWidth)),
                ("$relax_scale", Nullable(t.MoonRelaxScale)),
                ("$relax_max", Nullable(t.MoonRelaxMaxAltitude)),
                ("$relax_min", Nullable(t.MoonRelaxMinAltitude)),
                ("$ts_guid", RequireText(t.Guid, "exposuretemplate", t.Id, "guid")));
        }

        // ---- Exposure plans (desired counts only — TS's acquired/accepted are projections of
        //      actuals and are deliberately not lifted).
        foreach (TsSourceExposurePlan p in plans)
        {
            long targetSourceId = Require(p.TargetId, "exposureplan", p.Id, "targetid");
            if (!targetIds.TryGetValue(targetSourceId, out Guid targetId))
                throw new TsImportException($"TS import: exposureplan Id={p.Id} references target Id={targetSourceId}, which does not exist in the source.");
            long templateSourceId = Require(p.ExposureTemplateId, "exposureplan", p.Id, "exposureTemplateId");
            if (!templateIds.TryGetValue(templateSourceId, out Guid templateId))
                throw new TsImportException($"TS import: exposureplan Id={p.Id} references exposuretemplate Id={templateSourceId}, which does not exist in the source.");

            double exposure = Require(p.Exposure, "exposureplan", p.Id, "exposure");
            Insert(db, tx,
                """
                INSERT INTO exposure_plan (id, target_id, exposure_template_id, exposure_seconds, desired_count,
                    enabled, imported_from_ts_guid)
                VALUES ($id, $target, $template, $exposure, $desired, $enabled, $ts_guid);
                """,
                ("$id", GuidBlob.ToBlob(Guid.NewGuid())),
                ("$target", GuidBlob.ToBlob(targetId)),
                ("$template", GuidBlob.ToBlob(templateId)),
                // -1.0 is TS's inherit-template-default sentinel -> NULL.
                ("$exposure", Nullable(exposure == -1.0 ? null : (double?)exposure)),
                ("$desired", Require(p.Desired, "exposureplan", p.Id, "desired")),
                ("$enabled", RequireBool(p.Enabled, "exposureplan", p.Id, "enabled")),
                ("$ts_guid", RequireText(p.Guid, "exposureplan", p.Id, "guid")));
        }

        tx.Commit();
        return new TsImportReport(
            profileIds.Count, projects.Count, targets.Count + mosaicParents, mosaicParents,
            templates.Count, plans.Count);
    }

    /// <summary>The lift runs only into a store with no authored intent — refuse anything else.</summary>
    private static void RequireEmptyStore(SqliteConnection db, SqliteTransaction tx)
    {
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "SELECT (SELECT count(*) FROM profile) + (SELECT count(*) FROM project) + (SELECT count(*) FROM target) + " +
            "(SELECT count(*) FROM exposure_template) + (SELECT count(*) FROM exposure_plan) + (SELECT count(*) FROM plan);";
        long rows = (long)command.ExecuteScalar()!;
        if (rows != 0)
            throw new TsImportException(
                $"TS import: the destination store already contains {rows} intent row(s). The one-time lift runs " +
                "only into an empty store — there is no merge mode. The store is unchanged.");
    }

    private static long Require(long? value, string table, long id, string column) =>
        value ?? throw MissingField(table, id, column);

    private static double Require(double? value, string table, long id, string column) =>
        value ?? throw MissingField(table, id, column);

    private static string RequireText(string? value, string table, long id, string column) =>
        string.IsNullOrWhiteSpace(value) ? throw MissingField(table, id, column) : value;

    private static long RequireBool(long? value, string table, long id, string column) =>
        Require(value, table, id, column) is 0 or 1
            ? value!.Value
            : throw new TsImportException($"TS import: {table} Id={id} column '{column}' holds {value}; expected a 0/1 boolean.");

    private static TsImportException MissingField(string table, long id, string column) =>
        new($"TS import: {table} Id={id} column '{column}' is missing or blank; a required source field aborts the import.");

    private static object Nullable<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static object Nullable(string? value) => value is null ? DBNull.Value : value;

    private static void Insert(SqliteConnection db, SqliteTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
