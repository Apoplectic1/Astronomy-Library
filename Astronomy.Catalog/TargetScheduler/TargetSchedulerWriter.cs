using Astronomy.Catalog.Scan;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>One staged/applied count change: TS plan <see cref="TsExposurePlanId"/> goes from its current
/// <see cref="OldAcquired"/>/<see cref="OldAccepted"/> to <see cref="NewCount"/> (written to both columns).</summary>
public sealed record WriteBackChange(
    long TsExposurePlanId,
    string TargetName,
    string Filter,
    FilterPurpose Purpose,
    int OldAcquired,
    int OldAccepted,
    int NewCount)
{
    /// <summary>True when the disk count is below either current TS count (disk wins, but worth flagging).</summary>
    public bool IsDecrease => NewCount < OldAcquired || NewCount < OldAccepted;

    /// <summary>True when both columns already equal the disk count (nothing to write).</summary>
    public bool IsNoOp => NewCount == OldAcquired && NewCount == OldAccepted;
}

/// <summary>A post-commit read-back mismatch: the row did not end up at the expected count.</summary>
public sealed record WriteBackVerifyFailure(long TsExposurePlanId, int Expected, int ActualAcquired, int ActualAccepted);

/// <summary>The outcome of <see cref="TargetSchedulerWriter.Execute"/>: the per-row diff, whether it was applied, and any verify failures.</summary>
public sealed record WriteBackResult(
    IReadOnlyList<WriteBackChange> Changes,
    bool Applied,
    IReadOnlyList<WriteBackVerifyFailure> VerifyFailures);

/// <summary>
/// Writes reconciled disk counts into a <b>local</b> N.I.N.A. Target Scheduler <c>schedulerdb.sqlite</c> copy
/// (never the live imaging-PC db). Mirrors <see cref="TargetSchedulerReader"/>'s hardening but opens
/// <c>Mode=ReadWrite</c>: busy-timeout, explicit columns, and <see cref="HasRequiredColumns"/> /
/// <see cref="HasOpenSidecar"/> / <see cref="IsReadOnly"/> exposed so the caller can refuse an incompatible or
/// apparently-open db (validated by <c>exposureplan</c> column presence, not exact <see cref="SchemaUserVersion"/>,
/// which TS bumps on every nightly migration). Sets only
/// <c>exposureplan.acquired</c>/<c>accepted</c> (no <c>acquiredimage</c> rows); it never alters the journal mode,
/// so TS's rollback-journal db is left as-is. Dispose to close. Transitional — retires at the IS/ISP cutover.
/// </summary>
public sealed class TargetSchedulerWriter : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens <paramref name="schedulerDbPath"/> read-write with a busy-timeout.</summary>
    public TargetSchedulerWriter(string schedulerDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerDbPath);

        // Capture sidecar presence BEFORE opening — opening (or the first write) can itself create -journal/-wal.
        HasOpenSidecar =
            File.Exists(schedulerDbPath + "-wal") ||
            File.Exists(schedulerDbPath + "-shm") ||
            File.Exists(schedulerDbPath + "-journal");

        // A copy of a read-only snapshot keeps the read-only attribute; ReadWrite opens but writes fail at commit
        // time with a cryptic "readonly database". Capture it so the caller can refuse with a clear message.
        IsReadOnly = new FileInfo(schedulerDbPath).IsReadOnly;

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = schedulerDbPath,
            Mode = SqliteOpenMode.ReadWrite,   // must already exist; never create, never read-only
            // Private cache (the default): the writer is the exclusive writer of a local copy and must NOT join a
            // read-only shared cache left alive by the pooled reader connection from the preceding build
            // (that yields SQLITE_READONLY on the first write).
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

        // Validate the writer's actual contract — the columns it updates — instead of an exact user_version. TS
        // bumps user_version on every NINA-nightly migration, but the exposureplan columns it touches are stable.
        HasRequiredColumns = ExposurePlanHasColumns("Id", "acquired", "accepted");
    }

    /// <summary>The TS database's <c>PRAGMA user_version</c>.</summary>
    public long SchemaUserVersion { get; }

    /// <summary>True when a <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecar existed at open time (db may be open elsewhere).</summary>
    public bool HasOpenSidecar { get; }

    /// <summary>True when the db file has the read-only attribute (a copy of a protected snapshot keeps it; writes would fail).</summary>
    public bool IsReadOnly { get; }

    /// <summary>True when <c>exposureplan</c> has the <c>Id</c>/<c>acquired</c>/<c>accepted</c> columns write-back updates — its real contract, independent of TS's churning schema version.</summary>
    public bool HasRequiredColumns { get; }

    /// <summary>
    /// Reads each planned row's current counts to form the diff. When <paramref name="apply"/> is true, writes all
    /// rows in one transaction and read-back verifies; otherwise returns the diff with <c>Applied = false</c>.
    /// </summary>
    public WriteBackResult Execute(WriteBackPlan plan, bool apply)
    {
        ArgumentNullException.ThrowIfNull(plan);

        List<WriteBackChange> changes = [];
        foreach (PlannedWrite w in plan.Writes)
        {
            (int acquired, int accepted) = ReadCounts(w.TsExposurePlanId) ?? (-1, -1);
            changes.Add(new WriteBackChange(
                w.TsExposurePlanId, w.TargetName, w.Filter, w.Purpose, acquired, accepted, w.DiskCount));
        }

        if (!apply)
            return new WriteBackResult(changes, Applied: false, []);

        using (SqliteTransaction tx = _connection.BeginTransaction())
        {
            foreach (PlannedWrite w in plan.Writes)
            {
                using SqliteCommand cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE exposureplan SET acquired = $n, accepted = $n WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$n", w.DiskCount);
                cmd.Parameters.AddWithValue("$id", w.TsExposurePlanId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        List<WriteBackVerifyFailure> failures = [];
        foreach (PlannedWrite w in plan.Writes)
        {
            (int acquired, int accepted) = ReadCounts(w.TsExposurePlanId) ?? (-1, -1);
            if (acquired != w.DiskCount || accepted != w.DiskCount)
                failures.Add(new WriteBackVerifyFailure(w.TsExposurePlanId, w.DiskCount, acquired, accepted));
        }

        return new WriteBackResult(changes, Applied: true, failures);
    }

    private (int Acquired, int Accepted)? ReadCounts(long tsExposurePlanId)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT acquired, accepted FROM exposureplan WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", tsExposurePlanId);
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        int acquired = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        int accepted = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        return (acquired, accepted);
    }

    private bool ExposurePlanHasColumns(params string[] required)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(exposureplan);";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));   // PRAGMA table_info column 1 = name

        return required.All(columns.Contains);
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
