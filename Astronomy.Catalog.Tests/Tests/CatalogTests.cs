using Astronomy.Catalog;
using Astronomy.Catalog.Schema;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class SchemaManagerTests
{
    [Fact]
    public void Open_CreatesSchema_WithWalAndMigrationTracking()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using SqliteConnection connection = SchemaManager.Open(path);

            Assert.Equal("wal", (string)TestSupport.Scalar(connection, "PRAGMA journal_mode;")!);
            Assert.Equal(1, SchemaManager.GetUserVersion(connection));
            Assert.Equal(1, TestSupport.ScalarLong(connection, $"SELECT COUNT(*) FROM {SchemaManager.MigrationTable} WHERE version = 1;"));

            foreach (string table in new[] { "profile", "project", "target", "exposure_template", "exposure_plan", "image_file", "scan_state" })
                Assert.Equal(1, TestSupport.ScalarLong(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));

            Assert.Equal(1, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='view' AND name='inventory_rollup';"));

            // Lookup tables seeded.
            Assert.Equal(4, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM project_state;"));
            Assert.Equal(6, TestSupport.ScalarLong(connection, "SELECT COUNT(*) FROM processing_stage;"));
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }

    [Fact]
    public void Open_IsIdempotent_AppliesEachMigrationOnce()
    {
        string path = TestSupport.NewDbPath();
        try
        {
            using (SchemaManager.Open(path)) { }
            using SqliteConnection second = SchemaManager.Open(path);

            Assert.Equal(1, TestSupport.ScalarLong(second, $"SELECT COUNT(*) FROM {SchemaManager.MigrationTable};"));
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
    public void InventoryRollup_SumsLightsAndExcludesCalibration()
    {
        string path = TestSupport.NewDbPath();
        long now = TestSupport.NowUnix();
        try
        {
            using CatalogStore store = CatalogStore.Open(path);

            for (int i = 0; i < 3; i++)
            {
                store.InsertImageFile(new ImageFile(
                    Guid.NewGuid(), $@"C:\proc\M42\Ha\{i}.xisf", TargetId: null, "M42", "Ha", FrameType.Light,
                    ProcessingStage.Captures, ExposureSeconds: 300.0, CapturedAt: now + i, "Z533", Gain: 100,
                    OffsetAdu: 50, RaHours: 5.59, DecDegreesSigned: -5.39, FileMtime: now, FileSize: 1000, ScannedAt: now));
            }

            // A dark frame must be excluded from the lights-only rollup.
            store.InsertImageFile(new ImageFile(
                Guid.NewGuid(), @"C:\proc\M42\dark.xisf", null, "M42", "Ha", FrameType.Dark, ProcessingStage.Captures,
                300.0, now, "Z533", 100, 50, null, null, now, 1000, now));

            InventoryRollupRow row = Assert.Single(store.GetInventoryRollup());
            Assert.Equal("Ha", row.FilterName);
            Assert.Equal(ProcessingStage.Captures, row.ProcessingStage);
            Assert.Equal(3, row.FrameCount);
            Assert.Equal(900.0, row.TotalIntegrationSeconds);
            Assert.Equal(now, row.FirstCapturedAt);
            Assert.Equal(now + 2, row.LastCapturedAt);
        }
        finally
        {
            TestSupport.Cleanup(path);
        }
    }
}
