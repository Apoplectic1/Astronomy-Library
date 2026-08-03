using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The guarded insert contract over real temp dbs: guard order before any write, payload round-trip with
// read-back verify, guid parent resolution (including a parent inserted earlier in the same batch), the
// plan-insert cadence clear, override-order refusal, batch atomicity, and payload contract throws.
public sealed class TargetSchedulerEditorInsertTests
{
    [Fact]
    public void OpenSidecar_RefusesBeforeAnyWrite()
    {
        string db = NewDb();
        File.WriteAllText(db + "-wal", "");
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([PlanInsert("ep-new", targetId: 1L)]);

        Assert.Null(outcome);
        Assert.Equal(RefusalReason.OpenSidecar, refusal);
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM exposureplan WHERE guid = 'ep-new'"));
    }

    [Fact]
    public void UnknownPayloadColumn_RefusesColumnAbsent()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([new TsRowInsert(
            TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-new", ["targetid"] = 1L, ["exposureTemplateId"] = 21L, ["nosuchcolumn"] = 1 })]);

        Assert.Null(outcome);
        Assert.Equal(RefusalReason.ColumnAbsent, refusal);
    }

    [Fact]
    public void PlanInsert_VerifiedRoundTrip_AndCadenceCleared()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([new TsRowInsert(
            TsTable.ExposurePlan, new Dictionary<string, object?>
            {
                ["guid"] = "ep-new", ["targetid"] = 1L, ["exposureTemplateId"] = 21L,
                ["enabled"] = 1, ["desired"] = 42, ["acquired"] = 42, ["accepted"] = 42, ["exposure"] = -1,
            })]);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(outcome!.Applied);
        RowInsertResult row = Assert.Single(outcome.Rows);
        Assert.True(row.Succeeded);
        Assert.True(row.RowId > 0);
        Assert.Equal(42L, Scalar(db, $"SELECT desired FROM exposureplan WHERE Id = {row.RowId}"));
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));   // cleared
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 2"));   // untouched
    }

    [Fact]
    public void ParentByGuid_ResolvesToIntegerId()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, _) = editor.TryInsertRows([new TsRowInsert(
            TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-new", ["targetid"] = "t-1", ["exposureTemplateId"] = "et-21", ["desired"] = 5 })]);

        Assert.True(outcome!.Rows[0].Succeeded);
        Assert.Equal(1L, Scalar(db, "SELECT targetid FROM exposureplan WHERE guid = 'ep-new'"));
        Assert.Equal(21L, Scalar(db, "SELECT exposureTemplateId FROM exposureplan WHERE guid = 'ep-new'"));
    }

    [Fact]
    public void Batch_TargetThenPlan_PlanResolvesTheNewTargetsGuid()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows(
        [
            new TsRowInsert(TsTable.Target, new Dictionary<string, object?>
            { ["guid"] = "t-new", ["projectid"] = "p-101", ["name"] = "Sh2-119", ["active"] = 1 }),
            new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-new", ["targetid"] = "t-new", ["exposureTemplateId"] = 21L, ["desired"] = 7 }),
        ]);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(outcome!.Applied);
        Assert.All(outcome.Rows, r => Assert.True(r.Succeeded));
        long targetId = (long)Scalar(db, "SELECT Id FROM target WHERE guid = 't-new'")!;
        Assert.Equal(targetId, Scalar(db, "SELECT targetid FROM exposureplan WHERE guid = 'ep-new'"));
        Assert.Equal(101L, Scalar(db, "SELECT projectid FROM target WHERE guid = 't-new'"));
    }

    [Fact]
    public void UnresolvedParent_RollsBackTheWholeBatch()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows(
        [
            new TsRowInsert(TsTable.Target, new Dictionary<string, object?>
            { ["guid"] = "t-new", ["projectid"] = "p-101", ["name"] = "A", ["active"] = 1 }),
            new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-new", ["targetid"] = "no-such-guid", ["exposureTemplateId"] = 21L }),
        ]);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.False(outcome!.Applied);
        Assert.Equal("targetid", outcome.Rows[1].UnresolvedParentColumn);
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM target WHERE guid = 't-new'"));        // rolled back
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM exposureplan WHERE guid = 'ep-new'"));
    }

    [Fact]
    public void OeoTarget_RefusesPlanInsert_NothingApplied()
    {
        string db = NewDb(oeoOnTarget1: true);
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([PlanInsert("ep-new", targetId: 1L)]);

        Assert.Null(outcome);
        Assert.Equal(RefusalReason.HasOverrideOrder, refusal);
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM exposureplan WHERE guid = 'ep-new'"));
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));   // intact
        Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid = 1"));
    }

    [Fact]
    public void TargetInsert_ClearsNothing_NeverOeoRefused()
    {
        string db = NewDb(oeoOnTarget1: true);
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows([new TsRowInsert(
            TsTable.Target, new Dictionary<string, object?>
            { ["guid"] = "t-new", ["projectid"] = 101L, ["name"] = "B", ["active"] = 1 })]);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(outcome!.Rows[0].Succeeded);
        Assert.Equal(5L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem"));   // no clear anywhere
    }

    [Fact]
    public void FailedInsert_AppliesNothing()
    {
        string db = NewDb();
        Exec(db, "CREATE TRIGGER boom BEFORE DELETE ON filtercadenceitem BEGIN SELECT RAISE(ABORT, 'boom'); END;");
        using (TargetSchedulerEditor editor = new(db))
        {
            Assert.ThrowsAny<SqliteException>(() => editor.TryInsertRows([PlanInsert("ep-new", targetId: 1L)]));
        }
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM exposureplan WHERE guid = 'ep-new'"));
        Assert.Equal(2L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));   // intact
    }

    [Theory]
    [InlineData("Id")]        // db mints Id
    [InlineData("guid")]      // guid is the row's cross-copy name
    [InlineData("targetid")]  // a plan without a parent reference is a caller bug
    public void PayloadContractViolations_Throw(string breakWhich)
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        Dictionary<string, object?> payload = new()
        { ["guid"] = "ep-new", ["targetid"] = 1L, ["exposureTemplateId"] = 21L };
        if (breakWhich == "Id") payload["Id"] = 999L; else payload.Remove(breakWhich);

        Assert.Throws<ArgumentException>(() => editor.TryInsertRows([new TsRowInsert(TsTable.ExposurePlan, payload)]));
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM exposureplan WHERE guid = 'ep-new'"));
    }

    [Fact]
    public void NonInsertableTable_Throws()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        Assert.Throws<ArgumentException>(() => editor.TryInsertRows([new TsRowInsert(
            TsTable.Project, new Dictionary<string, object?> { ["guid"] = "p-new" })]));
    }

    [Fact]
    public void Batch_TemplateThenPlan_PlanResolvesTheNewTemplatesGuid()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        (InsertOutcome? outcome, RefusalReason refusal) = editor.TryInsertRows(
        [
            new TsRowInsert(TsTable.ExposureTemplate, new Dictionary<string, object?>
            { ["guid"] = "et-new", ["profileId"] = "prof-1", ["name"] = "Stars B g53", ["filtername"] = "B" }),
            new TsRowInsert(TsTable.ExposurePlan, new Dictionary<string, object?>
            { ["guid"] = "ep-new", ["targetid"] = 1L, ["exposureTemplateId"] = "et-new", ["desired"] = 30 }),
        ]);

        Assert.Equal(RefusalReason.None, refusal);
        Assert.True(outcome!.Applied);
        Assert.All(outcome.Rows, r => Assert.True(r.Succeeded));
        long templateId = (long)Scalar(db, "SELECT Id FROM exposuretemplate WHERE guid = 'et-new'")!;
        Assert.Equal(templateId, Scalar(db, "SELECT exposureTemplateId FROM exposureplan WHERE guid = 'ep-new'"));
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 1"));  // plan insert clears
    }

    [Fact]
    public void TemplateInsert_MissingIdentityColumn_Throws()
    {
        string db = NewDb();
        using TargetSchedulerEditor editor = new(db);
        Assert.Throws<ArgumentException>(() => editor.TryInsertRows([new TsRowInsert(
            TsTable.ExposureTemplate, new Dictionary<string, object?>
            { ["guid"] = "et-new", ["profileId"] = "prof-1", ["name"] = "X" })]));   // no filtername
    }

    // ---- fixture: mirrors the cadence tests', plus template 21 for plan FKs -----------------------------

    private static TsRowInsert PlanInsert(string guid, long targetId) => new(
        TsTable.ExposurePlan, new Dictionary<string, object?>
        { ["guid"] = guid, ["targetid"] = targetId, ["exposureTemplateId"] = 21L, ["desired"] = 10 });

    private static string NewDb(bool oeoOnTarget1 = false)
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "ts.sqlite");
        Exec(db,
            "CREATE TABLE project (Id INTEGER PRIMARY KEY, guid TEXT, filterswitchfrequency INTEGER);" +
            "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, projectid INTEGER, name TEXT);" +
            "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, targetid INTEGER, exposureTemplateId INTEGER," +
            " enabled INTEGER, desired INTEGER, acquired INTEGER, accepted INTEGER, exposure REAL);" +
            "CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT, profileId TEXT, name TEXT, filtername TEXT);" +
            "CREATE TABLE filtercadenceitem (Id INTEGER PRIMARY KEY, targetid INTEGER, \"order\" INTEGER, next INTEGER, action INTEGER, referenceIdx INTEGER);" +
            "CREATE TABLE overrideexposureorderitem (Id INTEGER PRIMARY KEY, targetid INTEGER, \"order\" INTEGER, action INTEGER, referenceIdx INTEGER);" +
            "INSERT INTO project VALUES (101, 'p-101', 1), (102, 'p-102', 1);" +
            "INSERT INTO target VALUES (1, 't-1', 1, 101, 'A'), (2, 't-2', 1, 101, 'B'), (3, 't-3', 1, 102, 'C');" +
            "INSERT INTO exposuretemplate VALUES (21, 'et-21', 'prof-1', 'H900', 'H');" +
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
