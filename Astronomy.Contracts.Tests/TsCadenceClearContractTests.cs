using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the DB half of CONSUMERS.md "Semantic assumptions" #22 — a cadence-breaking edit
/// clears the scoped <c>filtercadenceitem</c> rows in the same transaction as the column write (TS restores
/// those rows verbatim and regenerates only from empty, so update-without-clear is the silent-wrong-rotation
/// state); an unchanged-value edit is a verified no-op with NO clear; and a target-scope clear refuses with
/// <c>RefusalReason.HasOverrideOrder</c> when the target has hand-authored override-order rows, leaving the
/// DB untouched. Fixture pattern mirrors Astronomy.Catalog.Tests.
/// </summary>
public sealed class TsCadenceClearContractTests
{
    [Fact]
    public void TargetScopeEdit_ClearsOnlyThatTargetsCadenceRows()
    {
        string db = NewCadenceDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(TsTable.ExposurePlan, "ep-1", "enabled", 0);

            Assert.Equal(RefusalReason.None, refusal);
            Assert.True(result!.Succeeded);
            Assert.Equal(0L, ReadScalar(db, "SELECT enabled FROM exposureplan WHERE guid='ep-1'"));

            // Target 1's cadence rows are gone; target 2's survive — the clear is scoped, not global.
            Assert.Equal(0L, ReadScalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid=1"));
            Assert.Equal(2L, ReadScalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid=2"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void UnchangedValueEdit_IsAVerifiedNoOp_WithNoClear()
    {
        string db = NewCadenceDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            // ep-1.enabled is already 1: same value ⇒ verified no-op — and crucially NO cadence clear
            // (mirrors TS, whose setters only mark a breaking change on !=).
            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(TsTable.ExposurePlan, "ep-1", "enabled", 1);

            Assert.Equal(RefusalReason.None, refusal);
            Assert.True(result!.Succeeded);
            Assert.Equal(2L, ReadScalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid=1"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void TargetScopeEdit_WithOverrideOrderRows_RefusesAndTouchesNothing()
    {
        string db = NewCadenceDb(overrideOrderForTarget1: true);
        try
        {
            using TargetSchedulerEditor editor = new(db);
            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(TsTable.ExposurePlan, "ep-1", "enabled", 0);

            // Honoring the edit would delete hand-authored override-order rows (index-coupled to the
            // plan set) — data loss, so the edit refuses instead.
            Assert.Equal(RefusalReason.HasOverrideOrder, refusal);
            Assert.Null(result);

            // Column, cadence rows, and override-order rows all untouched.
            Assert.Equal(1L, ReadScalar(db, "SELECT enabled FROM exposureplan WHERE guid='ep-1'"));
            Assert.Equal(2L, ReadScalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid=1"));
            Assert.Equal(1L, ReadScalar(db, "SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid=1"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void ProjectScopeEdit_ClearsEveryTargetOfTheProject_AndIgnoresOverrideOrders()
    {
        // Both targets belong to project 1; a filter-switch-frequency change resets the whole
        // project's rotations. Project scope mirrors TS's own path, which leaves override-order
        // rows untouched — so (unlike target scope) it does not refuse on their presence.
        string db = NewCadenceDb(overrideOrderForTarget1: true);
        try
        {
            using TargetSchedulerEditor editor = new(db);
            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(TsTable.Project, "pr-1", "filterswitchfrequency", 2);

            Assert.Equal(RefusalReason.None, refusal);
            Assert.True(result!.Succeeded);
            Assert.Equal(0L, ReadScalar(db, "SELECT COUNT(*) FROM filtercadenceitem"));
            Assert.Equal(1L, ReadScalar(db, "SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid=1"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    // ---- fixture -------------------------------------------------------------

    // Two targets under one project, each with cadence rows; ep-1 belongs to target 1.
    private static string NewCadenceDb(bool overrideOrderForTarget1 = false)
    {
        string db = Path.Combine(Path.GetTempPath(), $"ts_cadence_{Guid.NewGuid():N}.db");
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, filterswitchfrequency INTEGER);" +
                "INSERT INTO project (Id, guid, filterswitchfrequency) VALUES (1, 'pr-1', 0);" +
                "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, projectid INTEGER);" +
                "INSERT INTO target (Id, guid, active, projectid) VALUES (1, 'tg-1', 1, 1);" +
                "INSERT INTO target (Id, guid, active, projectid) VALUES (2, 'tg-2', 1, 1);" +
                "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, enabled INTEGER, targetid INTEGER);" +
                "INSERT INTO exposureplan (guid, enabled, targetid) VALUES ('ep-1', 1, 1);" +
                "CREATE TABLE filtercadenceitem (Id INTEGER PRIMARY KEY, targetid INTEGER);" +
                "INSERT INTO filtercadenceitem (targetid) VALUES (1); INSERT INTO filtercadenceitem (targetid) VALUES (1);" +
                "INSERT INTO filtercadenceitem (targetid) VALUES (2); INSERT INTO filtercadenceitem (targetid) VALUES (2);" +
                "CREATE TABLE overrideexposureorderitem (Id INTEGER PRIMARY KEY, targetid INTEGER);" +
                (overrideOrderForTarget1 ? "INSERT INTO overrideexposureorderitem (targetid) VALUES (1);" : "");
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();   // release the setup connection before the editor opens read-write
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

    private static void Cleanup(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best-effort cleanup of throwaway test artifacts */ }
        }
    }
}
