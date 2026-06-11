using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>The outcome of one <c>target</c> field edit: whether the row was found, its prior value, and whether
/// the read-back matched the requested value.</summary>
public sealed record TargetEditResult(bool RowFound, int? OldActive, bool Verified)
{
    /// <summary>True when the row was found and the read-back confirmed the new value.</summary>
    public bool Succeeded => RowFound && Verified;
}

/// <summary>Outcome of one generic field edit: whether the row was found, its prior value (as text, for the
/// audit trail), and whether the read-back matched the requested value.</summary>
public sealed record FieldEditResult(bool RowFound, string? OldValue, bool Verified)
{
    /// <summary>True when the row was found and the read-back confirmed the new value.</summary>
    public bool Succeeded => RowFound && Verified;
}

/// <summary>
/// Edits individual fields of a <b>local</b> N.I.N.A. Target Scheduler <c>schedulerdb.sqlite</c> copy (never the
/// live imaging-PC db). Sibling to <see cref="TargetSchedulerReader"/> / <see cref="TargetSchedulerWriter"/>:
/// opens <c>Mode=ReadWrite</c> with the same hardening — a <b>private</b> SQLite cache (so it never inherits a
/// read-only shared cache left by a pooled reader), a busy-timeout, and column-presence guards
/// (<see cref="HasRequiredColumns"/> / <see cref="HasOpenSidecar"/> / <see cref="IsReadOnly"/>) so the caller can
/// refuse an incompatible or apparently-open db — validated by column presence, not an exact schema version
/// (TS bumps that every nightly migration). Each write is read-back verified. Transitional, retires at the IS/ISP
/// cutover. The surface is intentionally minimal — currently <c>target.active</c> — and grows as the editor does.
/// </summary>
public sealed class TargetSchedulerEditor : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens <paramref name="schedulerDbPath"/> read-write with a busy-timeout.</summary>
    public TargetSchedulerEditor(string schedulerDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerDbPath);

        // Capture sidecar presence BEFORE opening — opening can itself create -journal/-wal.
        HasOpenSidecar =
            File.Exists(schedulerDbPath + "-wal") ||
            File.Exists(schedulerDbPath + "-shm") ||
            File.Exists(schedulerDbPath + "-journal");

        // A copy of a read-only snapshot keeps the read-only attribute; ReadWrite opens but writes fail at commit
        // with a cryptic "readonly database". Capture it so the caller can refuse with a clear message.
        IsReadOnly = new FileInfo(schedulerDbPath).IsReadOnly;

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = schedulerDbPath,
            Mode = SqliteOpenMode.ReadWrite,   // must already exist; never create, never read-only
            // Private cache (the default): exclusive writer of a local copy; must NOT join a read-only shared
            // cache left alive by a pooled reader (that yields SQLITE_READONLY on the first write).
        }.ToString());
        _connection.Open();

        using (SqliteCommand pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout = 2000;";
            pragma.ExecuteNonQuery();
        }

        HasRequiredColumns = TargetHasColumns("Id", "guid", "active");
    }

    /// <summary>True when a <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecar existed at open time (db may be open elsewhere).</summary>
    public bool HasOpenSidecar { get; }

    /// <summary>True when the db file has the read-only attribute (writes would fail at commit).</summary>
    public bool IsReadOnly { get; }

    /// <summary>True when <c>target</c> has the <c>Id</c>/<c>guid</c>/<c>active</c> columns this editor needs — its real contract, independent of TS's churning schema version.</summary>
    public bool HasRequiredColumns { get; }

    /// <summary>
    /// Sets <c>target.active</c> on the row identified by <paramref name="tsTargetKey"/> — the catalog's
    /// <c>imported_from_ts_guid</c>, which is the TS target's <c>guid</c> or, when it has none, its integer
    /// <c>Id</c> as a string (a guid never parses as a long, so the key form is self-describing). Reads the prior
    /// value, updates, and read-back verifies. <see cref="TargetEditResult.RowFound"/> is false for an unknown key.
    /// </summary>
    public TargetEditResult SetTargetActive(string tsTargetKey, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tsTargetKey);

        bool byId = long.TryParse(tsTargetKey, out long id);
        string where = byId ? "Id = $key" : "guid = $key";
        object key = byId ? id : tsTargetKey;

        int? old = ReadActive(where, key);
        if (old is null)
            return new TargetEditResult(RowFound: false, OldActive: null, Verified: false);

        int wanted = active ? 1 : 0;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"UPDATE target SET active = $v WHERE {where};";
            cmd.Parameters.AddWithValue("$v", wanted);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.ExecuteNonQuery();
        }

        return new TargetEditResult(RowFound: true, OldActive: old, Verified: ReadActive(where, key) == wanted);
    }

    // Editable columns per table. A write to anything outside these is rejected — the column name is interpolated
    // into the SQL, so this whitelist is also the injection guard. Cadence-SAFE set only for now; the
    // cadence-breaking knobs (exposureplan.enabled, project.filterswitchfrequency) come later with their clear.
    private static readonly HashSet<string> TargetColumns =
        new(StringComparer.OrdinalIgnoreCase) { "active", "priority", "rotation", "roi" };
    private static readonly HashSet<string> PlanColumns =
        new(StringComparer.OrdinalIgnoreCase) { "desired" };
    private static readonly HashSet<string> ProjectColumns = new(StringComparer.OrdinalIgnoreCase)
        { "state", "priority", "minimumaltitude", "maximumaltitude", "horizonoffset", "meridianwindow", "ditherevery", "enablegrader" };

    /// <summary>Sets one editable <c>target</c> column (see <see cref="TargetColumns"/>) on the row keyed by guid-or-Id.</summary>
    public FieldEditResult SetTargetField(string tsTargetKey, string column, object? value) =>
        UpdateField("target", Whitelisted(column, TargetColumns), tsTargetKey, value);

    /// <summary>Sets one editable <c>exposureplan</c> column (currently <c>desired</c>) on the plan keyed by guid-or-Id.</summary>
    public FieldEditResult SetPlanField(string tsPlanKey, string column, object? value) =>
        UpdateField("exposureplan", Whitelisted(column, PlanColumns), tsPlanKey, value);

    /// <summary>Sets one editable <c>project</c> column (see <see cref="ProjectColumns"/>) on the project keyed by guid-or-Id.</summary>
    public FieldEditResult SetProjectField(string tsProjectKey, string column, object? value) =>
        UpdateField("project", Whitelisted(column, ProjectColumns), tsProjectKey, value);

    private static string Whitelisted(string column, HashSet<string> allowed) =>
        allowed.Contains(column)
            ? column
            : throw new ArgumentException($"column '{column}' is not an editable field", nameof(column));

    // Resolve the row by guid-or-Id (a guid never parses as long), read the old value, UPDATE the one column, and
    // read-back verify. Table + column are whitelisted literals; key + value are parameterised.
    private FieldEditResult UpdateField(string table, string column, string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        bool byId = long.TryParse(key, out long id);
        string where = byId ? "Id = $key" : "guid = $key";
        object keyObj = byId ? id : key;

        (bool found, object? old) = ReadRaw(table, column, where, keyObj);
        if (!found)
            return new FieldEditResult(RowFound: false, OldValue: null, Verified: false);

        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"UPDATE {table} SET {column} = $v WHERE {where};";
            cmd.Parameters.AddWithValue("$v", value ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$key", keyObj);
            cmd.ExecuteNonQuery();
        }

        (_, object? readBack) = ReadRaw(table, column, where, keyObj);
        return new FieldEditResult(RowFound: true, OldValue: ToText(old), Verified: NormalizedEquals(readBack, value));
    }

    private (bool Found, object? Value) ReadRaw(string table, string column, string where, object key)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table} WHERE {where};";
        cmd.Parameters.AddWithValue("$key", key);
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? (true, r.IsDBNull(0) ? null : r.GetValue(0)) : (false, null);
    }

    // SQLite hands INTEGER back as long while the caller passes int, so compare via invariant text (1 == 1L,
    // doubles round-trip, null == null).
    private static bool NormalizedEquals(object? a, object? b) =>
        (a is null && b is null) || (a is not null && b is not null && ToText(a) == ToText(b));

    private static string? ToText(object? v) => v is null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);

    private int? ReadActive(string where, object key)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT active FROM target WHERE {where};";
        cmd.Parameters.AddWithValue("$key", key);
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return r.IsDBNull(0) ? 0 : r.GetInt32(0);
    }

    private bool TargetHasColumns(params string[] required)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(target);";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));   // PRAGMA table_info column 1 = name

        return required.All(columns.Contains);
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
