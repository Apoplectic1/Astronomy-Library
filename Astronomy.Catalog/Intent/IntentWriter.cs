using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent;

/// <summary>
/// The intent store's write/lookup surface for the four intent-plane entity kinds (project, target,
/// exposure template, exposure plan) — so no caller ever hand-writes SQL against the schema. Upserts
/// are keyed by the caller-supplied row id and are <b>full-value</b>: every record field overwrites
/// the stored value, including NULL (unset — the surface never substitutes a default or sentinel for
/// a caller-supplied value); <c>created_at</c> is written on create only and is never rewritten by an
/// update. Provenance lookups resolve a row id from the optional <c>imported_from_ts_guid</c> key —
/// no match resolves to <see langword="null"/>, a duplicate fails loudly. Every operation composes
/// with a caller-owned transaction (pass the caller's <see cref="SqliteTransaction"/> to group any
/// number of operations into one atomic unit); the writer never begins, commits, or rolls back a
/// transaction of its own.
/// </summary>
public sealed class IntentWriter
{
    private readonly IntentStore _store;

    /// <summary>Creates a writer over <paramref name="store"/> (which stays owned by the caller).</summary>
    public IntentWriter(IntentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Creates or fully updates a <c>project</c> row keyed by <see cref="ProjectIntent.Id"/>.</summary>
    public void UpsertProject(ProjectIntent row, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        Execute(transaction,
            """
            INSERT INTO project (id, profile_id, name, description, state_id, priority_id, minimum_time_minutes,
                minimum_altitude_deg, maximum_altitude_deg, use_custom_horizon, horizon_offset_deg,
                meridian_window_minutes, filter_switch_frequency, dither_every, smart_exposure_order, is_mosaic,
                created_at, active_at, inactive_at, imported_from_ts_guid)
            VALUES ($id, $profile, $name, $description, $state, $priority, $mintime, $minalt, $maxalt, $uch,
                $hoffset, $meridian, $fsf, $dither, $seo, $mosaic, $created, $active, $inactive, $ts_guid)
            ON CONFLICT(id) DO UPDATE SET
                profile_id = excluded.profile_id, name = excluded.name, description = excluded.description,
                state_id = excluded.state_id, priority_id = excluded.priority_id,
                minimum_time_minutes = excluded.minimum_time_minutes,
                minimum_altitude_deg = excluded.minimum_altitude_deg,
                maximum_altitude_deg = excluded.maximum_altitude_deg,
                use_custom_horizon = excluded.use_custom_horizon, horizon_offset_deg = excluded.horizon_offset_deg,
                meridian_window_minutes = excluded.meridian_window_minutes,
                filter_switch_frequency = excluded.filter_switch_frequency, dither_every = excluded.dither_every,
                smart_exposure_order = excluded.smart_exposure_order, is_mosaic = excluded.is_mosaic,
                active_at = excluded.active_at, inactive_at = excluded.inactive_at,
                imported_from_ts_guid = excluded.imported_from_ts_guid;
            """,
            ("$id", GuidBlob.ToBlob(row.Id)),
            ("$profile", GuidBlob.ToBlob(row.ProfileId)),
            ("$name", row.Name),
            ("$description", Nullable(row.Description)),
            ("$state", row.StateId),
            ("$priority", row.PriorityId),
            ("$mintime", Nullable(row.MinimumTimeMinutes)),
            ("$minalt", Nullable(row.MinimumAltitudeDeg)),
            ("$maxalt", Nullable(row.MaximumAltitudeDeg)),
            ("$uch", row.UseCustomHorizon ? 1 : 0),
            ("$hoffset", row.HorizonOffsetDeg),
            ("$meridian", Nullable(row.MeridianWindowMinutes)),
            ("$fsf", Nullable(row.FilterSwitchFrequency)),
            ("$dither", Nullable(row.DitherEvery)),
            ("$seo", row.SmartExposureOrder ? 1 : 0),
            ("$mosaic", row.IsMosaic ? 1 : 0),
            ("$created", row.CreatedAt),
            ("$active", Nullable(row.ActiveAt)),
            ("$inactive", Nullable(row.InactiveAt)),
            ("$ts_guid", Nullable(row.ImportedFromTsGuid)));
    }

    /// <summary>Creates or fully updates a <c>target</c> row keyed by <see cref="TargetIntent.Id"/>.</summary>
    public void UpsertTarget(TargetIntent row, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        Execute(transaction,
            """
            INSERT INTO target (id, project_id, parent_target_id, name, enabled, ra_hours, dec_degrees_signed,
                epoch_id, rotation_deg, priority_id, created_at, imported_from_ts_guid)
            VALUES ($id, $project, $parent, $name, $enabled, $ra, $dec, $epoch, $rotation, $priority, $created, $ts_guid)
            ON CONFLICT(id) DO UPDATE SET
                project_id = excluded.project_id, parent_target_id = excluded.parent_target_id,
                name = excluded.name, enabled = excluded.enabled, ra_hours = excluded.ra_hours,
                dec_degrees_signed = excluded.dec_degrees_signed, epoch_id = excluded.epoch_id,
                rotation_deg = excluded.rotation_deg, priority_id = excluded.priority_id,
                imported_from_ts_guid = excluded.imported_from_ts_guid;
            """,
            ("$id", GuidBlob.ToBlob(row.Id)),
            ("$project", GuidBlob.ToBlob(row.ProjectId)),
            ("$parent", row.ParentTargetId is Guid parent ? GuidBlob.ToBlob(parent) : DBNull.Value),
            ("$name", row.Name),
            ("$enabled", row.Enabled ? 1 : 0),
            ("$ra", Nullable(row.RaHours)),
            ("$dec", Nullable(row.DecDegreesSigned)),
            ("$epoch", row.EpochId),
            ("$rotation", Nullable(row.RotationDeg)),
            ("$priority", Nullable(row.PriorityId)),
            ("$created", row.CreatedAt),
            ("$ts_guid", Nullable(row.ImportedFromTsGuid)));
    }

    /// <summary>Creates or fully updates an <c>exposure_template</c> row keyed by <see cref="ExposureTemplateIntent.Id"/>.</summary>
    public void UpsertExposureTemplate(ExposureTemplateIntent row, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        Execute(transaction,
            """
            INSERT INTO exposure_template (id, profile_id, name, filter_name, gain, offset_adu, binning,
                readout_mode, default_exposure_seconds, twilight_level_id, moon_avoidance_enabled,
                moon_avoidance_separation_deg, moon_avoidance_width_days, moon_relax_scale,
                moon_relax_max_altitude_deg, moon_relax_min_altitude_deg, imported_from_ts_guid)
            VALUES ($id, $profile, $name, $filter, $gain, $offset, $bin, $readout, $exposure, $twilight,
                $mae, $separation, $width, $relax_scale, $relax_max, $relax_min, $ts_guid)
            ON CONFLICT(id) DO UPDATE SET
                profile_id = excluded.profile_id, name = excluded.name, filter_name = excluded.filter_name,
                gain = excluded.gain, offset_adu = excluded.offset_adu, binning = excluded.binning,
                readout_mode = excluded.readout_mode, default_exposure_seconds = excluded.default_exposure_seconds,
                twilight_level_id = excluded.twilight_level_id,
                moon_avoidance_enabled = excluded.moon_avoidance_enabled,
                moon_avoidance_separation_deg = excluded.moon_avoidance_separation_deg,
                moon_avoidance_width_days = excluded.moon_avoidance_width_days,
                moon_relax_scale = excluded.moon_relax_scale,
                moon_relax_max_altitude_deg = excluded.moon_relax_max_altitude_deg,
                moon_relax_min_altitude_deg = excluded.moon_relax_min_altitude_deg,
                imported_from_ts_guid = excluded.imported_from_ts_guid;
            """,
            ("$id", GuidBlob.ToBlob(row.Id)),
            ("$profile", GuidBlob.ToBlob(row.ProfileId)),
            ("$name", row.Name),
            ("$filter", row.FilterName),
            ("$gain", Nullable(row.Gain)),
            ("$offset", Nullable(row.OffsetAdu)),
            ("$bin", row.Binning),
            ("$readout", Nullable(row.ReadoutMode)),
            ("$exposure", row.DefaultExposureSeconds),
            ("$twilight", row.TwilightLevelId),
            ("$mae", row.MoonAvoidanceEnabled ? 1 : 0),
            ("$separation", Nullable(row.MoonAvoidanceSeparationDeg)),
            ("$width", Nullable(row.MoonAvoidanceWidthDays)),
            ("$relax_scale", Nullable(row.MoonRelaxScale)),
            ("$relax_max", Nullable(row.MoonRelaxMaxAltitudeDeg)),
            ("$relax_min", Nullable(row.MoonRelaxMinAltitudeDeg)),
            ("$ts_guid", Nullable(row.ImportedFromTsGuid)));
    }

    /// <summary>Creates or fully updates an <c>exposure_plan</c> row keyed by <see cref="ExposurePlanIntent.Id"/>.</summary>
    public void UpsertExposurePlan(ExposurePlanIntent row, SqliteTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        Execute(transaction,
            """
            INSERT INTO exposure_plan (id, target_id, exposure_template_id, exposure_seconds, desired_count,
                enabled, imported_from_ts_guid)
            VALUES ($id, $target, $template, $exposure, $desired, $enabled, $ts_guid)
            ON CONFLICT(id) DO UPDATE SET
                target_id = excluded.target_id, exposure_template_id = excluded.exposure_template_id,
                exposure_seconds = excluded.exposure_seconds, desired_count = excluded.desired_count,
                enabled = excluded.enabled, imported_from_ts_guid = excluded.imported_from_ts_guid;
            """,
            ("$id", GuidBlob.ToBlob(row.Id)),
            ("$target", GuidBlob.ToBlob(row.TargetId)),
            ("$template", GuidBlob.ToBlob(row.ExposureTemplateId)),
            ("$exposure", Nullable(row.ExposureSeconds)),
            ("$desired", row.DesiredCount),
            ("$enabled", row.Enabled ? 1 : 0),
            ("$ts_guid", Nullable(row.ImportedFromTsGuid)));
    }

    /// <summary>Resolves a <c>project</c> row id from its provenance key, or <see langword="null"/> when no row carries it.</summary>
    /// <exception cref="IntentStoreException">More than one row carries the key (duplicate provenance).</exception>
    public Guid? FindProjectId(string importedFromTsGuid, SqliteTransaction? transaction = null) =>
        FindId("project", importedFromTsGuid, transaction);

    /// <summary>Resolves a <c>target</c> row id from its provenance key, or <see langword="null"/> when no row carries it.</summary>
    /// <exception cref="IntentStoreException">More than one row carries the key (duplicate provenance).</exception>
    public Guid? FindTargetId(string importedFromTsGuid, SqliteTransaction? transaction = null) =>
        FindId("target", importedFromTsGuid, transaction);

    /// <summary>Resolves an <c>exposure_template</c> row id from its provenance key, or <see langword="null"/> when no row carries it.</summary>
    /// <exception cref="IntentStoreException">More than one row carries the key (duplicate provenance).</exception>
    public Guid? FindExposureTemplateId(string importedFromTsGuid, SqliteTransaction? transaction = null) =>
        FindId("exposure_template", importedFromTsGuid, transaction);

    /// <summary>Resolves an <c>exposure_plan</c> row id from its provenance key, or <see langword="null"/> when no row carries it.</summary>
    /// <exception cref="IntentStoreException">More than one row carries the key (duplicate provenance).</exception>
    public Guid? FindExposurePlanId(string importedFromTsGuid, SqliteTransaction? transaction = null) =>
        FindId("exposure_plan", importedFromTsGuid, transaction);

    private Guid? FindId(string table, string importedFromTsGuid, SqliteTransaction? transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importedFromTsGuid);

        using SqliteCommand command = _store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT id FROM {table} WHERE imported_from_ts_guid = $guid;";
        command.Parameters.AddWithValue("$guid", importedFromTsGuid);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        Guid id = GuidBlob.FromBlob(reader.GetFieldValue<byte[]>(0));
        if (reader.Read())
        {
            throw new IntentStoreException(
                $"Intent store: {table} provenance key '{importedFromTsGuid}' matches more than one row — " +
                "duplicate provenance is a data-integrity violation, not something to disambiguate silently.");
        }

        return id;
    }

    private static object Nullable<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    private static object Nullable(string? value) => value is null ? DBNull.Value : value;

    private void Execute(SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _store.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
