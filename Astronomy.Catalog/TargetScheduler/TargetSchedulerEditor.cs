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

/// <summary>Why a guarded field write was refused — a structured reason the consumer maps to its own wording
/// (this library names no consumer). <see cref="None"/> means the write proceeded.
/// <see cref="HasOverrideOrder"/>: a cadence-clearing target-scope edit was refused because the target has
/// hand-authored <c>overrideexposureorderitem</c> rows — index-coupled to the plan set, so honoring the edit
/// would require deleting user-authored data; re-author the order in the TS editor instead.</summary>
public enum RefusalReason { None, SchemaIncompatible, ReadOnly, OpenSidecar, ColumnAbsent, HasOverrideOrder }

/// <summary>
/// Edits individual fields of a <b>local</b> N.I.N.A. Target Scheduler <c>schedulerdb.sqlite</c> copy (never the
/// live imaging-PC db). Sibling to <see cref="TargetSchedulerReader"/> / <see cref="TargetSchedulerWriter"/>:
/// opens <c>Mode=ReadWrite</c> with the same hardening — a <b>private</b> SQLite cache (so it never inherits a
/// read-only shared cache left by a pooled reader), a busy-timeout, and column-presence guards
/// (<see cref="HasRequiredColumns"/> / <see cref="HasOpenSidecar"/> / <see cref="IsReadOnly"/>) so the caller can
/// refuse an incompatible or apparently-open db — validated by column presence, not an exact schema version
/// (TS bumps that every nightly migration). Each write is read-back verified. Transitional, retires at the IS/ISP
/// cutover.
/// <para>
/// The editable surface is the declarative <see cref="TsEditableSchema"/>: <see cref="SetField"/> writes (and
/// <see cref="ReadField"/> reads) any column in that reference, validated against it — the reference doubles as
/// the SQL-injection whitelist. At open the editor reflects each table's columns (<c>PRAGMA table_info</c>) so
/// <see cref="IsFieldAvailable"/> can tell the caller whether a referenced field actually exists on this db
/// version. Fields with a cadence clear scope (<see cref="TsCadenceClear"/>) delete the
/// invalidated <c>filtercadenceitem</c> rows in the same transaction as the write (empty is always safe — TS
/// regenerates); unchanged values are verified no-ops; a target-scope edit refuses when hand-authored
/// override-order rows exist.
/// </para>
/// </summary>
public sealed class TargetSchedulerEditor : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<TsTable, HashSet<string>> _presentColumns;

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

        _presentColumns = new()
        {
            [TsTable.Project] = ReadColumns("project"),
            [TsTable.Target] = ReadColumns("target"),
            [TsTable.ExposurePlan] = ReadColumns("exposureplan"),
            [TsTable.ExposureTemplate] = ReadColumns("exposuretemplate"),
        };
        HasRequiredColumns = _presentColumns[TsTable.Target].IsSupersetOf(["Id", "guid", "active"]);
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

    /// <summary>
    /// Sets one editable field (per <see cref="TsEditableSchema"/>) on the row of <paramref name="table"/> keyed
    /// by <paramref name="tsKey"/> — the catalog's <c>imported_from_ts_guid</c> (the TS <c>guid</c>, or the integer
    /// <c>Id</c> as a string when it has none — a guid never parses as a long, so the key form is self-describing).
    /// Reads the prior value, updates the one whitelisted column, and read-back verifies. Throws
    /// <see cref="ArgumentException"/> if <paramref name="column"/> is not an editable field. The reference's column
    /// spelling (not the caller's casing) is what reaches the SQL.
    /// </summary>
    public FieldEditResult SetField(TsTable table, string tsKey, string column, object? value)
    {
        TsField field = TsEditableSchema.Find(table, column)
            ?? throw new ArgumentException($"column '{column}' is not an editable {table} field", nameof(column));
        return UpdateField(TsEditableSchema.TableName(table), field, tsKey, value);
    }

    /// <summary>
    /// The guarded entry point: checks the open db's safety predicates in order — required columns present, file
    /// writable, no open <c>-wal</c>/<c>-shm</c>/<c>-journal</c> sidecar, the target column actually present on
    /// this db version, and no hand-authored cadence override order blocking a target-scope clear — and, only if
    /// all pass, performs the read-back-verified <see cref="SetField"/>. Returns the
    /// edit result with <see cref="RefusalReason.None"/>, or a null result with the structured reason it refused. The
    /// caller owns the user-facing wording; this collapses the five predicates a consumer would otherwise re-assemble.
    /// </summary>
    public (FieldEditResult? Result, RefusalReason Refusal) TrySetField(TsTable table, string tsKey, string column, object? value)
    {
        if (!HasRequiredColumns) return (null, RefusalReason.SchemaIncompatible);
        if (IsReadOnly) return (null, RefusalReason.ReadOnly);
        if (HasOpenSidecar) return (null, RefusalReason.OpenSidecar);
        if (!IsFieldAvailable(table, column)) return (null, RefusalReason.ColumnAbsent);
        // A target-scope cadence clear must not orphan hand-authored override-order rows (index-coupled to
        // the plan set); deleting them is data loss, so the edit refuses instead. Project scope mirrors TS,
        // whose filter-switch-frequency path leaves override orders untouched.
        TsField field = TsEditableSchema.Find(table, column)!;   // non-null: IsFieldAvailable passed
        if (field.Clears == TsCadenceClear.Target && TargetHasOverrideOrder(TsEditableSchema.TableName(table), tsKey))
            return (null, RefusalReason.HasOverrideOrder);
        return (SetField(table, tsKey, column, value), RefusalReason.None);
    }

    /// <summary>
    /// Reads the current value of one editable field (per <see cref="TsEditableSchema"/>) for surfacing — e.g. to
    /// seed an edit control — keyed like <see cref="SetField"/>. <c>Found</c> is false for an unknown key; the value
    /// is the raw SQLite value (a boxed <c>long</c>/<c>double</c>/<c>string</c>) or <c>null</c> for SQL NULL.
    /// </summary>
    public (bool Found, object? Value) ReadField(TsTable table, string tsKey, string column)
    {
        TsField field = TsEditableSchema.Find(table, column)
            ?? throw new ArgumentException($"column '{column}' is not an editable {table} field", nameof(column));
        ArgumentException.ThrowIfNullOrWhiteSpace(tsKey);
        bool byId = long.TryParse(tsKey, out long id);
        string where = byId ? "Id = $key" : "guid = $key";
        object keyObj = byId ? id : tsKey;
        return ReadRaw(TsEditableSchema.TableName(table), field.Column, where, keyObj);
    }

    /// <summary>
    /// Reads one exposure plan's <em>effective</em> exposure — its own value, unless it holds the negative
    /// defer-to-template sentinel (see <see cref="TsField.Sentinel"/>), then its template's default — keyed
    /// like <see cref="SetField"/> (guid-or-Id). Resolves the same rule the scheduler applies (TS's planner
    /// tests <c>!= -1</c>; the Library treats every negative as the sentinel — indistinguishable in-contract,
    /// and 0 is a literal zero-second exposure in both, matching <see cref="EffectiveExposure"/>), so a
    /// consumer can surface the resolved value without re-implementing the join. <c>Found</c> is false for an
    /// unknown key or a plan whose template row is missing.
    /// </summary>
    public (bool Found, double? Value) ReadPlanEffectiveExposure(string tsPlanKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tsPlanKey);
        bool byId = long.TryParse(tsPlanKey, out long id);
        string where = byId ? "ep.Id = $key" : "ep.guid = $key";
        object key = byId ? id : tsPlanKey;

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT CASE WHEN ep.exposure < 0 THEN et.defaultexposure ELSE ep.exposure END " +
            $"FROM exposureplan ep JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId WHERE {where};";
        cmd.Parameters.AddWithValue("$key", key);
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? (true, r.IsDBNull(0) ? null : r.GetDouble(0)) : (false, null);
    }

    /// <summary>Sets one editable <c>target</c> column on the row keyed by guid-or-Id (thin wrapper over <see cref="SetField"/>).</summary>
    public FieldEditResult SetTargetField(string tsTargetKey, string column, object? value) =>
        SetField(TsTable.Target, tsTargetKey, column, value);

    /// <summary>Sets one editable <c>exposureplan</c> column on the plan keyed by guid-or-Id (wrapper over <see cref="SetField"/>).</summary>
    public FieldEditResult SetPlanField(string tsPlanKey, string column, object? value) =>
        SetField(TsTable.ExposurePlan, tsPlanKey, column, value);

    /// <summary>Sets one editable <c>project</c> column on the project keyed by guid-or-Id (wrapper over <see cref="SetField"/>).</summary>
    public FieldEditResult SetProjectField(string tsProjectKey, string column, object? value) =>
        SetField(TsTable.Project, tsProjectKey, column, value);

    /// <summary>True when <paramref name="column"/> for <paramref name="table"/> is both an editable field (in
    /// <see cref="TsEditableSchema"/>) and physically present in this db — a schema-drift guard, since TS adds
    /// columns across migrations and an older db may lack one. False for an unknown/non-editable or missing column.</summary>
    public bool IsFieldAvailable(TsTable table, string column) =>
        TsEditableSchema.Find(table, column) is { } field
        && _presentColumns.TryGetValue(table, out HashSet<string>? cols)
        && cols.Contains(field.Column);

    // Resolve the row by guid-or-Id (a guid never parses as long), read the old value, UPDATE the one column, and
    // read-back verify. Table + column are whitelisted literals; key + value are parameterised.
    // An unchanged value is a verified no-op (no UPDATE, no cadence clear — mirrors TS, whose own setters only
    // mark a breaking change on !=). A field with a cadence clear scope deletes the invalidated
    // filtercadenceitem rows IN THE SAME TRANSACTION as the column write: TS restores those rows verbatim and
    // regenerates only from empty, so update-without-clear (or a crash between the two) is exactly the
    // silent-wrong-rotation state this exists to prevent.
    private FieldEditResult UpdateField(string table, TsField field, string key, object? value)
    {
        string column = field.Column;
        (string where, object keyObj) = KeyClause(key);

        (bool found, object? old) = ReadRaw(table, column, where, keyObj);
        if (!found)
            return new FieldEditResult(RowFound: false, OldValue: null, Verified: false);
        if (NormalizedEquals(old, value))
            return new FieldEditResult(RowFound: true, OldValue: ToText(old), Verified: true);

        using (SqliteTransaction tx = _connection.BeginTransaction())
        {
            using (SqliteCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE {table} SET {column} = $v WHERE {where};";
                cmd.Parameters.AddWithValue("$v", value ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$key", keyObj);
                cmd.ExecuteNonQuery();
            }
            if (field.Clears != TsCadenceClear.None)
            {
                using SqliteCommand clear = _connection.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = field.Clears switch
                {
                    TsCadenceClear.Target =>
                        $"DELETE FROM filtercadenceitem WHERE targetid = (SELECT targetid FROM {table} WHERE {where});",
                    _ =>
                        $"DELETE FROM filtercadenceitem WHERE targetid IN " +
                        $"(SELECT Id FROM target WHERE projectid = (SELECT Id FROM {table} WHERE {where}));",
                };
                clear.Parameters.AddWithValue("$key", keyObj);
                clear.ExecuteNonQuery();
            }
            tx.Commit();
        }

        (_, object? readBack) = ReadRaw(table, column, where, keyObj);
        return new FieldEditResult(RowFound: true, OldValue: ToText(old), Verified: NormalizedEquals(readBack, value));
    }

    // True when the row's target (via the row table's targetid) has hand-authored override-order rows.
    private bool TargetHasOverrideOrder(string table, string key)
    {
        (string where, object keyObj) = KeyClause(key);
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            $"SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid = (SELECT targetid FROM {table} WHERE {where});";
        cmd.Parameters.AddWithValue("$key", keyObj);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture) > 0;
    }

    private static (string Where, object Key) KeyClause(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        bool byId = long.TryParse(key, out long id);
        return (byId ? "Id = $key" : "guid = $key", byId ? id : key);
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

    // The column names present on one table (case-insensitive), or an empty set if the table is absent — PRAGMA
    // on a missing table simply yields no rows. The table name is one of the four reference literals, not input.
    private HashSet<string> ReadColumns(string table)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));   // PRAGMA table_info column 1 = name
        return columns;
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
