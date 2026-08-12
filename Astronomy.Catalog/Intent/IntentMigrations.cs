using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent;

/// <summary>
/// The intent store's migration framework. Scripts are embedded resources under
/// <c>Intent/Migrations/NNNN_name.sql</c>, applied in version order, each inside its own
/// transaction that also records a <c>schema_migration</c> row and advances
/// <c>PRAGMA user_version</c> (the fast check — always equal to the highest logged version).
/// A store newer than <see cref="LatestVersion"/> aborts before any write; a failed script rolls
/// back leaving the prior version fully intact.
/// </summary>
public static partial class IntentMigrations
{
    [GeneratedRegex(@"\.Intent\.Migrations\.(?<version>\d{4})_(?<name>[A-Za-z0-9_]+)\.sql$")]
    private static partial Regex ScriptResourceRegex();

    /// <summary>One migration script: its version, name, and SQL (read lazily for embedded scripts).</summary>
    internal sealed record MigrationScript(int Version, string Name, Func<string> ReadSql);

    private static readonly IReadOnlyList<MigrationScript> EmbeddedScripts = DiscoverScripts();

    /// <summary>The highest migration version this library ships (the schema version a fresh store ends at).</summary>
    public static int LatestVersion => EmbeddedScripts[^1].Version;

    private static List<MigrationScript> DiscoverScripts()
    {
        Assembly assembly = typeof(IntentMigrations).Assembly;
        List<MigrationScript> scripts = [];
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            Match match = ScriptResourceRegex().Match(resource);
            if (!match.Success) continue;
            scripts.Add(new MigrationScript(
                int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture),
                match.Groups["name"].Value,
                () => ReadResourceSql(assembly, resource)));
        }

        scripts.Sort((a, b) => a.Version.CompareTo(b.Version));
        ValidateContiguous(scripts);
        return scripts;
    }

    private static void ValidateContiguous(IReadOnlyList<MigrationScript> scripts)
    {
        if (scripts.Count == 0)
            throw new InvalidOperationException("No intent-store migration scripts found (Intent/Migrations/NNNN_name.sql).");
        for (int i = 0; i < scripts.Count; i++)
        {
            if (scripts[i].Version != i + 1)
                throw new InvalidOperationException(
                    $"Intent-store migration scripts are not contiguous from 0001: found version {scripts[i].Version:0000} at position {i + 1}.");
        }
    }

    /// <summary>
    /// Brings <paramref name="connection"/>'s store up to <see cref="LatestVersion"/>, applying any
    /// pending scripts in order (a fresh file is a store at version 0).
    /// </summary>
    /// <exception cref="IntentStoreException">
    /// The store is newer than this library, or a migration script failed (the failing transaction
    /// is rolled back; the store remains at the last completed version).
    /// </exception>
    public static void Migrate(SqliteConnection connection) => Apply(connection, EmbeddedScripts);

    /// <summary>Core apply loop, script-set injectable so tests can exercise ordering and rollback.</summary>
    internal static void Apply(SqliteConnection connection, IReadOnlyList<MigrationScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ValidateContiguous(scripts);

        int latest = scripts[^1].Version;
        long currentVersion = ReadUserVersion(connection);
        if (currentVersion > latest)
        {
            throw new IntentStoreException(
                $"Intent store schema version {currentVersion} is newer than this library's latest migration " +
                $"({latest}). Update the application before opening this store; no write was performed.");
        }

        if (currentVersion == latest)
            return;

        EnsureMigrationLog(connection);

        // Table-rebuild scripts (R10) drop and recreate tables that other tables reference — impossible
        // with foreign-key enforcement on, and PRAGMA foreign_keys is a no-op inside a transaction, so
        // the framework owns the posture, not the scripts (SQLite's documented rebuild procedure):
        // suspend enforcement around the apply loop and gate every script's commit on a whole-store
        // foreign_key_check — any violation rolls the script back, so net integrity is stronger than
        // per-statement enforcement, not weaker.
        Execute(connection, transaction: null, "PRAGMA foreign_keys = OFF;");
        try
        {
            foreach (MigrationScript script in scripts)
            {
                if (script.Version <= currentVersion) continue;

                using SqliteTransaction transaction = connection.BeginTransaction();
                try
                {
                    Execute(connection, transaction, script.ReadSql());

                    using (SqliteCommand log = connection.CreateCommand())
                    {
                        log.Transaction = transaction;
                        log.CommandText = "INSERT INTO schema_migration (version, name, applied_at) VALUES ($version, $name, unixepoch());";
                        log.Parameters.AddWithValue("$version", script.Version);
                        log.Parameters.AddWithValue("$name", script.Name);
                        log.ExecuteNonQuery();
                    }

                    // PRAGMA user_version lives in the database header and participates in the transaction.
                    Execute(connection, transaction, $"PRAGMA user_version = {script.Version};");
                    RequireForeignKeyIntegrity(connection, transaction);
                    transaction.Commit();
                }
                catch (Exception ex) when (ex is not IntentStoreException)
                {
                    transaction.Rollback();
                    throw new IntentStoreException(
                        $"Intent-store migration {script.Version:0000}_{script.Name} failed and was rolled back; " +
                        $"the store remains at schema version {script.Version - 1}. {ex.Message}", ex);
                }
            }
        }
        finally
        {
            Execute(connection, transaction: null, "PRAGMA foreign_keys = ON;");
        }
    }

    /// <summary>
    /// The pre-commit integrity gate: with enforcement suspended for the script, the whole store must
    /// still pass <c>PRAGMA foreign_key_check</c> before its transaction may commit.
    /// </summary>
    private static void RequireForeignKeyIntegrity(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = check.ExecuteReader();
        if (!reader.Read())
            return;

        string table = reader.GetString(0);
        string parent = reader.GetString(2);
        throw new InvalidOperationException(
            $"PRAGMA foreign_key_check failed: table '{table}' holds at least one reference to a missing '{parent}' row.");
    }

    /// <summary>Reads the store's current schema version (<c>PRAGMA user_version</c>).</summary>
    public static long ReadUserVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// The framework's own bootstrap — the one piece of DDL outside the scripts, so version 0001 can
    /// record itself like every later migration.
    /// </summary>
    private static void EnsureMigrationLog(SqliteConnection connection) => Execute(connection, transaction: null,
        """
        CREATE TABLE IF NOT EXISTS schema_migration (
            version    INTEGER NOT NULL PRIMARY KEY,
            name       TEXT NOT NULL,
            applied_at INTEGER NOT NULL
        ) WITHOUT ROWID;
        """);

    private static string ReadResourceSql(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
