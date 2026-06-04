using Astronomy.Catalog;
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
                "inventory_target", "inventory_filter" })
                Assert.Equal(1, TestSupport.ScalarLong(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));

            // Lookup tables seeded.
            Assert.Equal(4, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM project_state;"));
            Assert.Equal(2, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM frame_purpose;"));
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

            Target target = new(
                Guid.NewGuid(), project.Id, "M42", Enabled: true, RaHours: 5.59, DecDegreesSigned: -5.39,
                Epoch.J2000, RotationDeg: 0.0, RoiPercent: 100.0, Priority: null, CreatedAt: now, ImportedFromTsGuid: null);
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
            Assert.Equal(plan, Assert.Single(store.GetExposurePlans(target.Id)));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    [Fact]
    public void ReplaceInventory_PersistsScannerAggregates()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);

            store.ReplaceInventory(SampleReport());

            InventoryTarget it = Assert.Single(store.GetInventoryTargets());
            Assert.Equal("M42 - Orion", it.DirectoryName);
            Assert.Equal("M42", it.Catalog);
            Assert.Equal("Orion", it.CommonName);
            Assert.Equal(5.59, it.RaHours);

            InventoryFilter inf = Assert.Single(store.GetInventoryFilters("M42 - Orion"));
            Assert.Equal("H", inf.FilterName);
            Assert.Equal(FilterPurpose.Light, inf.Purpose);
            Assert.Equal(12, inf.ExposureCount);
            Assert.Equal(3600.0, inf.TotalIntegrationSeconds);
            Assert.Equal(100, inf.TypicalGain);
            Assert.Equal(1, inf.TypicalBinningX);
            Assert.Equal("Z533", inf.Cameras);
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    [Fact]
    public void ReplaceInventory_IsAFullReplace()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);
            store.ReplaceInventory(SampleReport());
            store.ReplaceInventory(SampleReport()); // second scan replaces, not appends

            Assert.Single(store.GetInventoryTargets());
            Assert.Single(store.GetInventoryFilters());
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    private static ImageLibraryReport SampleReport()
    {
        DateTime first = new(2024, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        TypicalSettings typical = new(gain: 100, offset: 50, setTempC: -10.0, binning: (1, 1), exposureSec: 300.0);
        FilterAggregate ha = new(
            filterName: "H", filterCode: "H", purpose: FilterPurpose.Light, exposureCount: 12,
            totalIntegration: TimeSpan.FromSeconds(3600), firstImagedUtc: first, lastImagedUtc: first.AddHours(2),
            typical: typical, camerasSeen: new[] { "Z533" });
        TargetReport target = new(
            directoryName: "M42 - Orion", catalog: "M42", commonName: "Orion", objectName: "M42",
            raHours: 5.59, decDegrees: -5.39, filters: new[] { ha });
        return new ImageLibraryReport(
            libraryRoot: @"C:\proc", scannedAtUtc: new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            targets: new[] { target }, skippedFiles: new Dictionary<string, string>());
    }
}
