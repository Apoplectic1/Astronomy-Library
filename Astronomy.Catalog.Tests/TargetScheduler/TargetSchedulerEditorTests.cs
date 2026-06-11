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

    [Fact]
    public void SetTargetField_Priority_UpdatesAndVerifies()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            FieldEditResult r = editor.SetTargetField("tg-1", "priority", 2);
            Assert.True(r.Succeeded);
            Assert.Equal("-1", r.OldValue);
            Assert.Equal(2L, ReadScalar(db, "SELECT priority FROM target WHERE guid='tg-1'"));
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void SetPlanField_Desired_UpdatesAndVerifies()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.SetPlanField("ep-1", "desired", 140).Succeeded);
            Assert.Equal(140L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE guid='ep-1'"));
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void SetProjectField_NullableDouble_RoundTrips()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.SetProjectField("pr-1", "minimumaltitude", 45.5).Succeeded);
            Assert.Equal(45.5, Convert.ToDouble(ReadScalar(db, "SELECT minimumaltitude FROM project WHERE guid='pr-1'")));
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void SetField_RejectsNonWhitelistedColumn()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.Throws<ArgumentException>(() => editor.SetTargetField("tg-1", "name", "hax"));
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void SetField_Generic_RoutesByTable()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.SetField(TsTable.ExposureTemplate, "tpl-1", "gain", 200).Succeeded);
            Assert.Equal(200L, ReadScalar(db, "SELECT gain FROM exposuretemplate WHERE guid='tpl-1'"));
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void ReadField_ReturnsCurrentValue_AndFalseForUnknownKey()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            (bool found, object? value) = editor.ReadField(TsTable.Project, "pr-1", "minimumaltitude");
            Assert.True(found);
            Assert.Equal(30.0, Convert.ToDouble(value));
            Assert.False(editor.ReadField(TsTable.Project, "nope", "minimumaltitude").Found);
        }
        finally { TestSupport.Cleanup(db); }
    }

    [Fact]
    public void IsFieldAvailable_TrueForPresent_FalseForMissingOrNonEditable()
    {
        string db = NewFullDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.IsFieldAvailable(TsTable.Project, "minimumaltitude"));        // editable + present
            Assert.False(editor.IsFieldAvailable(TsTable.Project, "filterswitchfrequency")); // editable but absent here
            Assert.False(editor.IsFieldAvailable(TsTable.Target, "name"));                   // present but not editable
        }
        finally { TestSupport.Cleanup(db); }
    }

    // ---- helpers ------------------------------------------------------------

    // A db with target/exposureplan/project rows carrying the editable columns the field setters touch.
    private static string NewFullDb()
    {
        string db = TestSupport.NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, priority INTEGER, rotation REAL, roi REAL, name TEXT);" +
                "INSERT INTO target (guid, active, priority, name) VALUES ('tg-1', 1, -1, 'Alpha');" +
                "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, desired INTEGER, acquired INTEGER, accepted INTEGER);" +
                "INSERT INTO exposureplan (guid, desired, acquired, accepted) VALUES ('ep-1', 10, 0, 0);" +
                "CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, state INTEGER, minimumaltitude REAL, ditherevery INTEGER);" +
                "INSERT INTO project (guid, state, minimumaltitude, ditherevery) VALUES ('pr-1', 1, 30.0, 0);" +
                "CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT, name TEXT, filtername TEXT, gain INTEGER, offset INTEGER, bin INTEGER, readoutmode INTEGER, defaultexposure REAL);" +
                "INSERT INTO exposuretemplate (guid, name, filtername, gain, offset, bin, defaultexposure) VALUES ('tpl-1', 'Ha 300', 'Ha', 100, 10, 1, 300.0);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return db;
    }

    private static object ReadScalar(string db, string sql)
    {
        using SqliteConnection conn = new(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()!;
    }

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
