using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// Integration: build a real catalog (disk library + a COPY of the local TS working db), then exercise the writer
// against the copy. Gated on BOTH the TS db and the disk library being present (without the library every target
// is Planned-only and there is nothing to write) — silent no-op otherwise, matching the suite convention. Always
// writes a temp COPY, never the source, so a manual `tcm writeback --apply` on the working db can't affect it.
public sealed class TargetSchedulerWriterTests
{
    private const string TsDbPath =
        @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";
    private const string LibraryPath = @"E:\Photography\Astro Photography\Processing";

    [Fact]
    public async Task Apply_WritesDiskCounts_AndVerifies()
    {
        if (!File.Exists(TsDbPath) || !Directory.Exists(LibraryPath))
            return;

        string catalog = TestSupport.NewDbPath();
        string tsCopy = TestSupport.NewDbPath();
        File.Copy(TsDbPath, tsCopy, overwrite: true);
        File.SetAttributes(tsCopy, FileAttributes.Normal);   // defensive: a read-only source would yield a read-only copy
        try
        {
            WriteBackPlan plan = await BuildPlanAsync(catalog, tsCopy);
            Assert.NotEmpty(plan.Writes);   // real library -> Both targets with writable cells

            WriteBackResult result;
            using (TargetSchedulerWriter writer = new(tsCopy))
            {
                Assert.True(writer.HasRequiredColumns);   // exposureplan has acquired/accepted/Id (version-agnostic)
                Assert.False(writer.HasOpenSidecar);
                result = writer.Execute(plan, apply: true);
            }

            Assert.True(result.Applied);
            Assert.Empty(result.VerifyFailures);

            // Independent read-back (fresh connection): a sampled written row is now acquired == accepted == DiskCount,
            // and desired was ratcheted to at least the disk count (never below what was kept).
            PlannedWrite sample = plan.Writes[0];
            (int acquired, int accepted, int desired) = ReadCounts(tsCopy, sample.TsExposurePlanId);
            Assert.Equal(sample.DiskCount, acquired);
            Assert.Equal(sample.DiskCount, accepted);
            Assert.True(desired >= sample.DiskCount);
        }
        finally
        {
            TestSupport.Cleanup(catalog);
            TestSupport.Cleanup(tsCopy);
        }
    }

    [Fact]
    public async Task DryRun_DoesNotMutate()
    {
        if (!File.Exists(TsDbPath) || !Directory.Exists(LibraryPath))
            return;

        string catalog = TestSupport.NewDbPath();
        string tsCopy = TestSupport.NewDbPath();
        File.Copy(TsDbPath, tsCopy, overwrite: true);
        File.SetAttributes(tsCopy, FileAttributes.Normal);
        try
        {
            WriteBackPlan plan = await BuildPlanAsync(catalog, tsCopy);
            Assert.NotEmpty(plan.Writes);

            PlannedWrite sample = plan.Writes[0];
            (int beforeAcq, int beforeAcc, int beforeDes) = ReadCounts(tsCopy, sample.TsExposurePlanId);

            using (TargetSchedulerWriter writer = new(tsCopy))
            {
                WriteBackResult result = writer.Execute(plan, apply: false);
                Assert.False(result.Applied);
                Assert.Empty(result.VerifyFailures);
            }

            (int afterAcq, int afterAcc, int afterDes) = ReadCounts(tsCopy, sample.TsExposurePlanId);
            Assert.Equal(beforeAcq, afterAcq);
            Assert.Equal(beforeAcc, afterAcc);
            Assert.Equal(beforeDes, afterDes);
        }
        finally
        {
            TestSupport.Cleanup(catalog);
            TestSupport.Cleanup(tsCopy);
        }
    }

    [Fact]
    public void Apply_RatchetsDesiredUp_OnOvershoot_NeverDown_OnUndershoot()
    {
        string tsDb = TestSupport.NewDbPath();
        using (SqliteConnection setup = new(new SqliteConnectionStringBuilder { DataSource = tsDb }.ToString()))
        {
            setup.Open();
            using SqliteCommand cmd = setup.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, acquired INTEGER, accepted INTEGER, desired INTEGER);"
                + "INSERT INTO exposureplan (Id, acquired, accepted, desired) VALUES (1, 0, 0, 100), (2, 0, 0, 100);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();   // release the setup connection before the writer opens read-write
        try
        {
            // plan 1 over-shoots its goal (disk 140 > desired 100); plan 2 under-shoots (disk 50 < 100).
            WriteBackPlan plan = new(
                [new PlannedWrite(1, Guid.NewGuid(), "Over", "H", FilterPurpose.Light, 140),
                 new PlannedWrite(2, Guid.NewGuid(), "Under", "H", FilterPurpose.Light, 50)],
                [], [], 0);

            using (TargetSchedulerWriter writer = new(tsDb))
            {
                Assert.True(writer.HasRequiredColumns);
                WriteBackResult result = writer.Execute(plan, apply: true);
                Assert.Empty(result.VerifyFailures);
            }

            Assert.Equal((140, 140, 140), ReadCounts(tsDb, 1));   // over-shoot: acc=acq=140, desired raised 100->140
            Assert.Equal((50, 50, 100), ReadCounts(tsDb, 2));     // under-shoot: acc=acq=50, desired unchanged at 100
        }
        finally
        {
            TestSupport.Cleanup(tsDb);
        }
    }

    private static async Task<WriteBackPlan> BuildPlanAsync(string catalog, string tsCopy)
    {
        CatalogBuildReport report =
            await CatalogBuilder.BuildAsync(catalog, libraryRoot: LibraryPath, targetSchedulerDbPath: tsCopy);
        using CatalogStore store = CatalogStore.OpenReadOnly(catalog);
        return WriteBackPlanner.Plan(
            store.GetTargets(), store.GetExposurePlans(), store.GetExposureTemplates(),
            store.GetInventoryFilters(), report);
    }

    private static (int Acquired, int Accepted, int Desired) ReadCounts(string tsDbPath, long planId)
    {
        using SqliteConnection conn = new(new SqliteConnectionStringBuilder
        {
            DataSource = tsDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT acquired, accepted, desired FROM exposureplan WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", planId);
        using SqliteDataReader r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
    }
}
