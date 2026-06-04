using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Tests;

/// <summary>Shared helpers for catalog tests: temp DB paths, cleanup (incl. WAL sidecars), scalar queries.</summary>
internal static class TestSupport
{
    /// <summary>A unique temp path for a throwaway Catalog.db.</summary>
    public static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"catalog_test_{Guid.NewGuid():N}.db");

    /// <summary>Current UNIX-seconds timestamp.</summary>
    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Deletes a temp DB and its WAL/SHM sidecars (best effort).</summary>
    public static void Cleanup(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // best-effort cleanup of throwaway test artifacts
            }
        }
    }

    /// <summary>Executes a scalar query.</summary>
    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>Executes a scalar query and returns it as <see cref="long"/>.</summary>
    public static long ScalarLong(SqliteConnection connection, string sql) => (long)(Scalar(connection, sql) ?? 0L);
}
