using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class CatalogBuilderTests
{
    // Local TS working db under TCM's TS Database/ (NINA-nightly schema; living — assert invariants, not exact counts).
    private const string TsDbPath =
        @"E:\Projects\VisualStudio\Astronomy\TargetCatalogManager\TS Database\schedulerdb.sqlite";

    [Fact]
    public async Task BuildAsync_TsOnly_PopulatesPlannedOnly()
    {
        if (!File.Exists(TsDbPath))
            return; // dev db not present — silent no-op

        string path = TestSupport.NewDbPath();
        try
        {
            // No library root → disk plane empty → every TS target lands as planned-only, anchored to nothing.
            CatalogBuildReport report =
                await CatalogBuilder.BuildAsync(path, libraryRoot: null, targetSchedulerDbPath: TsDbPath);

            Assert.True(report.TsTargetCount > 0);
            Assert.Equal(0, report.DiskTargetCount);
            Assert.Equal(0, report.BothCount);
            Assert.Equal(0, report.ActualOnlyCount);
            Assert.Equal(report.TsTargetCount, report.PlannedOnlyCount);   // all TS targets → planned-only

            using CatalogStore store = CatalogStore.Open(path);
            Assert.NotEmpty(store.GetProjects());
            Assert.NotEmpty(store.GetExposureTemplates());
            Assert.NotEmpty(store.GetExposurePlans());
            Assert.Equal(report.TsTargetCount, store.GetTargets().Count);
            Assert.Empty(store.GetShotTargets());
            Assert.Empty(store.GetInventoryFilters());
            Assert.All(store.GetTargets(), t => Assert.Equal(TargetSource.Planned, t.Source));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }
}
