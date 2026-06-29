using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the Target Scheduler DB-access surface — CONSUMERS.md
/// "Semantic assumptions" #8 (open-in-ctor lifecycle) and #9 (column-presence gate).
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

    // ---- helpers (mirrored from Astronomy.Catalog.Tests) --------------------

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
