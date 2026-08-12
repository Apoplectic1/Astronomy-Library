using Astronomy.Catalog.Intent;
using Astronomy.Catalog.Intent.TsImport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The write/lookup surface's contract: full-value upserts keyed by id (created_at create-only),
// NULL means unset (never coalesced), caller-owned transaction composition, provenance lookups
// (known / unknown / duplicate), and the two-write-paths compatibility pin — rows lifted by
// TsIntentImporter resolve and update through IntentWriter with GuidBlob encoding and
// imported_from_ts_guid conventions intact.
public sealed class IntentWriterTests
{
    [Fact]
    public void CreateChain_RoundTripsExactly()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);
        Guid projectId = Guid.NewGuid(), targetId = Guid.NewGuid(), templateId = Guid.NewGuid(), planId = Guid.NewGuid();

        writer.UpsertProject(new ProjectIntent
        {
            Id = projectId, ProfileId = profileId, Name = "M 31", Description = "Andromeda",
            StateId = 1, PriorityId = 2, MinimumTimeMinutes = 30, MinimumAltitudeDeg = 25.5,
            UseCustomHorizon = true, HorizonOffsetDeg = 5.0, MeridianWindowMinutes = 60,
            CreatedAt = 1_700_000_000, ActiveAt = 1_700_000_100, ImportedFromTsGuid = "p-1",
        });
        writer.UpsertTarget(new TargetIntent
        {
            Id = targetId, ProjectId = projectId, Name = "M 31", RaHours = 0.712,
            DecDegreesSigned = 41.269, RotationDeg = 15.0, CreatedAt = 1_700_000_200,
            ImportedFromTsGuid = "t-1",
        });
        writer.UpsertExposureTemplate(new ExposureTemplateIntent
        {
            Id = templateId, ProfileId = profileId, Name = "Ha 300s", FilterName = "Ha",
            Gain = 100, OffsetAdu = 50, Binning = 1, DefaultExposureSeconds = 300.0,
            TwilightLevelId = 1, MoonAvoidanceEnabled = true, MoonAvoidanceSeparationDeg = 60.0,
            ImportedFromTsGuid = "et-1",
        });
        writer.UpsertExposurePlan(new ExposurePlanIntent
        {
            Id = planId, TargetId = targetId, ExposureTemplateId = templateId,
            DesiredCount = 40, ImportedFromTsGuid = "ep-1",
        });

        // Values read back exactly as supplied; caller-supplied created_at landed verbatim.
        Assert.Equal("Andromeda", Scalar(store, "SELECT description FROM project WHERE imported_from_ts_guid = 'p-1';"));
        Assert.Equal(1_700_000_000L, Scalar(store, "SELECT created_at FROM project WHERE imported_from_ts_guid = 'p-1';"));
        Assert.Equal(30L, Scalar(store, "SELECT minimum_time_minutes FROM project WHERE imported_from_ts_guid = 'p-1';"));
        Assert.Equal(1L, Scalar(store, "SELECT use_custom_horizon FROM project WHERE imported_from_ts_guid = 'p-1';"));
        Assert.Equal(0.712, Scalar(store, "SELECT ra_hours FROM target WHERE imported_from_ts_guid = 't-1';"));
        Assert.Equal(2L, Scalar(store, "SELECT epoch_id FROM target WHERE imported_from_ts_guid = 't-1';"));   // DDL-mirror default J2000
        Assert.Equal(1_700_000_200L, Scalar(store, "SELECT created_at FROM target WHERE imported_from_ts_guid = 't-1';"));
        Assert.Equal("Ha", Scalar(store, "SELECT filter_name FROM exposure_template WHERE imported_from_ts_guid = 'et-1';"));
        Assert.Equal(60.0, Scalar(store, "SELECT moon_avoidance_separation_deg FROM exposure_template WHERE imported_from_ts_guid = 'et-1';"));
        Assert.Equal(40L, Scalar(store, "SELECT desired_count FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));
        Assert.Equal(1L, Scalar(store, "SELECT enabled FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));

        // The FK chain resolves end to end through the stored GUID blobs.
        Assert.Equal("M 31", Scalar(store,
            "SELECT p.name FROM exposure_plan ep JOIN target t ON t.id = ep.target_id JOIN project p ON p.id = t.project_id " +
            "WHERE ep.imported_from_ts_guid = 'ep-1';"));
    }

    [Fact]
    public void Update_IsFullValue_AndPreservesCreatedAt()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);
        Guid projectId = Guid.NewGuid();

        writer.UpsertProject(new ProjectIntent
        {
            Id = projectId, ProfileId = profileId, Name = "Before", Description = "old text",
            StateId = 0, PriorityId = 1, MinimumTimeMinutes = 60, MeridianWindowMinutes = 45,
            CreatedAt = 1_700_000_000, ActiveAt = 1_700_000_500,
        });
        writer.UpsertProject(new ProjectIntent
        {
            Id = projectId, ProfileId = profileId, Name = "After", Description = null,
            StateId = 1, PriorityId = 2, MinimumTimeMinutes = null, MeridianWindowMinutes = null,
            IsMosaic = true, CreatedAt = 9_999_999_999, ActiveAt = null,
        });

        Assert.Equal(1L, Scalar(store, "SELECT count(*) FROM project;"));   // updated, not duplicated
        Assert.Equal("After", Scalar(store, "SELECT name FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT description IS NULL FROM project;"));            // full-value: NULL overwrote
        Assert.Equal(1L, Scalar(store, "SELECT minimum_time_minutes IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT active_at IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT is_mosaic FROM project;"));
        Assert.Equal(1_700_000_000L, Scalar(store, "SELECT created_at FROM project;"));         // creation instant immutable
    }

    [Fact]
    public void Create_NullMeansUnset_NoInventedValues()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);

        writer.UpsertProject(new ProjectIntent
        {
            Id = Guid.NewGuid(), ProfileId = profileId, Name = "Minimal", StateId = 0, PriorityId = 1,
            CreatedAt = 1_700_000_000,
        });

        // Every optional the caller left unset stored as NULL — no default, zero, or sentinel invented.
        Assert.Equal(1L, Scalar(store, "SELECT description IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT minimum_time_minutes IS NULL FROM project;"));   // nullable since 0002
        Assert.Equal(1L, Scalar(store, "SELECT minimum_altitude_deg IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT maximum_altitude_deg IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT meridian_window_minutes IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT filter_switch_frequency IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT dither_every IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT active_at IS NULL FROM project;"));
        Assert.Equal(1L, Scalar(store, "SELECT imported_from_ts_guid IS NULL FROM project;"));
    }

    [Fact]
    public void CallerTransaction_RollbackDiscardsGroupedWrites()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);
        Guid projectId = Guid.NewGuid();

        using (SqliteTransaction tx = store.Connection.BeginTransaction())
        {
            writer.UpsertProject(new ProjectIntent
            {
                Id = projectId, ProfileId = profileId, Name = "Doomed", StateId = 0, PriorityId = 1,
                CreatedAt = 1,
            }, tx);
            writer.UpsertTarget(new TargetIntent
            {
                Id = Guid.NewGuid(), ProjectId = projectId, Name = "Doomed target",
                RaHours = 1.0, DecDegreesSigned = 2.0, CreatedAt = 2,
            }, tx);
            Assert.Null(writer.FindProjectId("nothing-yet", tx));   // lookups compose with the caller's transaction too
            tx.Rollback();
        }

        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM project;"));
        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM target;"));
        Assert.Equal(1L, Scalar(store, "SELECT count(*) FROM profile;"));   // pre-transaction content intact
    }

    [Fact]
    public void ProvenanceLookup_KnownResolves_UnknownIsNull()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);
        Guid projectId = Guid.NewGuid(), targetId = Guid.NewGuid(), templateId = Guid.NewGuid(), planId = Guid.NewGuid();

        writer.UpsertProject(new ProjectIntent
        { Id = projectId, ProfileId = profileId, Name = "P", StateId = 0, PriorityId = 1, CreatedAt = 1, ImportedFromTsGuid = "p-1" });
        writer.UpsertTarget(new TargetIntent
        { Id = targetId, ProjectId = projectId, Name = "T", RaHours = 1.0, DecDegreesSigned = 2.0, CreatedAt = 2, ImportedFromTsGuid = "t-1" });
        writer.UpsertExposureTemplate(new ExposureTemplateIntent
        { Id = templateId, ProfileId = profileId, Name = "E", FilterName = "L", Binning = 1, DefaultExposureSeconds = 60.0, TwilightLevelId = 0, ImportedFromTsGuid = "et-1" });
        writer.UpsertExposurePlan(new ExposurePlanIntent
        { Id = planId, TargetId = targetId, ExposureTemplateId = templateId, DesiredCount = 10, ImportedFromTsGuid = "ep-1" });

        Assert.Equal(projectId, writer.FindProjectId("p-1"));
        Assert.Equal(targetId, writer.FindTargetId("t-1"));
        Assert.Equal(templateId, writer.FindExposureTemplateId("et-1"));
        Assert.Equal(planId, writer.FindExposurePlanId("ep-1"));

        Assert.Null(writer.FindProjectId("no-such-key"));
        Assert.Null(writer.FindTargetId("p-1"));   // keys are per-entity-kind — a project key resolves no target
    }

    [Fact]
    public void ProvenanceLookup_Duplicate_FailsLoudly()
    {
        using IntentStore store = IntentStore.Open(NewStorePath());
        IntentWriter writer = new(store);
        Guid profileId = SeedProfile(store);
        for (int i = 0; i < 2; i++)
        {
            writer.UpsertProject(new ProjectIntent
            { Id = Guid.NewGuid(), ProfileId = profileId, Name = $"P{i}", StateId = 0, PriorityId = 1, CreatedAt = 1, ImportedFromTsGuid = "dup" });
        }

        IntentStoreException ex = Assert.Throws<IntentStoreException>(() => writer.FindProjectId("dup"));

        Assert.Contains("project", ex.Message);
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void ImportedRows_UpdateThroughSurface_SharedEncodingAndProvenance()
    {
        // The compatibility pin: two write paths into one schema share their invariants by test.
        // Rows lifted by TsIntentImporter must resolve through IntentWriter's provenance lookups
        // (same imported_from_ts_guid conventions) and update under the resolved id (same GuidBlob
        // encoding) — landing on the imported row, never beside it.
        using IntentStore store = IntentStore.Open(NewStorePath());
        TsIntentImporter.Import(NewTsDb(), store, DateTimeOffset.FromUnixTimeSeconds(1_750_000_000));
        IntentWriter writer = new(store);

        Guid? planId = writer.FindExposurePlanId("ep-1");
        Guid? targetId = writer.FindTargetId("t-1");
        Guid? templateId = writer.FindExposureTemplateId("et-1");
        Assert.NotNull(planId);
        Assert.NotNull(targetId);
        Assert.NotNull(templateId);

        // The resolved id round-trips through GuidBlob back to the imported row.
        Assert.Equal(1L, ScalarWith(store, "SELECT count(*) FROM exposure_plan WHERE id = $id;",
            ("$id", GuidBlob.ToBlob(planId.Value))));

        long plansBefore = (long)Scalar(store, "SELECT count(*) FROM exposure_plan;")!;
        writer.UpsertExposurePlan(new ExposurePlanIntent
        {
            Id = planId.Value, TargetId = targetId.Value, ExposureTemplateId = templateId.Value,
            ExposureSeconds = null,   // stays inherit-template (the importer stored the -1.0 sentinel as NULL)
            DesiredCount = 99, Enabled = false, ImportedFromTsGuid = "ep-1",
        });

        Assert.Equal(plansBefore, Scalar(store, "SELECT count(*) FROM exposure_plan;"));   // updated, not duplicated
        Assert.Equal(99L, Scalar(store, "SELECT desired_count FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));
        Assert.Equal(0L, Scalar(store, "SELECT enabled FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));
        Assert.Equal(1L, Scalar(store, "SELECT exposure_seconds IS NULL FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));

        // And the writer's own rows carry provenance the same way the importer's do.
        writer.UpsertTarget(new TargetIntent
        {
            Id = Guid.NewGuid(), ProjectId = writer.FindProjectId("p-101")!.Value, Name = "Surface-born",
            RaHours = 3.0, DecDegreesSigned = 4.0, CreatedAt = 5, ImportedFromTsGuid = "t-new",
        });
        Assert.NotNull(writer.FindTargetId("t-new"));
    }

    // ---- Fixture: minimal schedulerdb.sqlite (real TS DDL, one plain project) for the lift ------

    private static string NewTsDb()
    {
        string db = Path.Combine(NewDir(), "schedulerdb.sqlite");
        Exec(db,
            """
            CREATE TABLE project (Id INTEGER NOT NULL, profileId TEXT NOT NULL, name TEXT NOT NULL, description TEXT,
                state INTEGER, priority INTEGER, createdate INTEGER, activedate INTEGER, inactivedate INTEGER,
                minimumtime INTEGER, minimumaltitude REAL, usecustomhorizon INTEGER, horizonoffset REAL,
                meridianwindow INTEGER, filterswitchfrequency INTEGER, ditherevery INTEGER, enablegrader INTEGER,
                isMosaic INTEGER NOT NULL DEFAULT 0, flatsHandling INTEGER NOT NULL DEFAULT 0,
                maximumAltitude REAL DEFAULT 0, smartexposureorder INTEGER DEFAULT 0, guid TEXT, PRIMARY KEY(Id));
            CREATE TABLE target (Id INTEGER NOT NULL, name TEXT NOT NULL, active INTEGER NOT NULL, ra REAL, dec REAL,
                epochcode INTEGER NOT NULL, rotation REAL, roi REAL, projectid INTEGER, unusedOEO TEXT, guid TEXT,
                priority INTEGER DEFAULT -1, PRIMARY KEY(Id));
            CREATE TABLE exposuretemplate (Id INTEGER NOT NULL, profileId TEXT NOT NULL, name TEXT NOT NULL,
                filtername TEXT NOT NULL, gain INTEGER, offset INTEGER, bin INTEGER, readoutmode INTEGER,
                twilightlevel INTEGER, moonavoidanceenabled INTEGER, moonavoidanceseparation REAL,
                moonavoidancewidth INTEGER, maximumhumidity REAL, defaultexposure REAL DEFAULT 60,
                moonrelaxscale REAL DEFAULT 0, moonrelaxmaxaltitude REAL DEFAULT 5, moonrelaxminaltitude REAL DEFAULT -15,
                moondownenabled INTEGER DEFAULT 0, ditherevery INTEGER DEFAULT -1, minutesOffset INTEGER DEFAULT 0,
                guid TEXT, PRIMARY KEY(Id));
            CREATE TABLE exposureplan (Id INTEGER NOT NULL, profileId TEXT NOT NULL, exposure REAL NOT NULL,
                desired INTEGER, acquired INTEGER, accepted INTEGER, targetid INTEGER, exposureTemplateId INTEGER,
                enabled INTEGER DEFAULT 1, guid TEXT, PRIMARY KEY(Id));

            INSERT INTO project VALUES
                (101, 'profile-guid-1', 'M 31', 'Andromeda', 1, 1, 1700000000, 1700000100, NULL, 30, 0.0, 0, 0.0,
                 NULL, NULL, NULL, 1, 0, 0, 0.0, 0, 'p-101');
            INSERT INTO target VALUES
                (1, 'M 31', 1, 0.712, 41.269, 2, NULL, 100.0, 101, NULL, 't-1', -1);
            INSERT INTO exposuretemplate VALUES
                (201, 'profile-guid-1', 'Ha 300s', 'Ha', 100, 50, 1, -1, 1, 1, 60.0, 7, 0.0, 300.0, 0.5, 5.0, -15.0,
                 0, -1, 0, 'et-1');
            INSERT INTO exposureplan VALUES
                (301, 'profile-guid-1', -1.0, 20, 5, 4, 1, 201, 1, 'ep-1');
            """);
        return db;
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static Guid SeedProfile(IntentStore store)
    {
        Guid id = Guid.NewGuid();
        using SqliteCommand cmd = store.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($id, 'Rig A', NULL, 100);";
        cmd.Parameters.AddWithValue("$id", GuidBlob.ToBlob(id));
        cmd.ExecuteNonQuery();
        return id;
    }

    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string NewStorePath() => Path.Combine(NewDir(), "intent.db");

    private static void Exec(string db, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(IntentStore store, string sql) => ScalarWith(store, sql);

    private static object? ScalarWith(IntentStore store, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach ((string name, object value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteScalar();
    }
}
