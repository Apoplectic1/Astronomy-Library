using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent;

/// <summary>
/// The authored intent store — a permanent, user-owned SQLite database of planning truth (targets,
/// desired counts, membership, policy, blessed plans). This type owns the open/close invariants;
/// the schema itself is stood up and evolved by <see cref="IntentMigrations"/> (migrated, never
/// rebuilt). The store path is always the caller's configuration — this library carries no default
/// location — and must be a local fixed path: the store is single-writer by system invariant, and
/// network placement is refused outright rather than coordinated around.
/// </summary>
public sealed class IntentStore : IDisposable
{
    private const int BusyTimeoutMs = 2000;

    private IntentStore(SqliteConnection connection, string databasePath)
    {
        Connection = connection;
        DatabasePath = databasePath;
    }

    /// <summary>The open connection. Owned by the store — do not dispose it directly; dispose the store.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>The store file's full path as opened.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Opens (creating if absent) the store at <paramref name="databasePath"/>: rejects non-local
    /// paths before any file is created, migrates the schema to the library's latest version, and
    /// configures the connection (foreign keys on, WAL, <c>synchronous=NORMAL</c>, short busy
    /// timeout). A busy or conflicting writer surfaces as a <see cref="SqliteException"/> once the
    /// busy window elapses — loudly, never a silent wait.
    /// </summary>
    /// <exception cref="IntentStoreException">
    /// The path is not local, the store is newer than this library, or a migration failed.
    /// </exception>
    public static IntentStore Open(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        RequireLocalPath(databasePath);

        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // No pooling: Dispose must actually close the file handle, or checkpoint-on-close
            // leaves a pooled handle open and the closed store is not one consistent file at rest.
            Pooling = false,
        }.ToString());

        connection.Open();
        try
        {
            Execute(connection, "PRAGMA foreign_keys = ON;");
            Execute(connection, $"PRAGMA busy_timeout = {BusyTimeoutMs};");
            Execute(connection, "PRAGMA journal_mode = WAL;");
            Execute(connection, "PRAGMA synchronous = NORMAL;");
            IntentMigrations.Migrate(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return new IntentStore(connection, databasePath);
    }

    /// <summary>
    /// Rejects UNC paths and paths on network drives before any file I/O. The store's one
    /// path-shaped rule is behavioral and local: where the file lives is the caller's choice, but
    /// it must be a local, fully qualified location — fail fast otherwise.
    /// </summary>
    private static void RequireLocalPath(string databasePath)
    {
        string fullPath = Path.GetFullPath(databasePath);

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new IntentStoreException(
                $"Intent store path '{databasePath}' is a UNC path. The store is local-only " +
                "(single-writer by invariant; nothing SQLite crosses the network) — use a local fixed-drive path.");
        }

        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            throw new IntentStoreException(
                $"Intent store path '{databasePath}' has no drive root; a fully qualified local path is required.");
        }

        DriveInfo drive = new(root);
        if (drive.DriveType == DriveType.Network)
        {
            throw new IntentStoreException(
                $"Intent store path '{databasePath}' resolves to network drive '{root}'. The store is local-only " +
                "(single-writer by invariant; nothing SQLite crosses the network) — use a local fixed-drive path.");
        }
    }

    /// <summary>
    /// Checkpoints the WAL in TRUNCATE mode and closes. A closed store is one consistent file —
    /// the <c>.db</c> alone contains every committed write — so file-level copy/sync of the closed
    /// store is safe.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Execute(Connection, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
        finally
        {
            Connection.Dispose();
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
