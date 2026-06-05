using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class CatalogBuilderTests
{
    private const string SnapshotPath =
        @"E:\Projects\VisualStudio\Astronomy\IntervalScheduler\TS DataBase Example\schedulerdb.sqlite";

    [Fact]
    public async Task BuildAsync_TsSnapshotOnly_PopulatesPlannedOnly()
    {
        if (!File.Exists(SnapshotPath))
            return; // pinned snapshot not present — silent no-op

        string path = TestSupport.NewDbPath();
        try
        {
            // No library root → disk plane empty → the whole TS plan lands as planned-only, anchored to nothing.
            CatalogBuildReport report =
                await CatalogBuilder.BuildAsync(path, libraryRoot: null, targetSchedulerDbPath: SnapshotPath);

            Assert.Equal(102, report.TsTargetCount);
            Assert.Equal(0, report.DiskTargetCount);
            Assert.Equal(0, report.BothCount);
            Assert.Equal(0, report.ActualOnlyCount);
            Assert.Equal(102, report.PlannedOnlyCount);

            using CatalogStore store = CatalogStore.Open(path);
            Assert.Equal(10, store.GetProjects().Count);
            Assert.Equal(102, store.GetTargets().Count);
            Assert.Equal(20, store.GetExposureTemplates().Count);
            Assert.Equal(662, store.GetExposurePlans().Count);
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
