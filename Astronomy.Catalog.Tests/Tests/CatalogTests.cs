using Astronomy.Catalog;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class SchemaManagerTests
{
    [Fact]
    public void Open_CreatesSchema_WithWalAndLookups()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using SqliteConnection connection = SchemaManager.Open(path);

            Assert.Equal("wal", (string)TestSupport.Scalar(connection, "PRAGMA journal_mode;")!);

            foreach (string table in new[] { "profile", "project", "target", "exposure_template", "exposure_plan",
                "inventory_filter", "target_source" })
                Assert.Equal(1, TestSupport.ScalarLong(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));

            // Lookup tables seeded.
            Assert.Equal(4, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM project_state;"));
            Assert.Equal(2, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM frame_purpose;"));
            Assert.Equal(3, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM target_source;"));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    [Fact]
    public void Open_IsIdempotent_NoDuplicateLookups()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using (SchemaManager.Open(path)) { }
            using SqliteConnection second = SchemaManager.Open(path);

            // Re-applying the schema (CREATE IF NOT EXISTS + INSERT OR IGNORE) must not duplicate seed rows.
            Assert.Equal(2, TestSupport.ScalarLong(second, "SELECT COUNT(*) FROM frame_purpose;"));
            Assert.Equal(3, TestSupport.ScalarLong(second, "SELECT COUNT(*) FROM epoch;"));
            Assert.Equal(3, TestSupport.ScalarLong(second, "SELECT COUNT(*) FROM target_source;"));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }
}

public sealed class CatalogStoreTests
{
    [Fact]
    public void InsertAndRead_PlanEntities_RoundTrip()
    {
        string path = TestSupport.NewDbPath();
        long now = TestSupport.NowUnix();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);

            Profile profile = new(Guid.NewGuid(), "Penns Park", "nina-guid-123", now);
            store.InsertProfile(profile);

            Project project = new(
                Guid.NewGuid(), profile.Id, "Nebulae - Above 45", "winter set", ProjectState.Active,
                ProjectPriority.High, MinimumAltitudeDeg: 30.0, MaximumAltitudeDeg: null, MinimumTimeMinutes: 60,
                UseCustomHorizon: true, HorizonOffsetDeg: 5.0, MeridianWindowMinutes: 480, IsMosaic: false,
                EnableGrader: true, CreatedAt: now, ActiveAt: now, InactiveAt: null, ImportedFromTsGuid: null);
            store.InsertProject(project);

            // A fully-populated "Both" target exercises every column (disk identity + plan attributes).
            Target target = new(
                Guid.NewGuid(), TargetSource.Both, project.Id, "M42 - Orion", Enabled: true, RaHours: 5.59,
                DecDegreesSigned: -5.39, Epoch.J2000, RotationDeg: 0.0, RoiPercent: 100.0, Priority: null,
                DirectoryName: "M42 - Orion", Catalog: "M42", CommonName: "Orion", ObjectName: "M42",
                ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: "ts-target-1");
            store.InsertTarget(target);

            ExposureTemplate template = new(
                Guid.NewGuid(), profile.Id, "Ha 3nm", "Ha", Gain: 100, OffsetAdu: 50, Binning: 1, ReadoutMode: 0,
                DefaultExposureSeconds: 300.0, ImportedFromTsGuid: null);
            store.InsertExposureTemplate(template);

            ExposurePlan plan = new(
                Guid.NewGuid(), target.Id, template.Id, ExposureSeconds: 300.0, DesiredCount: 60,
                AcquiredCount: 20, AcceptedCount: 18, Enabled: true, ImportedFromTsGuid: null);
            store.InsertExposurePlan(plan);

            Assert.Equal(project, Assert.Single(store.GetProjects()));
            Assert.Equal(target, Assert.Single(store.GetTargets(project.Id)));
            Assert.Equal(target, Assert.Single(store.GetShotTargets()));
            Assert.Equal(plan, Assert.Single(store.GetExposurePlans(target.Id)));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    [Fact]
    public void WriteCatalog_RoundTrips_Graph_AndFullyReplaces()
    {
        string path = TestSupport.NewDbPath();
        long now = TestSupport.NowUnix();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);

            CatalogGraph graph = SampleGraph(now, out Target target, out ExposurePlan plan, out InventoryFilter inventory);
            store.WriteCatalog(graph);

            Assert.Equal(target, Assert.Single(store.GetShotTargets()));
            Assert.Equal(inventory, Assert.Single(store.GetInventoryFilters(target.Id)));
            Assert.Equal(plan, Assert.Single(store.GetExposurePlans(target.Id)));

            // Second write fully replaces, not appends.
            store.WriteCatalog(graph);
            Assert.Single(store.GetTargets());
            Assert.Single(store.GetInventoryFilters());
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    private static CatalogGraph SampleGraph(long now, out Target target, out ExposurePlan plan, out InventoryFilter inventory)
    {
        Profile profile = new(Guid.NewGuid(), "Penns Park", "nina-guid", now);
        Project project = new(
            Guid.NewGuid(), profile.Id, "Winter Nebulae", null, ProjectState.Active, ProjectPriority.Normal,
            null, null, null, false, null, null, false, true, now, null, null, "ts-project-1");
        Guid targetId = Guid.NewGuid();
        target = new(
            targetId, TargetSource.Both, project.Id, "M42 - Orion", Enabled: true, RaHours: 5.59,
            DecDegreesSigned: -5.39, Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null,
            DirectoryName: "M42 - Orion", Catalog: "M42", CommonName: "Orion", ObjectName: "M42",
            ScannedAt: now, CreatedAt: now, ImportedFromTsGuid: "ts-target-1");
        ExposureTemplate template = new(
            Guid.NewGuid(), profile.Id, "Ha 3nm", "H", 100, 50, 1, null, 300.0, "ts-template-1");
        plan = new(
            Guid.NewGuid(), targetId, template.Id, ExposureSeconds: 300.0, DesiredCount: 60, AcquiredCount: 20,
            AcceptedCount: 18, Enabled: true, ImportedFromTsGuid: "ts-plan-1");
        inventory = new(
            targetId, "H", FilterPurpose.Light, "H", ExposureCount: 12, TotalIntegrationSeconds: 3600.0,
            FirstImagedAt: 1_700_000_000, LastImagedAt: 1_700_007_200, TypicalGain: 100, TypicalOffset: 50,
            TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1, TypicalExposureSeconds: 300.0,
            Cameras: "Z533");
        return new CatalogGraph([profile], [project], [template], [target], [plan], [inventory]);
    }
}
