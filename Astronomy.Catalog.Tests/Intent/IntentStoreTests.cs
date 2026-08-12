using Astronomy.Catalog.Intent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The store's open/close invariants: authored intent round-trips across close/reopen, non-local
// paths are refused before any file exists, a second writer fails loudly, and a closed store is
// one consistent file (checkpoint-on-close) whose schema carries no actuals surface.
public sealed class IntentStoreTests
{
    [Fact]
    public void AuthoredIntent_RoundTrips_AcrossCloseAndReopen()
    {
        string path = NewStorePath();
        Guid profileId = Guid.NewGuid();

        using (IntentStore store = IntentStore.Open(path))
        {
            Exec(store.Connection,
                "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, 'Rig A', NULL, 1700000000);",
                ("$id", GuidBlob.ToBlob(profileId)));
        }

        using (IntentStore store = IntentStore.Open(path))
        {
            using SqliteCommand cmd = store.Connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM profile WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", GuidBlob.ToBlob(profileId));
            Assert.Equal("Rig A", cmd.ExecuteScalar());
        }
    }

    [Fact]
    public void UncPath_IsRefused_NamingPathAndRule()
    {
        IntentStoreException ex = Assert.Throws<IntentStoreException>(
            () => IntentStore.Open(@"\\some-server\share\Catalog.db"));

        Assert.Contains(@"\\some-server\share\Catalog.db", ex.Message);
        Assert.Contains("local-only", ex.Message);
    }

    [Fact]
    public void SecondWriter_FailsLoudly_NoInterleave()
    {
        string path = NewStorePath();
        using IntentStore first = IntentStore.Open(path);
        using IntentStore second = IntentStore.Open(path);

        using SqliteTransaction hold = first.Connection.BeginTransaction();
        Exec(first.Connection, hold,
            "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, 'holder', NULL, 1);",
            ("$id", GuidBlob.ToBlob(Guid.NewGuid())));

        SqliteException ex = Assert.Throws<SqliteException>(() => Exec(second.Connection, null,
            "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, 'intruder', NULL, 2);",
            ("$id", GuidBlob.ToBlob(Guid.NewGuid()))));
        Assert.Equal(5, ex.SqliteErrorCode);   // SQLITE_BUSY — refused, not interleaved

        hold.Commit();
        Assert.Equal(1L, Scalar(first.Connection, "SELECT count(*) FROM profile;"));
    }

    [Fact]
    public void ClosedStore_IsOneConsistentFile()
    {
        string path = NewStorePath();
        Guid profileId = Guid.NewGuid();
        using (IntentStore store = IntentStore.Open(path))
        {
            Exec(store.Connection,
                "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, 'Rig A', NULL, 1700000000);",
                ("$id", GuidBlob.ToBlob(profileId)));
        }

        // Checkpoint-on-close leaves no pending WAL content (the -wal is deleted or truncated to 0).
        string wal = path + "-wal";
        Assert.True(!File.Exists(wal) || new FileInfo(wal).Length == 0);

        // The .db alone, copied elsewhere, opens as a complete database with every committed write.
        string copy = Path.Combine(Path.GetDirectoryName(path)!, "copy.db");
        File.Copy(path, copy);
        using SqliteConnection reader = new(new SqliteConnectionStringBuilder
        { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        reader.Open();
        Assert.Equal(1L, Scalar(reader, "SELECT count(*) FROM profile;"));
    }

    [Fact]
    public void Schema_HasNoActualsSurface()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());

        // No acquisition-history or scan-output tables — progress truth is the disk library, scanned fresh.
        Assert.Equal(0L, Scalar(store.Connection,
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN " +
            "('acquired_image', 'flat_history', 'inventory_filter', 'acquiredimage', 'imagedata', 'flathistory');"));

        // exposure_plan holds desired counts only — no acquired/accepted projections of actuals.
        Assert.Equal(0L, Scalar(store.Connection,
            "SELECT count(*) FROM pragma_table_info('exposure_plan') WHERE name IN ('acquired_count', 'accepted_count');"));
    }

    private static string NewStorePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "intent.db");
    }

    private static void Exec(SqliteConnection db, string sql, params (string Name, object Value)[] parameters) =>
        Exec(db, null, sql, parameters);

    private static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ((string name, object value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
