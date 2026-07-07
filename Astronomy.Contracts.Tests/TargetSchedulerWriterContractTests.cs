using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md "Semantic assumptions" #23 — write-back is UPDATE-ONLY: existing
/// <c>exposureplan</c> rows' <c>acquired</c>/<c>accepted</c> only (never inserts/deletes rows, never
/// alters the journal mode); <c>desired</c> ratchets to <c>max(old, new)</c> — raised, never lowered;
/// and a zero disk count is a REAL WRITE, not a skip (an unmet spec is recorded as 0, not left stale).
/// A consumer's push-as-replay presents the <c>Execute(apply:false)</c> diff and trusts these semantics
/// when the user confirms. Fixture pattern mirrors Astronomy.Catalog.Tests.
/// </summary>
public sealed class TargetSchedulerWriterContractTests
{
    [Fact]
    public void Execute_NoApply_ReturnsTheDiffAndTouchesNothing()
    {
        string db = NewPlanDb();
        try
        {
            using TargetSchedulerWriter writer = new(db);
            WriteBackResult result = writer.Execute(PlanFor((1, 7), (2, 8), (3, 0)), apply: false);

            Assert.False(result.Applied);
            Assert.Equal(3, result.Changes.Count);

            // DB untouched: original counts survive a diff-only pass.
            Assert.Equal(5L, ReadScalar(db, "SELECT acquired FROM exposureplan WHERE Id=1"));
            Assert.Equal(3L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE Id=2"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void Execute_Apply_UpdatesCountsRowCountUnchanged_NoVerifyFailures()
    {
        string db = NewPlanDb();
        try
        {
            long rowsBefore;
            string journalBefore;
            using (TargetSchedulerWriter writer = new(db))
            {
                rowsBefore = (long)ReadScalar(db, "SELECT COUNT(*) FROM exposureplan");
                journalBefore = (string)ReadScalar(db, "PRAGMA journal_mode");

                WriteBackResult result = writer.Execute(PlanFor((1, 7), (2, 8)), apply: true);
                Assert.True(result.Applied);
                Assert.Empty(result.VerifyFailures);
            }

            // Update-only: same rows, new counts — acquired AND accepted both go to the disk count.
            Assert.Equal(rowsBefore, ReadScalar(db, "SELECT COUNT(*) FROM exposureplan"));
            Assert.Equal(7L, ReadScalar(db, "SELECT acquired FROM exposureplan WHERE Id=1"));
            Assert.Equal(7L, ReadScalar(db, "SELECT accepted FROM exposureplan WHERE Id=1"));
            Assert.Equal(8L, ReadScalar(db, "SELECT acquired FROM exposureplan WHERE Id=2"));

            // The journal mode is TS's own (rollback journal); the writer must not have switched it.
            Assert.Equal(journalBefore, (string)ReadScalar(db, "PRAGMA journal_mode"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void Execute_Apply_DesiredRatchetsUpOnly()
    {
        string db = NewPlanDb();
        try
        {
            using (TargetSchedulerWriter writer = new(db))
            {
                WriteBackResult result = writer.Execute(PlanFor((1, 7), (2, 8)), apply: true);

                // Id=1: desired 10, count 7 → goal stays 10 (never lowered) and the diff says so.
                WriteBackChange c1 = result.Changes.Single(c => c.TsExposurePlanId == 1);
                Assert.Equal(10, c1.NewDesired);
                Assert.False(c1.RaisesDesired);

                // Id=2: desired 3, count 8 → goal ratchets to 8 so it is never below what was kept.
                WriteBackChange c2 = result.Changes.Single(c => c.TsExposurePlanId == 2);
                Assert.Equal(8, c2.NewDesired);
                Assert.True(c2.RaisesDesired);
            }

            Assert.Equal(10L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE Id=1"));
            Assert.Equal(8L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE Id=2"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void Execute_Apply_ZeroDiskCount_IsARealWrite()
    {
        string db = NewPlanDb();
        try
        {
            using (TargetSchedulerWriter writer = new(db))
            {
                // Id=1 currently has acquired=5/accepted=4: a plan whose spec no frames meet must be
                // written DOWN to 0/0 — skipping it would leave stale counts posing as progress.
                WriteBackResult result = writer.Execute(PlanFor((1, 0)), apply: true);
                Assert.Empty(result.VerifyFailures);
                Assert.True(result.Changes.Single().IsDecrease);
            }

            Assert.Equal(0L, ReadScalar(db, "SELECT acquired FROM exposureplan WHERE Id=1"));
            Assert.Equal(0L, ReadScalar(db, "SELECT accepted FROM exposureplan WHERE Id=1"));
            Assert.Equal(10L, ReadScalar(db, "SELECT desired FROM exposureplan WHERE Id=1"));   // goal survives
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void Execute_Apply_UnknownPlanId_NeverInsertsARow_ReportsVerifyFailure()
    {
        string db = NewPlanDb();
        try
        {
            long rowsBefore = (long)ReadScalar(db, "SELECT COUNT(*) FROM exposureplan");
            using (TargetSchedulerWriter writer = new(db))
            {
                // Update-only means a vanished row (deleted between plan and apply) is a loud verify
                // failure, never a quiet INSERT.
                WriteBackResult result = writer.Execute(PlanFor((99, 5)), apply: true);
                WriteBackVerifyFailure failure = Assert.Single(result.VerifyFailures);
                Assert.Equal(99L, failure.TsExposurePlanId);
            }

            Assert.Equal(rowsBefore, ReadScalar(db, "SELECT COUNT(*) FROM exposureplan"));
        }
        finally
        {
            Cleanup(db);
        }
    }

    // ---- fixture -------------------------------------------------------------

    // Id=1: acquired 5 / accepted 4 / desired 10 · Id=2: 0/0/3 · Id=3: 2/2/6.
    private static string NewPlanDb()
    {
        string db = Path.Combine(Path.GetTempPath(), $"ts_writer_{Guid.NewGuid():N}.db");
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, acquired INTEGER, accepted INTEGER, desired INTEGER);" +
                "INSERT INTO exposureplan (Id, acquired, accepted, desired) VALUES (1, 5, 4, 10);" +
                "INSERT INTO exposureplan (Id, acquired, accepted, desired) VALUES (2, 0, 0, 3);" +
                "INSERT INTO exposureplan (Id, acquired, accepted, desired) VALUES (3, 2, 2, 6);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();   // release the setup connection before the writer opens read-write
        return db;
    }

    private static WriteBackPlan PlanFor(params (long PlanId, int DiskCount)[] writes) => new(
        Writes: [.. writes.Select(w => new PlannedWrite(
            TsExposurePlanId: w.PlanId, TargetId: Guid.NewGuid(), TargetName: "T", Filter: "Ha",
            Purpose: FilterPurpose.Light, PlanSeconds: 300, DiskCount: w.DiskCount))],
        Manual: [],
        NeedsReconciliation: [],
        IgnoredMissing: 0);

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
