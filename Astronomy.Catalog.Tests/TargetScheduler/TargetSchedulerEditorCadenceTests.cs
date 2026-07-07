using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The cadence-clear contract over real temp dbs: scoped transactional deletes, unchanged-value no-op,
// override-order refusal, and rollback atomicity (a trigger forces the DELETE to fail mid-transaction).
public sealed class TargetSchedulerEditorCadenceTests
{
    [Fact]
    public void DisablePlan_ClearsOnlyItsTargetsCadenceRows()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        FieldEditResult result = editor.SetField(TsTable.ExposurePlan, "11", "enabled", 0);

        Assert.True(result.Succeeded);
        Assert.Equal(0L, Scalar(db, "SELECT enabled FROM exposureplan WHERE Id = 11"));
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 2"));   // untouched
    }

    [Fact]
    public void FsfChange_ClearsWholeProject_SparesOtherProjects()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        FieldEditResult result = editor.SetField(TsTable.Project, "101", "filterswitchfrequency", 3);

        Assert.True(result.Succeeded);
        // Project 101 owns targets 1 + 2; project 102 owns target 3.
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid IN (1, 2)"));
        Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 3"));
    }

    [Fact]
    public void UnchangedValue_IsAVerifiedNoOp_CadenceRowsSurvive()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        FieldEditResult result = editor.SetField(TsTable.ExposurePlan, "11", "enabled", 1);   // already 1

        Assert.True(result.Succeeded);
        Assert.Equal("1", result.OldValue);
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));   // no clear
    }

    [Fact]
    public void OverrideOrder_RefusesTargetScope_EverythingIntact()
    {
        string db = NewDb(oeoOnTarget1: true);
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.ExposurePlan, "11", "enabled", 0);

        Assert.Null(result);
        Assert.Equal(RefusalReason.HasOverrideOrder, refusal);
        Assert.Equal(1L, Scalar(db, "SELECT enabled FROM exposureplan WHERE Id = 11"));
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));
        Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid = 1"));
    }

    [Fact]
    public void OverrideOrder_ProjectScopeProceeds_OeoUntouched()
    {
        string db = NewDb(oeoOnTarget1: true);
        using TargetSchedulerEditor editor = new(db);
        (FieldEditResult? result, RefusalReason refusal) = editor.TrySetField(TsTable.Project, "101", "filterswitchfrequency", 5);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(result!.Succeeded);
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid IN (1, 2)"));
        Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid = 1"));   // never deleted
    }

    [Fact]
    public void FailedClear_RollsBackTheUpdateToo()
    {
        string db = NewDb();
        Exec(db, "CREATE TRIGGER boom BEFORE DELETE ON filtercadenceitem BEGIN SELECT RAISE(ABORT, 'boom'); END;");
        using (TargetSchedulerEditor editor = new(db))
        {
            Assert.ThrowsAny<SqliteException>(() => editor.SetField(TsTable.ExposurePlan, "11", "enabled", 0));
        }
        Assert.Equal(1L, Scalar(db, "SELECT enabled FROM exposureplan WHERE Id = 11"));                     // UPDATE rolled back
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));          // rows intact
    }

    // ---- fixture: two projects, three targets, cadence rows on all; plan 11 on target 1 ------------------

    private static string NewDb(bool oeoOnTarget1 = false)
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "ts.sqlite");
        Exec(db,
            "CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, filterswitchfrequency INTEGER);" +
            "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, projectid INTEGER, name TEXT);" +
            "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, targetid INTEGER, enabled INTEGER, desired INTEGER);" +
            "CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT);" +
            "CREATE TABLE filtercadenceitem (Id INTEGER PRIMARY KEY, targetid INTEGER, \"order\" INTEGER, next INTEGER, action INTEGER, referenceIdx INTEGER);" +
            "CREATE TABLE overrideexposureorderitem (Id INTEGER PRIMARY KEY, targetid INTEGER, \"order\" INTEGER, action INTEGER, referenceIdx INTEGER);" +
            "INSERT INTO project VALUES (101, 'p-101', 1), (102, 'p-102', 1);" +
            "INSERT INTO target VALUES (1, 't-1', 1, 101, 'A'), (2, 't-2', 1, 101, 'B'), (3, 't-3', 1, 102, 'C');" +
            "INSERT INTO exposureplan VALUES (11, 'ep-11', 1, 1, 20);" +
            "INSERT INTO filtercadenceitem VALUES (1, 1, 1, 1, 0, 0), (2, 1, 2, 0, 0, 1)," +
            " (3, 2, 1, 1, 0, 0), (4, 2, 2, 0, 0, 1), (5, 3, 1, 1, 0, 0);" +
            (oeoOnTarget1 ? "INSERT INTO overrideexposureorderitem VALUES (1, 1, 1, 0, 0);" : ""));
        return db;
    }

    private static void Exec(string db, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(string db, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder
        { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
