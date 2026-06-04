using Astronomy.Catalog;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class TsCatalogImporterTests
{
    private const string SnapshotPath =
        @"E:\Projects\VisualStudio\Astronomy\IntervalScheduler\TS DataBase Example\schedulerdb.sqlite";

    [Fact]
    public void Import_PinnedSnapshot_PopulatesPlanPlane()
    {
        if (!File.Exists(SnapshotPath))
            return; // pinned snapshot not present — silent no-op (matches the reader test)

        string path = TestSupport.NewDbPath();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);

            using (TargetSchedulerReader ts = new(SnapshotPath))
                TsCatalogImporter.Import(store, ts);

            // Documented snapshot counts (no orphans expected in a consistent TS db).
            Assert.Equal(10, store.GetProjects().Count);
            Assert.Equal(102, store.GetTargets().Count);
            Assert.Equal(20, store.GetExposureTemplates().Count);
            Assert.Equal(662, store.GetExposurePlans().Count);
            Assert.NotEmpty(store.GetProfiles());

            // Every imported target is J2000 with in-range RA and a provenance link back to TS.
            Assert.All(store.GetTargets(), t =>
            {
                Assert.Equal(Epoch.J2000, t.Epoch);
                Assert.False(string.IsNullOrEmpty(t.ImportedFromTsGuid));
                if (t.RaHours is double ra)
                    Assert.InRange(ra, 0.0, 24.0);
            });

            // Re-import fully replaces (counts don't double).
            using (TargetSchedulerReader ts2 = new(SnapshotPath))
                TsCatalogImporter.Import(store, ts2);
            Assert.Equal(102, store.GetTargets().Count);
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }
}
