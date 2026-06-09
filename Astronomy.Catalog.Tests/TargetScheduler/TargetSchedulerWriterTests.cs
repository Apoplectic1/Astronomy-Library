using Astronomy.Catalog.Build;
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
        @"E:\Projects\VisualStudio\Astronomy\TargetCatalogManager\TS Database\schedulerdb.sqlite";
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

            // Independent read-back (fresh connection): a sampled written row is now acquired == accepted == DiskCount.
            PlannedWrite sample = plan.Writes[0];
            (int acquired, int accepted) = ReadCounts(tsCopy, sample.TsExposurePlanId);
            Assert.Equal(sample.DiskCount, acquired);
            Assert.Equal(sample.DiskCount, accepted);
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
            (int beforeAcq, int beforeAcc) = ReadCounts(tsCopy, sample.TsExposurePlanId);

            using (TargetSchedulerWriter writer = new(tsCopy))
            {
                WriteBackResult result = writer.Execute(plan, apply: false);
                Assert.False(result.Applied);
                Assert.Empty(result.VerifyFailures);
            }

            (int afterAcq, int afterAcc) = ReadCounts(tsCopy, sample.TsExposurePlanId);
            Assert.Equal(beforeAcq, afterAcq);
            Assert.Equal(beforeAcc, afterAcc);
        }
        finally
        {
            TestSupport.Cleanup(catalog);
            TestSupport.Cleanup(tsCopy);
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

    private static (int Acquired, int Accepted) ReadCounts(string tsDbPath, long planId)
    {
        using SqliteConnection conn = new(new SqliteConnectionStringBuilder
        {
            DataSource = tsDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT acquired, accepted FROM exposureplan WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", planId);
        using SqliteDataReader r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return (r.GetInt32(0), r.GetInt32(1));
    }
}
