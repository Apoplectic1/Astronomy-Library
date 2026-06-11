using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// Hermetic: a temp sqlite with just a `target` table, mirroring TargetSchedulerWriterTests' setup pattern.
public sealed class TargetSchedulerEditorTests
{
    [Fact]
    public void SetByGuid_TogglesActive_AndVerifies()
    {
        string db = NewTargetDb(("g-1", 1, "Alpha"), ("g-2", 0, "Beta"));
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.HasRequiredColumns);
            Assert.False(editor.HasOpenSidecar);

            TargetEditResult result = editor.SetTargetActive("g-1", active: false);

            Assert.True(result.RowFound);
            Assert.Equal(1, result.OldActive);
            Assert.True(result.Verified);
            Assert.True(result.Succeeded);
            Assert.Equal(0, ReadActive(db, "g-1"));
            Assert.Equal(0, ReadActive(db, "g-2"));   // the other row is untouched
        }
        finally
        {
            TestSupport.Cleanup(db);
        }
    }

    [Fact]
    public void SetById_WhenKeyIsInteger_TargetsTheIdColumn()
    {
        // A TS target with no guid: the catalog provenance is the integer Id as a string, so the key is numeric.
        string db = NewTargetDb((null, 0, "NoGuid"));   // Id 1, guid NULL, active 0
        try
        {
            using TargetSchedulerEditor editor = new(db);
            TargetEditResult result = editor.SetTargetActive("1", active: true);

            Assert.True(result.RowFound);
            Assert.Equal(0, result.OldActive);
            Assert.True(result.Verified);
            Assert.Equal(1, ReadActiveById(db, 1));
        }
        finally
        {
            TestSupport.Cleanup(db);
        }
    }

    [Fact]
    public void UnknownKey_ReturnsRowNotFound_AndWritesNothing()
    {
        string db = NewTargetDb(("g-1", 1, "Alpha"));
        try
        {
            using TargetSchedulerEditor editor = new(db);
            TargetEditResult result = editor.SetTargetActive("does-not-exist", active: false);

            Assert.False(result.RowFound);
            Assert.False(result.Succeeded);
            Assert.Null(result.OldActive);
            Assert.Equal(1, ReadActive(db, "g-1"));   // unchanged
        }
        finally
        {
            TestSupport.Cleanup(db);
        }
    }

    [Fact]
    public void MissingActiveColumn_IsRefusedByGuard()
    {
        string db = TestSupport.NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText = "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT);";   // no `active`
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.False(editor.HasRequiredColumns);
        }
        finally
        {
            TestSupport.Cleanup(db);
        }
    }

    // ---- helpers ------------------------------------------------------------

    private static string NewTargetDb(params (string? Guid, int Active, string Name)[] rows)
    {
        string db = TestSupport.NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand create = setup.CreateCommand();
            create.CommandText =
                "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, name TEXT);";
            create.ExecuteNonQuery();

            foreach ((string? guid, int active, string name) in rows)
            {
                using SqliteCommand insert = setup.CreateCommand();
                insert.CommandText = "INSERT INTO target (guid, active, name) VALUES ($g, $a, $n);";
                insert.Parameters.AddWithValue("$g", (object?)guid ?? DBNull.Value);
                insert.Parameters.AddWithValue("$a", active);
                insert.Parameters.AddWithValue("$n", name);
                insert.ExecuteNonQuery();
            }
        }
        SqliteConnection.ClearAllPools();   // release the setup connection before the editor opens read-write
        return db;
    }

    private static int ReadActive(string db, string guid)
    {
        using SqliteConnection conn = new(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT active FROM target WHERE guid = $g;";
        cmd.Parameters.AddWithValue("$g", guid);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ReadActiveById(string db, long id)
    {
        using SqliteConnection conn = new(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT active FROM target WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
