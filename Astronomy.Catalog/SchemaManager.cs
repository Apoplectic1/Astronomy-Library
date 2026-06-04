using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog;

/// <summary>
/// Opens <c>Catalog.db</c> connections and applies the embedded SQL migrations. TCM (the writer) calls
/// <see cref="Open"/>; read-only consumers (XFM / TP / IS / ISP) call <see cref="OpenReadOnly"/>. Migrations
/// are the <c>0001_init.sql</c>-style scripts embedded under <c>Schema/Migrations/</c>; each runs once, in a
/// transaction, recorded in the <c>schema_migration</c> table with <c>PRAGMA user_version</c> kept in sync.
/// </summary>
public static class SchemaManager
{
    /// <summary>Name of the migration-tracking table (R8).</summary>
    public const string MigrationTable = "schema_migration";

    private const int BusyTimeoutMs = 2000;

    /// <summary>
    /// Opens a read-write connection to the database at <paramref name="databasePath"/> (creating the file if
    /// needed), enables WAL, and applies any pending migrations. The returned connection is open and owned by
    /// the caller.
    /// </summary>
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
        Migrate(connection);
        return connection;
    }

    /// <summary>
    /// Opens a read-only, shared-cache connection with a busy-timeout — the safe shape for consumers reading a
    /// DB another process may be writing. Does not run migrations (the writer owns schema evolution).
    /// </summary>
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

    /// <summary>Returns <c>PRAGMA user_version</c> — the highest applied migration version.</summary>
    public static long GetUserVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
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

    private static void Migrate(SqliteConnection connection)
    {
        Execute(connection,
            $"CREATE TABLE IF NOT EXISTS {MigrationTable} (" +
            "version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at INTEGER NOT NULL) WITHOUT ROWID;");

        HashSet<long> applied = [];
        using (SqliteCommand query = connection.CreateCommand())
        {
            query.CommandText = $"SELECT version FROM {MigrationTable};";
            using SqliteDataReader reader = query.ExecuteReader();
            while (reader.Read())
                applied.Add(reader.GetInt64(0));
        }

        foreach (Migration migration in DiscoverMigrations())
        {
            if (applied.Contains(migration.Version))
                continue;

            using SqliteTransaction transaction = connection.BeginTransaction();

            Execute(connection, migration.Sql, transaction);

            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    $"INSERT INTO {MigrationTable} (version, name, applied_at) VALUES ($version, $name, $appliedAt);";
                insert.Parameters.AddWithValue("$version", migration.Version);
                insert.Parameters.AddWithValue("$name", migration.Name);
                insert.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                insert.ExecuteNonQuery();
            }

            // user_version is an int literal (migration.Version), never user input — safe to interpolate.
            Execute(connection, $"PRAGMA user_version = {migration.Version};", transaction);

            transaction.Commit();
        }
    }

    private static IEnumerable<Migration> DiscoverMigrations()
    {
        Assembly assembly = typeof(SchemaManager).Assembly;
        const string marker = ".Schema.Migrations.";

        List<Migration> migrations = [];
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            int markerIndex = resource.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0 || !resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = resource[(markerIndex + marker.Length)..];   // e.g. "0001_init.sql"
            int underscore = fileName.IndexOf('_', StringComparison.Ordinal);
            string versionToken = underscore > 0 ? fileName[..underscore] : Path.GetFileNameWithoutExtension(fileName);
            if (!int.TryParse(versionToken, out int version))
                continue;

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            migrations.Add(new Migration(version, Path.GetFileNameWithoutExtension(fileName), reader.ReadToEnd()));
        }

        return migrations.OrderBy(m => m.Version);
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record Migration(int Version, string Name, string Sql);
}
