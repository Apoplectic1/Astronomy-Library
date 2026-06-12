using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog;

/// <summary>
/// Opens <c>Catalog.db</c> connections and applies the embedded schema. The catalog builder (the single writer)
/// calls <see cref="Open"/>; read-only consumers call <see cref="OpenReadOnly"/>. There is no migration framework —
/// the catalog is fully derived (disk scan + TS import; goals live in the scheduler DB), so the schema is applied
/// idempotently (CREATE TABLE IF NOT EXISTS + INSERT OR IGNORE) and a schema change is handled by deleting the
/// regenerable database file.
/// </summary>
public static class SchemaManager
{
    private const int BusyTimeoutMs = 2000;

    /// <summary>Opens a read-write connection (creating the file if needed) and ensures the schema exists.</summary>
    public static SqliteConnection Open(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());

        connection.Open();
        Configure(connection, readOnly: false);
        EnsureSchema(connection);
        return connection;
    }

    /// <summary>Opens an existing catalog read-only (shared cache + busy-timeout); does not touch the schema.</summary>
    public static SqliteConnection OpenReadOnly(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        }.ToString());

        connection.Open();
        Configure(connection, readOnly: true);
        return connection;
    }

    private static void Configure(SqliteConnection connection, bool readOnly)
    {
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, $"PRAGMA busy_timeout = {BusyTimeoutMs};");
        if (!readOnly)
        {
            Execute(connection, "PRAGMA journal_mode = WAL;");
            Execute(connection, "PRAGMA synchronous = NORMAL;");
        }
    }

    /// <summary>Runs the embedded <c>schema.sql</c> (idempotent: CREATE TABLE IF NOT EXISTS + INSERT OR IGNORE).</summary>
    private static void EnsureSchema(SqliteConnection connection) => Execute(connection, ReadSchemaSql());

    private static string ReadSchemaSql()
    {
        Assembly assembly = typeof(SchemaManager).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".schema.sql", StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
