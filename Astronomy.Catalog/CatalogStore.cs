using Astronomy.Catalog.Data;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog;

/// <summary>
/// Read/write access to a <c>Catalog.db</c> file. TCM (the writer) opens via <see cref="Open"/>; read-only
/// consumers via <see cref="OpenReadOnly"/>. Owns a single open connection; dispose to close. Holds the plan-plane
/// CRUD plus the disk-derived inventory: <see cref="ReplaceInventory"/> persists an <see cref="ImageLibraryReport"/>
/// (from <see cref="ImageLibraryScanner"/>) into the aggregate inventory tables.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    private readonly SqliteConnection _connection;

    private CatalogStore(SqliteConnection connection) => _connection = connection;

    /// <summary>Opens (creating + ensuring the schema) a read-write catalog at <paramref name="databasePath"/>.</summary>
    public static CatalogStore Open(string databasePath) => new(SchemaManager.Open(databasePath));

    /// <summary>Opens an existing catalog read-only (safe for consumers reading while TCM writes).</summary>
    public static CatalogStore OpenReadOnly(string databasePath) => new(SchemaManager.OpenReadOnly(databasePath));

    /// <summary>The underlying open connection (for advanced/ad-hoc queries).</summary>
    public SqliteConnection Connection => _connection;

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();

    // ---- Plan-plane inserts ------------------------------------------------

    /// <summary>Inserts a <see cref="Profile"/> row.</summary>
    public void InsertProfile(Profile p) => Execute(
        "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, $name, $nina, $created);",
        ("$id", GuidBlob.ToBlob(p.Id)), ("$name", p.Name), ("$nina", p.NinaProfileGuid), ("$created", p.CreatedAt));

    /// <summary>Inserts a <see cref="Project"/> row.</summary>
    public void InsertProject(Project p) => Execute(
        """
        INSERT INTO project (id, profile_id, name, description, state_id, priority_id, minimum_altitude_deg,
            maximum_altitude_deg, minimum_time_minutes, use_custom_horizon, horizon_offset_deg,
            meridian_window_minutes, is_mosaic, enable_grader, created_at, active_at, inactive_at, imported_from_ts_guid)
        VALUES ($id, $profile, $name, $desc, $state, $priority, $minAlt, $maxAlt, $minTime, $customHorizon,
            $horizonOffset, $meridian, $mosaic, $grader, $created, $active, $inactive, $ts);
        """,
        ("$id", GuidBlob.ToBlob(p.Id)), ("$profile", GuidBlob.ToBlob(p.ProfileId)), ("$name", p.Name),
        ("$desc", p.Description), ("$state", (int)p.State), ("$priority", (int)p.Priority),
        ("$minAlt", p.MinimumAltitudeDeg), ("$maxAlt", p.MaximumAltitudeDeg), ("$minTime", p.MinimumTimeMinutes),
        ("$customHorizon", Bit(p.UseCustomHorizon)), ("$horizonOffset", p.HorizonOffsetDeg),
        ("$meridian", p.MeridianWindowMinutes), ("$mosaic", Bit(p.IsMosaic)), ("$grader", Bit(p.EnableGrader)),
        ("$created", p.CreatedAt), ("$active", p.ActiveAt), ("$inactive", p.InactiveAt), ("$ts", p.ImportedFromTsGuid));

    /// <summary>Inserts a <see cref="Target"/> row.</summary>
    public void InsertTarget(Target t) => Execute(
        """
        INSERT INTO target (id, project_id, name, enabled, ra_hours, dec_degrees_signed, epoch_id, rotation_deg,
            roi_percent, priority_id, created_at, imported_from_ts_guid)
        VALUES ($id, $project, $name, $enabled, $ra, $dec, $epoch, $rotation, $roi, $priority, $created, $ts);
        """,
        ("$id", GuidBlob.ToBlob(t.Id)), ("$project", GuidBlob.ToBlob(t.ProjectId)), ("$name", t.Name),
        ("$enabled", Bit(t.Enabled)), ("$ra", t.RaHours), ("$dec", t.DecDegreesSigned), ("$epoch", (int)t.Epoch),
        ("$rotation", t.RotationDeg), ("$roi", t.RoiPercent), ("$priority", (int?)t.Priority),
        ("$created", t.CreatedAt), ("$ts", t.ImportedFromTsGuid));

    /// <summary>Inserts an <see cref="ExposureTemplate"/> row.</summary>
    public void InsertExposureTemplate(ExposureTemplate t) => Execute(
        """
        INSERT INTO exposure_template (id, profile_id, name, filter_name, gain, offset_adu, binning, readout_mode,
            default_exposure_seconds, imported_from_ts_guid)
        VALUES ($id, $profile, $name, $filter, $gain, $offset, $binning, $readout, $defExp, $ts);
        """,
        ("$id", GuidBlob.ToBlob(t.Id)), ("$profile", GuidBlob.ToBlob(t.ProfileId)), ("$name", t.Name),
        ("$filter", t.FilterName), ("$gain", t.Gain), ("$offset", t.OffsetAdu), ("$binning", t.Binning),
        ("$readout", t.ReadoutMode), ("$defExp", t.DefaultExposureSeconds), ("$ts", t.ImportedFromTsGuid));

    /// <summary>Inserts an <see cref="ExposurePlan"/> row.</summary>
    public void InsertExposurePlan(ExposurePlan p) => Execute(
        """
        INSERT INTO exposure_plan (id, target_id, exposure_template_id, exposure_seconds, desired_count,
            acquired_count, accepted_count, enabled, imported_from_ts_guid)
        VALUES ($id, $target, $template, $exp, $desired, $acquired, $accepted, $enabled, $ts);
        """,
        ("$id", GuidBlob.ToBlob(p.Id)), ("$target", GuidBlob.ToBlob(p.TargetId)),
        ("$template", GuidBlob.ToBlob(p.ExposureTemplateId)), ("$exp", p.ExposureSeconds),
        ("$desired", p.DesiredCount), ("$acquired", p.AcquiredCount), ("$accepted", p.AcceptedCount),
        ("$enabled", Bit(p.Enabled)), ("$ts", p.ImportedFromTsGuid));

    // ---- Plan-plane reads --------------------------------------------------

    /// <summary>All projects.</summary>
    public IReadOnlyList<Project> GetProjects() => Query("SELECT * FROM project;", ProjectMapper.Instance);

    /// <summary>All targets.</summary>
    public IReadOnlyList<Target> GetTargets() => Query("SELECT * FROM target;", TargetMapper.Instance);

    /// <summary>Targets belonging to a project.</summary>
    public IReadOnlyList<Target> GetTargets(Guid projectId) =>
        Query("SELECT * FROM target WHERE project_id = $p;", TargetMapper.Instance, ("$p", GuidBlob.ToBlob(projectId)));

    /// <summary>Exposure plans for a target.</summary>
    public IReadOnlyList<ExposurePlan> GetExposurePlans(Guid targetId) =>
        Query("SELECT * FROM exposure_plan WHERE target_id = $t;", ExposurePlanMapper.Instance, ("$t", GuidBlob.ToBlob(targetId)));

    // ---- Inventory ---------------------------------------------------------

    /// <summary>All scanned targets.</summary>
    public IReadOnlyList<InventoryTarget> GetInventoryTargets() =>
        Query("SELECT * FROM inventory_target;", InventoryTargetMapper.Instance);

    /// <summary>All per-filter inventory rows.</summary>
    public IReadOnlyList<InventoryFilter> GetInventoryFilters() =>
        Query("SELECT * FROM inventory_filter;", InventoryFilterMapper.Instance);

    /// <summary>Per-filter inventory rows for one scanned target.</summary>
    public IReadOnlyList<InventoryFilter> GetInventoryFilters(string directoryName) =>
        Query("SELECT * FROM inventory_filter WHERE directory_name = $d;", InventoryFilterMapper.Instance, ("$d", directoryName));

    /// <summary>
    /// Replaces the entire inventory with the aggregates from a fresh scan. Transactional: clears
    /// <c>inventory_filter</c>/<c>inventory_target</c>, then inserts each <see cref="TargetReport"/> and its
    /// <see cref="FilterAggregate"/>s.
    /// </summary>
    public void ReplaceInventory(ImageLibraryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        long scannedAt = new DateTimeOffset(report.ScannedAtUtc).ToUnixTimeSeconds();

        using SqliteTransaction tx = _connection.BeginTransaction();

        ExecuteTx(tx, "DELETE FROM inventory_filter;");
        ExecuteTx(tx, "DELETE FROM inventory_target;");

        foreach (TargetReport t in report.Targets)
        {
            ExecuteTx(tx,
                """
                INSERT INTO inventory_target (directory_name, catalog, common_name, object_name, ra_hours,
                    dec_degrees_signed, scanned_at)
                VALUES ($dir, $cat, $common, $obj, $ra, $dec, $scanned);
                """,
                ("$dir", t.DirectoryName), ("$cat", t.Catalog), ("$common", t.CommonName), ("$obj", t.ObjectName),
                ("$ra", t.RaHours), ("$dec", t.DecDegrees), ("$scanned", scannedAt));

            foreach (FilterAggregate f in t.Filters)
            {
                ExecuteTx(tx,
                    """
                    INSERT INTO inventory_filter (directory_name, filter_code, frame_purpose_id, filter_name,
                        exposure_count, total_integration_seconds, first_imaged_at, last_imaged_at, typical_gain,
                        typical_offset, typical_set_temp_c, typical_binning_x, typical_binning_y,
                        typical_exposure_seconds, cameras)
                    VALUES ($dir, $code, $purpose, $name, $count, $integ, $first, $last, $gain, $offset, $temp,
                        $binx, $biny, $exp, $cameras);
                    """,
                    ("$dir", t.DirectoryName), ("$code", f.FilterCode), ("$purpose", (int)f.Purpose),
                    ("$name", f.FilterName), ("$count", f.ExposureCount),
                    ("$integ", f.TotalIntegration.TotalSeconds),
                    ("$first", new DateTimeOffset(f.FirstImagedUtc).ToUnixTimeSeconds()),
                    ("$last", new DateTimeOffset(f.LastImagedUtc).ToUnixTimeSeconds()),
                    ("$gain", f.Typical.Gain), ("$offset", f.Typical.Offset), ("$temp", f.Typical.SetTempC),
                    ("$binx", f.Typical.Binning.X), ("$biny", f.Typical.Binning.Y),
                    ("$exp", f.Typical.ExposureSec), ("$cameras", string.Join(",", f.CamerasSeen)));
            }
        }

        tx.Commit();
    }

    // ---- Helpers -----------------------------------------------------------

    private void Execute(string sql, params (string Name, object? Value)[] parameters) =>
        ExecuteCore(null, sql, parameters);

    private void ExecuteTx(SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters) =>
        ExecuteCore(transaction, sql, parameters);

    private void ExecuteCore(SqliteTransaction? transaction, string sql, (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, ITableMapper<T> mapper, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        List<T> results = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(mapper.Map(reader));
        return results;
    }

    private static int Bit(bool value) => value ? 1 : 0;
}
