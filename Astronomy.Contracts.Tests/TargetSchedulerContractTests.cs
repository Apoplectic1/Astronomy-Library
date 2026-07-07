using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the Target Scheduler DB-access surface — CONSUMERS.md
/// "Semantic assumptions" #8 (open-in-ctor lifecycle), #9 (column-presence gate),
/// #10 (write-back key = guid-or-Id, disambiguated by <c>long.TryParse</c>), and
/// #20 (<c>ReadPlanEffectiveExposure</c> resolves the sentinel through the template).
/// DB setup mirrors Astronomy.Catalog.Tests (temp SQLite via Microsoft.Data.Sqlite,
/// ClearAllPools after the setup connection so the reader/editor opens cleanly).
/// </summary>
public sealed class TargetSchedulerContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #8:
    //   "TargetSchedulerReader/Editor open the DB in their ctor — file must exist."
    // The connection is opened (Open()) inside the constructor, so a bogus/non-existent
    // path fails AT CONSTRUCTION, not lazily on first read. TSM relies on construction
    // being the validation point (it wraps `new` in its open-DB error handling). Reader
    // opens Mode=ReadOnly and Editor opens Mode=ReadWrite (never *Create); neither
    // creates the file, so both throw SqliteException for a missing path.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Reader_Ctor_ThrowsForMissingDbPath()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"ts_no_such_db_{Guid.NewGuid():N}.sqlite");
        Assert.False(File.Exists(missing));

        // Throws in the ctor (the Open() call), proving open-in-ctor lifecycle.
        Assert.Throws<SqliteException>(() => new TargetSchedulerReader(missing));
    }

    [Fact]
    public void Editor_Ctor_ThrowsForMissingDbPath()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"ts_no_such_db_{Guid.NewGuid():N}.sqlite");
        Assert.False(File.Exists(missing));

        Assert.Throws<SqliteException>(() => new TargetSchedulerEditor(missing));
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #9:
    //   "TargetSchedulerEditor.HasRequiredColumns (Id,guid,active) gates ALL writes
    //    (else RefusalReason.SchemaIncompatible)."
    // When the `target` table lacks the required columns, EVERY guarded write must be
    // refused with RefusalReason.SchemaIncompatible BEFORE any UPDATE runs, and the DB
    // must be left untouched. SchemaIncompatible is checked first in TrySetField, so it
    // wins even over a perfectly-valid editable field/value (here `priority`, which
    // succeeds on a complete schema — see Astronomy.Catalog.Tests).
    // ---------------------------------------------------------------------------

    [Fact]
    public void TrySetField_TargetMissingRequiredColumns_RefusesSchemaIncompatible_LeavesDbUntouched()
    {
        // A `target` table WITHOUT the required Id/guid/active triad. It does carry an
        // editable column (`priority`) so the refusal can only come from the schema gate,
        // not from a missing/non-editable column — and we can prove the value is untouched.
        string db = NewTargetDbWithoutRequiredColumns();
        try
        {
            using TargetSchedulerEditor editor = new(db);

            // The contract gate itself: the required-column triad is absent.
            Assert.False(editor.HasRequiredColumns);

            (FieldEditResult? result, RefusalReason refusal) =
                editor.TrySetField(TsTable.Target, "tg-1", "priority", 5);

            Assert.Equal(RefusalReason.SchemaIncompatible, refusal);
            Assert.Null(result);                              // no edit result at all

            // DB left untouched: priority is still its original value.
            Assert.Equal(-1L, ReadScalar(db, "SELECT priority FROM target WHERE guid='tg-1'"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #10:
    //   "Editor write-back key = ImportedFromTsGuid (GUID string *or* TS int Id as
    //    decimal string; disambiguated by long.TryParse)."
    // The key form is self-describing: a string that parses as a long selects BY Id —
    // even when some other row's guid happens to be that same digit string. TSM stores
    // the TS guid when the target has one, else the integer Id as text; a guid never
    // parses as a long, so the two key spaces can only collide if a guid is all digits,
    // and then Id wins by contract.
    // ---------------------------------------------------------------------------

    [Fact]
    public void EditKey_NumericString_SelectsById_EvenWhenAGuidMatchesTheDigits()
    {
        // Id=1 carries the pathological guid '7'; Id=7 carries a normal guid. Key "7" must hit Id=7.
        string db = NewDbWithRows(
            "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, name TEXT);" +
            "INSERT INTO target (Id, guid, active, name) VALUES (1, '7', 0, 'DigitGuid');" +
            "INSERT INTO target (Id, guid, active, name) VALUES (7, 'g-7', 0, 'RealSeven');");
        try
        {
            using TargetSchedulerEditor editor = new(db);
            TargetEditResult result = editor.SetTargetActive("7", active: true);

            Assert.True(result.Succeeded);
            Assert.Equal(1L, ReadScalar(db, "SELECT active FROM target WHERE Id = 7"));   // selected by Id
            Assert.Equal(0L, ReadScalar(db, "SELECT active FROM target WHERE Id = 1"));   // digit-guid row untouched
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void EditKey_GuidString_SelectsByGuid()
    {
        string db = NewDbWithRows(
            "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL, name TEXT);" +
            "INSERT INTO target (Id, guid, active, name) VALUES (1, 'g-1', 0, 'Alpha');");
        try
        {
            using TargetSchedulerEditor editor = new(db);
            Assert.True(editor.SetTargetActive("g-1", active: true).Succeeded);
            Assert.Equal(1L, ReadScalar(db, "SELECT active FROM target WHERE guid = 'g-1'"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #20:
    //   "ReadPlanEffectiveExposure resolves the sentinel THROUGH THE TEMPLATE (the #19
    //    rule as SQL) and returns Found=false for an unknown key or a missing template
    //    row." TSM seeds its exposure editor from the resolved value, never the raw
    //    sentinel — a raw read would surface -1 to the user.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ReadPlanEffectiveExposure_SentinelDefersToTemplate_OverrideWins()
    {
        string db = NewEffectiveExposureDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);

            // ep-sentinel holds -1 → the template's 300.
            (bool found, double? value) = editor.ReadPlanEffectiveExposure("ep-sentinel");
            Assert.True(found);
            Assert.Equal(300.0, value);

            // ep-own holds a positive override → its own value, template ignored.
            (found, value) = editor.ReadPlanEffectiveExposure("ep-own");
            Assert.True(found);
            Assert.Equal(120.0, value);
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void ReadPlanEffectiveExposure_UnknownKeyOrMissingTemplate_NotFound()
    {
        string db = NewEffectiveExposureDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);

            Assert.False(editor.ReadPlanEffectiveExposure("no-such-plan").Found);

            // ep-orphan's exposureTemplateId points at no template row: the INNER JOIN yields
            // nothing, so the plan is NOT FOUND rather than half-resolved.
            Assert.False(editor.ReadPlanEffectiveExposure("ep-orphan").Found);
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void ReadPlanEffectiveExposure_ZeroExposure_DefersToTemplate()
    {
        // PINS CURRENT BEHAVIOR — known divergence flagged in CONSUMERS.md #19: this SQL's override
        // test is `exposure > 0` (0 defers to the template), while EffectiveExposure.Seconds'
        // raw-TS overload uses `< 0` as the sentinel test (0 is a literal zero-second exposure) —
        // see EffectiveExposureContractTests. The two disagree at exactly 0; adjudicate against
        // TS's own semantics before relying on either at 0.
        string db = NewEffectiveExposureDb();
        try
        {
            using TargetSchedulerEditor editor = new(db);
            (bool found, double? value) = editor.ReadPlanEffectiveExposure("ep-zero");
            Assert.True(found);
            Assert.Equal(300.0, value);
        }
        finally
        {
            Cleanup(db);
        }
    }

    // ---- helpers (mirrored from Astronomy.Catalog.Tests) --------------------

    // exposureplan + exposuretemplate rows exercising every #20 resolution branch. The editor's
    // ctor also reflects target/project (PRAGMA on a missing table yields no rows — fine).
    private static string NewEffectiveExposureDb() => NewDbWithRows(
        "CREATE TABLE target (Id INTEGER PRIMARY KEY, guid TEXT, active INTEGER NOT NULL);" +
        "CREATE TABLE exposuretemplate (Id INTEGER PRIMARY KEY, guid TEXT, defaultexposure REAL);" +
        "INSERT INTO exposuretemplate (Id, guid, defaultexposure) VALUES (1, 'tpl-1', 300.0);" +
        "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, guid TEXT, exposure REAL, exposureTemplateId INTEGER);" +
        "INSERT INTO exposureplan (guid, exposure, exposureTemplateId) VALUES ('ep-sentinel', -1, 1);" +
        "INSERT INTO exposureplan (guid, exposure, exposureTemplateId) VALUES ('ep-own', 120.0, 1);" +
        "INSERT INTO exposureplan (guid, exposure, exposureTemplateId) VALUES ('ep-zero', 0.0, 1);" +
        "INSERT INTO exposureplan (guid, exposure, exposureTemplateId) VALUES ('ep-orphan', -1, 99);");

    private static string NewDbWithRows(string setupSql)
    {
        string db = NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText = setupSql;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();   // release the setup connection before the editor opens read-write
        return db;
    }


    private static string NewTargetDbWithoutRequiredColumns()
    {
        string db = NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            // Has an editable column (priority) but NOT the required `active` (nor a typed Id/guid triad).
            cmd.CommandText =
                "CREATE TABLE target (rowid_alias INTEGER PRIMARY KEY, guid TEXT, priority INTEGER, name TEXT);" +
                "INSERT INTO target (guid, priority, name) VALUES ('tg-1', -1, 'Alpha');";
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

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"ts_contract_{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best-effort cleanup of throwaway test artifacts */ }
        }
    }
}
