using Astronomy.Catalog.Build;
using Astronomy.Catalog.Data;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Schema;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog;

/// <summary>
/// Read/write access to a <c>Catalog.db</c> file. TCM (the writer) opens via <see cref="Open"/>; read-only
/// consumers via <see cref="OpenReadOnly"/>. Owns a single open connection; dispose to close. The catalog is fully
/// derived, so writing is one atomic full-rebuild — <see cref="WriteCatalog"/> replaces the entire graph from a
/// resolved <see cref="CatalogGraph"/> (see <see cref="CatalogBuilder"/>). The individual <c>Insert*</c> methods
/// back that rebuild and remain available for ad-hoc/test use.
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

    // ---- Plan-plane inserts (optionally inside a transaction) ----------------

    /// <summary>Inserts a <see cref="Profile"/> row.</summary>
    public void InsertProfile(Profile p, SqliteTransaction? transaction = null) => Execute(transaction,
        "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, $name, $nina, $created);",
        ("$id", GuidBlob.ToBlob(p.Id)), ("$name", p.Name), ("$nina", p.NinaProfileGuid), ("$created", p.CreatedAt));

    /// <summary>Inserts a <see cref="Project"/> row.</summary>
    public void InsertProject(Project p, SqliteTransaction? transaction = null) => Execute(transaction,
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

    /// <summary>Inserts a canonical <see cref="Target"/> row (disk identity + plan attributes).</summary>
    public void InsertTarget(Target t, SqliteTransaction? transaction = null) => Execute(transaction,
        """
        INSERT INTO target (id, source_id, project_id, name, enabled, ra_hours, dec_degrees_signed, epoch_id,
            rotation_deg, roi_percent, priority_id, directory_name, catalog, common_name, object_name, scanned_at,
            created_at, imported_from_ts_guid)
        VALUES ($id, $source, $project, $name, $enabled, $ra, $dec, $epoch, $rotation, $roi, $priority, $dir, $cat,
            $common, $obj, $scanned, $created, $ts);
        """,
        ("$id", GuidBlob.ToBlob(t.Id)), ("$source", (int)t.Source),
        ("$project", t.ProjectId is Guid pid ? GuidBlob.ToBlob(pid) : null), ("$name", t.Name),
        ("$enabled", Bit(t.Enabled)), ("$ra", t.RaHours), ("$dec", t.DecDegreesSigned), ("$epoch", (int)t.Epoch),
        ("$rotation", t.RotationDeg), ("$roi", t.RoiPercent), ("$priority", (int?)t.Priority),
        ("$dir", t.DirectoryName), ("$cat", t.Catalog), ("$common", t.CommonName), ("$obj", t.ObjectName),
        ("$scanned", t.ScannedAt), ("$created", t.CreatedAt), ("$ts", t.ImportedFromTsGuid));

    /// <summary>Inserts an <see cref="InventoryFilter"/> row (per-target/filter actuals).</summary>
    public void InsertInventoryFilter(InventoryFilter f, SqliteTransaction? transaction = null) => Execute(transaction,
        """
        INSERT INTO inventory_filter (target_id, filter_code, frame_purpose_id, filter_name, exposure_count,
            total_integration_seconds, first_imaged_at, last_imaged_at, typical_gain, typical_offset,
            typical_set_temp_c, typical_binning_x, typical_binning_y, exposure_seconds, cameras)
        VALUES ($target, $code, $purpose, $name, $count, $integ, $first, $last, $gain, $offset, $temp, $binx, $biny,
            $exp, $cameras);
        """,
        ("$target", GuidBlob.ToBlob(f.TargetId)), ("$code", f.FilterCode), ("$purpose", (int)f.Purpose),
        ("$name", f.FilterName), ("$count", f.ExposureCount), ("$integ", f.TotalIntegrationSeconds),
        ("$first", f.FirstImagedAt), ("$last", f.LastImagedAt), ("$gain", f.TypicalGain), ("$offset", f.TypicalOffset),
        ("$temp", f.TypicalSetTempC), ("$binx", f.TypicalBinningX), ("$biny", f.TypicalBinningY),
        ("$exp", f.ExposureSeconds), ("$cameras", f.Cameras));

    /// <summary>Inserts an <see cref="ExposureTemplate"/> row.</summary>
    public void InsertExposureTemplate(ExposureTemplate t, SqliteTransaction? transaction = null) => Execute(transaction,
        """
        INSERT INTO exposure_template (id, profile_id, name, filter_name, gain, offset_adu, binning, readout_mode,
            default_exposure_seconds, imported_from_ts_guid)
        VALUES ($id, $profile, $name, $filter, $gain, $offset, $binning, $readout, $defExp, $ts);
        """,
        ("$id", GuidBlob.ToBlob(t.Id)), ("$profile", GuidBlob.ToBlob(t.ProfileId)), ("$name", t.Name),
        ("$filter", t.FilterName), ("$gain", t.Gain), ("$offset", t.OffsetAdu), ("$binning", t.Binning),
        ("$readout", t.ReadoutMode), ("$defExp", t.DefaultExposureSeconds), ("$ts", t.ImportedFromTsGuid));

    /// <summary>Inserts an <see cref="ExposurePlan"/> row.</summary>
    public void InsertExposurePlan(ExposurePlan p, SqliteTransaction? transaction = null) => Execute(transaction,
        """
        INSERT INTO exposure_plan (id, target_id, exposure_template_id, exposure_seconds, desired_count,
            acquired_count, accepted_count, enabled, imported_from_ts_guid)
        VALUES ($id, $target, $template, $exp, $desired, $acquired, $accepted, $enabled, $ts);
        """,
        ("$id", GuidBlob.ToBlob(p.Id)), ("$target", GuidBlob.ToBlob(p.TargetId)),
        ("$template", GuidBlob.ToBlob(p.ExposureTemplateId)), ("$exp", p.ExposureSeconds),
        ("$desired", p.DesiredCount), ("$acquired", p.AcquiredCount), ("$accepted", p.AcceptedCount),
        ("$enabled", Bit(p.Enabled)), ("$ts", p.ImportedFromTsGuid));

    /// <summary>
    /// Replaces the entire catalog with a resolved <see cref="CatalogGraph"/> (the full rebuild). Transactional:
    /// clears every plan/inventory table, then inserts in foreign-key order
    /// (profile → project → exposure_template → target → exposure_plan → inventory_filter).
    /// </summary>
    public void WriteCatalog(CatalogGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        using SqliteTransaction tx = _connection.BeginTransaction();

        foreach (string table in new[] { "inventory_filter", "exposure_plan", "target", "exposure_template", "project", "profile" })
            Execute(tx, $"DELETE FROM {table};");

        foreach (Profile p in graph.Profiles) InsertProfile(p, tx);
        foreach (Project p in graph.Projects) InsertProject(p, tx);
        foreach (ExposureTemplate t in graph.Templates) InsertExposureTemplate(t, tx);
        foreach (Target t in graph.Targets) InsertTarget(t, tx);
        foreach (ExposurePlan p in graph.Plans) InsertExposurePlan(p, tx);
        foreach (InventoryFilter f in graph.InventoryFilters) InsertInventoryFilter(f, tx);

        tx.Commit();
    }

    // ---- Plan-plane reads --------------------------------------------------

    /// <summary>All profiles.</summary>
    public IReadOnlyList<Profile> GetProfiles() => Query("SELECT * FROM profile;", ProfileMapper.Instance);

    /// <summary>All projects.</summary>
    public IReadOnlyList<Project> GetProjects() => Query("SELECT * FROM project;", ProjectMapper.Instance);

    /// <summary>All targets (actual, planned, and both).</summary>
    public IReadOnlyList<Target> GetTargets() => Query("SELECT * FROM target;", TargetMapper.Instance);

    /// <summary>
    /// Targets that have frames on disk — i.e. they have been shot (<see cref="TargetSource.Actual"/> OR
    /// <see cref="TargetSource.Both"/>). This is XFM's actual-only world: a <c>Both</c> target has been shot (it
    /// just also carries a plan), so it belongs here; only planned-only targets (no files yet) are excluded.
    /// </summary>
    public IReadOnlyList<Target> GetShotTargets() =>
        Query("SELECT * FROM target WHERE source_id IN (0, 2);", TargetMapper.Instance);

    /// <summary>Targets belonging to a project.</summary>
    public IReadOnlyList<Target> GetTargets(Guid projectId) =>
        Query("SELECT * FROM target WHERE project_id = $p;", TargetMapper.Instance, ("$p", GuidBlob.ToBlob(projectId)));

    /// <summary>All exposure templates.</summary>
    public IReadOnlyList<ExposureTemplate> GetExposureTemplates() =>
        Query("SELECT * FROM exposure_template;", ExposureTemplateMapper.Instance);

    /// <summary>Exposure plans for a target.</summary>
    public IReadOnlyList<ExposurePlan> GetExposurePlans(Guid targetId) =>
        Query("SELECT * FROM exposure_plan WHERE target_id = $t;", ExposurePlanMapper.Instance, ("$t", GuidBlob.ToBlob(targetId)));

    /// <summary>All exposure plans.</summary>
    public IReadOnlyList<ExposurePlan> GetExposurePlans() =>
        Query("SELECT * FROM exposure_plan;", ExposurePlanMapper.Instance);

    // ---- Inventory ---------------------------------------------------------

    /// <summary>All per-filter inventory rows.</summary>
    public IReadOnlyList<InventoryFilter> GetInventoryFilters() =>
        Query("SELECT * FROM inventory_filter;", InventoryFilterMapper.Instance);

    /// <summary>Per-filter inventory rows for one canonical target.</summary>
    public IReadOnlyList<InventoryFilter> GetInventoryFilters(Guid targetId) =>
        Query("SELECT * FROM inventory_filter WHERE target_id = $t;", InventoryFilterMapper.Instance, ("$t", GuidBlob.ToBlob(targetId)));

    // ---- Reconciliation (goal vs actual) -----------------------------------

    /// <summary>
    /// Goal (TS <c>desired_count</c>) vs actual (disk inventory) per target/filter, for every target. Actual is
    /// disk truth; TS's own acquired counts are ignored. <paramref name="policy"/> chooses whether Stars frames
    /// count toward a filter's goal (default <see cref="ReconcilePolicy.Combined"/>).
    /// </summary>
    public IReadOnlyList<TargetReconciliation> GetReconciliation(ReconcilePolicy policy = ReconcilePolicy.Combined) =>
        Reconciler.Reconcile(GetTargets(), GetExposurePlans(), GetExposureTemplates(), GetInventoryFilters(), policy);

    // ---- Helpers -----------------------------------------------------------

    private void Execute(SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
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
