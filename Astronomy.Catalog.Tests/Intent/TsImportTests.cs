using Astronomy.Catalog.Intent;
using Astronomy.Catalog.Intent.TsImport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The lift's contract over synthetic TS fixtures: pinned translation maps (the epoch swap above
// all — the SafeEpoch precedent), sentinel→NULL translations, mosaic parent/child reconstruction,
// per-row provenance, empty-store refusal, and all-or-nothing aborts that leave the source
// byte-identical and the store row-free.
public sealed class TsImportTests
{
    private static readonly DateTimeOffset ImportStamp = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);

    [Fact]
    public void EpochMap_IsThePinnedNinaToLibrarySwap()
    {
        // NINA orders JNOW=0, B1950=1, J2000=2; the library orders B1950=0, JNow=1, J2000=2.
        // Only J2000 agrees — by coincidence. A cast here is the SafeEpoch silent-swap.
        Assert.Equal(1, TsImportMaps.Epoch[0]);   // NINA JNOW  -> JNow
        Assert.Equal(0, TsImportMaps.Epoch[1]);   // NINA B1950 -> B1950
        Assert.Equal(2, TsImportMaps.Epoch[2]);   // J2000      -> J2000
        Assert.False(TsImportMaps.Epoch.ContainsKey(3));   // J2050: no store epoch — aborts
    }

    [Fact]
    public void OtherMaps_CoverTheFullTsDomains()
    {
        Assert.Equal([0, 1, 2, 3], TsImportMaps.ProjectState.Keys.Order());     // Draft/Active/Inactive/Closed
        Assert.Equal([0, 1, 2], TsImportMaps.Priority.Keys.Order());            // Low/Normal/High (-1 is a sentinel, not a map entry)
        Assert.Equal([0, 1, 2, 3], TsImportMaps.TwilightLevel.Keys.Order());    // Nighttime/Astronomical/Nautical/Civil
    }

    [Fact]
    public void Lift_TranslatesRemapsAndReconstructs()
    {
        string tsDb = NewTsDb();
        using IntentStore store = IntentStore.Open(NewStorePath());

        TsImportReport report = TsIntentImporter.Import(tsDb, store, ImportStamp);

        // Counts: 1 profile, 2 projects, 3 TS targets + 1 synthesized mosaic parent, 1 template, 2 plans.
        Assert.Equal(new TsImportReport(1, 2, 4, 1, 1, 2), report);

        // Epoch crossed by NAME, not by int: the panels' epochcode 0 (NINA JNOW) lands as store JNow (id 1).
        Assert.Equal("JNow", Scalar(store,
            "SELECT e.name FROM target t JOIN epoch e ON e.id = t.epoch_id WHERE t.imported_from_ts_guid = 't-2';"));
        Assert.Equal("J2000", Scalar(store,
            "SELECT e.name FROM target t JOIN epoch e ON e.id = t.epoch_id WHERE t.imported_from_ts_guid = 't-1';"));

        // Sentinels became NULL: target priority -1, template readoutmode -1, plan exposure -1.0,
        // project minimumaltitude 0.0.
        Assert.Equal(1L, Scalar(store, "SELECT priority_id IS NULL FROM target WHERE imported_from_ts_guid = 't-1';"));
        Assert.Equal(1L, Scalar(store, "SELECT readout_mode IS NULL FROM exposure_template WHERE imported_from_ts_guid = 'et-1';"));
        Assert.Equal(1L, Scalar(store, "SELECT exposure_seconds IS NULL FROM exposure_plan WHERE imported_from_ts_guid = 'ep-1';"));
        Assert.Equal(300.0, Scalar(store, "SELECT exposure_seconds FROM exposure_plan WHERE imported_from_ts_guid = 'ep-2';"));
        Assert.Equal(1L, Scalar(store, "SELECT minimum_altitude_deg IS NULL FROM project WHERE imported_from_ts_guid = 'p-101';"));

        // Mosaic: one parent (no coordinates, no provenance — reconstruction, not a lifted row),
        // panels linked to it; the plain project's target has no parent.
        Assert.Equal(1L, Scalar(store,
            "SELECT count(*) FROM target WHERE parent_target_id IS NULL AND ra_hours IS NULL AND imported_from_ts_guid IS NULL;"));
        Assert.Equal(2L, Scalar(store,
            "SELECT count(*) FROM target t JOIN target p ON p.id = t.parent_target_id WHERE p.name = 'Mosaic - Heart';"));
        Assert.Equal(1L, Scalar(store,
            "SELECT parent_target_id IS NULL FROM target WHERE imported_from_ts_guid = 't-1';"));

        // Provenance: every lifted row carries its TS guid; the profile keeps the NINA profile string.
        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM project WHERE imported_from_ts_guid IS NULL;"));
        Assert.Equal(0L, Scalar(store, "SELECT count(*) FROM exposure_plan WHERE imported_from_ts_guid IS NULL;"));
        Assert.Equal("profile-guid-1", Scalar(store, "SELECT nina_profile_guid FROM profile;"));
    }

    [Fact]
    public void UnmappedEnum_Aborts_NamingTableColumnValueRow()
    {
        string tsDb = NewTsDb();
        Exec(tsDb, "UPDATE target SET epochcode = 3 WHERE Id = 1;");   // J2050: deliberately unmapped
        using IntentStore store = IntentStore.Open(NewStorePath());

        TsImportException ex = Assert.Throws<TsImportException>(() => TsIntentImporter.Import(tsDb, store, ImportStamp));

        Assert.Contains("target", ex.Message);
        Assert.Contains("epochcode", ex.Message);
        Assert.Contains("3", ex.Message);
        Assert.Contains("Id=1", ex.Message);
        AssertStoreEmpty(store);
    }

    [Fact]
    public void MissingRequiredField_Aborts_WithDiagnostics()
    {
        string tsDb = NewTsDb();
        Exec(tsDb, "UPDATE target SET guid = NULL WHERE Id = 1;");
        using IntentStore store = IntentStore.Open(NewStorePath());

        TsImportException ex = Assert.Throws<TsImportException>(() => TsIntentImporter.Import(tsDb, store, ImportStamp));

        Assert.Contains("target", ex.Message);
        Assert.Contains("guid", ex.Message);
        Assert.Contains("required", ex.Message);
        AssertStoreEmpty(store);
    }

    [Fact]
    public void AbortedImport_LeavesSourceByteIdentical()
    {
        string tsDb = NewTsDb();
        Exec(tsDb, "UPDATE target SET epochcode = 3 WHERE Id = 1;");
        byte[] before = File.ReadAllBytes(tsDb);
        using IntentStore store = IntentStore.Open(NewStorePath());

        Assert.Throws<TsImportException>(() => TsIntentImporter.Import(tsDb, store, ImportStamp));

        Assert.Equal(before, File.ReadAllBytes(tsDb));
    }

    [Fact]
    public void CompletedImport_LeavesSourceByteIdentical()
    {
        string tsDb = NewTsDb();
        byte[] before = File.ReadAllBytes(tsDb);
        using IntentStore store = IntentStore.Open(NewStorePath());

        TsIntentImporter.Import(tsDb, store, ImportStamp);

        Assert.Equal(before, File.ReadAllBytes(tsDb));
    }

    [Fact]
    public void NonEmptyStore_IsRefused_AndUnchanged()
    {
        string tsDb = NewTsDb();
        using IntentStore store = IntentStore.Open(NewStorePath());
        TsIntentImporter.Import(tsDb, store, ImportStamp);
        long targetsAfterFirst = (long)Scalar(store, "SELECT count(*) FROM target;")!;

        TsImportException ex = Assert.Throws<TsImportException>(() => TsIntentImporter.Import(tsDb, store, ImportStamp));

        Assert.Contains("empty store", ex.Message);
        Assert.Equal(targetsAfterFirst, Scalar(store, "SELECT count(*) FROM target;"));
    }

    // ---- Fixture: a minimal schedulerdb.sqlite with the real TS DDL's in-scope columns ---------
    // Project 101 is a plain project (one target, epochcode J2000, priority sentinel -1, altitude
    // sentinels 0.0); project 102 is a mosaic with two panel targets carrying epochcode 0 (NINA
    // JNOW — the value the swap map must NOT pass through as-is).

    private static string NewTsDb()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, "schedulerdb.sqlite");
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
                 NULL, NULL, NULL, 1, 0, 0, 0.0, 0, 'p-101'),
                (102, 'profile-guid-1', 'Mosaic - Heart', NULL, 1, 2, 1700000200, NULL, NULL, 60, 25.5, 1, 5.0,
                 30, 2, 3, 1, 1, 0, 80.0, 1, 'p-102');
            INSERT INTO target VALUES
                (1, 'M 31', 1, 0.712, 41.269, 2, NULL, 100.0, 101, NULL, 't-1', -1),
                (2, 'Panel 1', 1, 2.5, 61.5, 0, 15.0, 100.0, 102, NULL, 't-2', 0),
                (3, 'Panel 2', 0, 2.6, 61.7, 0, 15.0, 100.0, 102, NULL, 't-3', 2);
            INSERT INTO exposuretemplate VALUES
                (201, 'profile-guid-1', 'Ha 300s', 'Ha', 100, 50, 1, -1, 1, 1, 60.0, 7, 0.0, 300.0, 0.5, 5.0, -15.0,
                 0, -1, 0, 'et-1');
            INSERT INTO exposureplan VALUES
                (301, 'profile-guid-1', -1.0, 20, 5, 4, 1, 201, 1, 'ep-1'),
                (302, 'profile-guid-1', 300.0, 40, 0, 0, 2, 201, 0, 'ep-2');
            """);
        return db;
    }

    private static void AssertStoreEmpty(IntentStore store)
    {
        Assert.Equal(0L, Scalar(store,
            "SELECT (SELECT count(*) FROM profile) + (SELECT count(*) FROM project) + (SELECT count(*) FROM target) + " +
            "(SELECT count(*) FROM exposure_template) + (SELECT count(*) FROM exposure_plan);"));
    }

    private static string NewStorePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "intent.db");
    }

    private static void Exec(string db, string sql)
    {
        using SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(IntentStore store, string sql)
    {
        using SqliteCommand cmd = store.Connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
