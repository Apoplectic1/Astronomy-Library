using Astronomy.Catalog.Data;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.TargetScheduler;

// Read models mirroring N.I.N.A. Target Scheduler's schedulerdb.sqlite columns. Column names (and their
// casing drift) match the live schema; see TS_SCHEDULER_INGEST.md. Nullable where TS permits NULL.

/// <summary>A TS <c>project</c> row. <c>TsGuid</c> is TS's stable identifier (reused as the catalog id on import).</summary>
public sealed record TsProject(long Id, string ProfileId, string Name, int State, int Priority, double? MinimumAltitude, int IsMosaic, string? TsGuid);

/// <summary>A TS <c>target</c> row. <c>Ra</c> is decimal hours; <c>Dec</c> is signed decimal degrees; <c>EpochCode</c> 2 = J2000.</summary>
public sealed record TsTarget(long Id, string Name, int Active, double? Ra, double? Dec, int EpochCode, double? Rotation, double? Roi, long? ProjectId, int Priority, string? TsGuid);

/// <summary>A TS <c>exposureplan</c> row (desired/acquired/accepted counts per target/filter).</summary>
public sealed record TsExposurePlan(long Id, string ProfileId, double Exposure, int Desired, int Acquired, int Accepted, long TargetId, long ExposureTemplateId, bool Enabled = true);

/// <summary>A TS <c>exposuretemplate</c> row.</summary>
public sealed record TsExposureTemplate(long Id, string ProfileId, string Name, string FilterName, int Gain, int Offset, int Bin, double DefaultExposure);

/// <summary>A TS <c>acquiredimage</c> row (per-frame history).</summary>
public sealed record TsAcquiredImage(long Id, long ProjectId, long TargetId, long AcquiredDate, string FilterName);

/// <summary>
/// Read-only reader for N.I.N.A. Target Scheduler's <c>schedulerdb.sqlite</c>. Opens with
/// <c>Mode=ReadOnly;Cache=Shared</c> and a busy-timeout (TS uses rollback-journal, so a TS writer can briefly
/// block readers), reads with explicit column lists (never <c>SELECT *</c> — TS adds columns over time), and
/// exposes <see cref="SchemaUserVersion"/> so callers can flag a newer-than-tested schema rather than refusing
/// to read. Dispose to close. Phase 4 will add the write-back of reconciled counts.
/// </summary>
public sealed class TargetSchedulerReader : IDisposable
{
    /// <summary>The TS schema version this reader was last validated against (a soft signal — reads proceed regardless; see <see cref="IsNewerThanTested"/>).</summary>
    public const long TestedUserVersion = 25;

    private readonly SqliteConnection _connection;

    /// <summary>Opens <paramref name="schedulerDbPath"/> read-only with a busy-timeout.</summary>
    public TargetSchedulerReader(string schedulerDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerDbPath);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = schedulerDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        _connection.Open();

        using (SqliteCommand pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout = 2000;";
            pragma.ExecuteNonQuery();
        }

        using SqliteCommand version = _connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        SchemaUserVersion = (long)(version.ExecuteScalar() ?? 0L);
    }

    /// <summary>The TS database's <c>PRAGMA user_version</c>.</summary>
    public long SchemaUserVersion { get; }

    /// <summary>True when the database is a newer schema than this reader was validated against.</summary>
    public bool IsNewerThanTested => SchemaUserVersion > TestedUserVersion;

    /// <summary>Reads the whole plan plane (projects, targets, templates, plans) in one pass.</summary>
    /// <param name="ct">Cancellation token, observed per row of each read.</param>
    public TsPlanData ReadPlanData(CancellationToken ct = default) =>
        new(ReadProjects(ct), ReadTargets(ct), ReadExposureTemplates(ct), ReadExposurePlans(ct));

    /// <summary>Reads all TS projects.</summary>
    /// <param name="ct">Cancellation token, observed per row.</param>
    public IReadOnlyList<TsProject> ReadProjects(CancellationToken ct = default) => Query(
        "SELECT Id, profileId, name, state, priority, minimumaltitude, isMosaic, guid FROM project;",
        r => new TsProject(
            r.GetInt64("Id"), r.GetString("profileId"), r.GetString("name"), r.GetInt32("state"),
            r.GetInt32("priority"), r.GetDoubleOrNull("minimumaltitude"), r.GetInt32("isMosaic"),
            r.GetStringOrNull("guid")), ct);

    /// <summary>Reads all TS targets.</summary>
    /// <param name="ct">Cancellation token, observed per row.</param>
    public IReadOnlyList<TsTarget> ReadTargets(CancellationToken ct = default) => Query(
        "SELECT Id, name, active, ra, dec, epochcode, rotation, roi, projectid, priority, guid FROM target;",
        r => new TsTarget(
            r.GetInt64("Id"), r.GetString("name"), r.GetInt32("active"), r.GetDoubleOrNull("ra"),
            r.GetDoubleOrNull("dec"), r.GetInt32("epochcode"), r.GetDoubleOrNull("rotation"),
            r.GetDoubleOrNull("roi"), r.GetInt64OrNull("projectid"), r.GetInt32("priority"),
            r.GetStringOrNull("guid")), ct);

    /// <summary>Reads all TS exposure plans.</summary>
    /// <param name="ct">Cancellation token, observed per row.</param>
    public IReadOnlyList<TsExposurePlan> ReadExposurePlans(CancellationToken ct = default) => Query(
        "SELECT Id, profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled FROM exposureplan;",
        r => new TsExposurePlan(
            r.GetInt64("Id"), r.GetString("profileId"), r.GetDouble("exposure"), r.GetInt32("desired"),
            r.GetInt32("acquired"), r.GetInt32("accepted"), r.GetInt64("targetid"), r.GetInt64("exposureTemplateId"),
            r.GetInt32("enabled") != 0), ct);

    /// <summary>Reads all TS exposure templates.</summary>
    /// <param name="ct">Cancellation token, observed per row.</param>
    public IReadOnlyList<TsExposureTemplate> ReadExposureTemplates(CancellationToken ct = default) => Query(
        "SELECT Id, profileId, name, filtername, gain, offset, bin, defaultexposure FROM exposuretemplate;",
        r => new TsExposureTemplate(
            r.GetInt64("Id"), r.GetString("profileId"), r.GetString("name"), r.GetString("filtername"),
            r.GetInt32("gain"), r.GetInt32("offset"), r.GetInt32("bin"), r.GetDouble("defaultexposure")), ct);

    /// <summary>Reads all TS acquired-image history rows (metadata BLOB/JSON columns deliberately omitted).</summary>
    /// <param name="ct">Cancellation token, observed per row.</param>
    public IReadOnlyList<TsAcquiredImage> ReadAcquiredImages(CancellationToken ct = default) => Query(
        "SELECT Id, projectId, targetId, acquireddate, filtername FROM acquiredimage;",
        r => new TsAcquiredImage(
            r.GetInt64("Id"), r.GetInt64("projectId"), r.GetInt64("targetId"),
            r.GetInt64("acquireddate"), r.GetString("filtername")), ct);

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();

    // The one read choke point: every Read* runs through here, so observing the token here is what makes the
    // whole reader cancellable. Checked per row — a read is a tight loop over an open reader, and a caller
    // that cancels mid-read wants the connection released promptly, not at the end of the table.
    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, CancellationToken ct)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;

        List<T> results = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(map(reader));
        }
        return results;
    }
}
