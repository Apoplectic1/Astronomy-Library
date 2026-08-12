using Astronomy.Catalog.Intent;
using Astronomy.Catalog.Intent.TsImport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// Round-trip of the lift against the real TS working db (shared BIRDWATCHER snapshot — a LIVING
// db, so assertions are invariants over counts and provenance, not exact numbers). Silent no-op
// when the snapshot isn't present, matching the suite convention.
public sealed class TsImportSnapshotTests
{
    private const string TsDbPath =
        @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";

    [Fact]
    public void SnapshotLift_CountsMatchSource_ProvenanceResolves()
    {
        if (!File.Exists(TsDbPath))
            return; // silent no-op when the dev db isn't present (matches the suite convention)

        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using IntentStore store = IntentStore.Open(Path.Combine(dir, "intent.db"));

        TsImportReport report = TsIntentImporter.Import(TsDbPath, store, DateTimeOffset.UtcNow);

        using SqliteConnection source = new(new SqliteConnectionStringBuilder
        { DataSource = TsDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();

        // Entity counts match direct source queries (store target count = TS targets + one
        // synthesized parent per mosaic project that has panels).
        Assert.Equal(Scalar(source, "SELECT count(*) FROM (SELECT profileId FROM project UNION SELECT profileId FROM exposuretemplate);"), (long)report.Profiles);
        Assert.Equal(Scalar(source, "SELECT count(*) FROM project;"), (long)report.Projects);
        Assert.Equal(Scalar(source, "SELECT count(*) FROM exposuretemplate;"), (long)report.ExposureTemplates);
        Assert.Equal(Scalar(source, "SELECT count(*) FROM exposureplan;"), (long)report.ExposurePlans);
        Assert.Equal(Scalar(source, "SELECT count(*) FROM target;"), (long)(report.Targets - report.MosaicParents));
        Assert.Equal(Scalar(source, "SELECT count(DISTINCT projectid) FROM target WHERE projectid IN (SELECT Id FROM project WHERE isMosaic = 1);"),
            (long)report.MosaicParents);

        Assert.Equal(Scalar(store.Connection, "SELECT count(*) FROM profile;"), (long)report.Profiles);
        Assert.Equal(Scalar(store.Connection, "SELECT count(*) FROM project;"), (long)report.Projects);
        Assert.Equal(Scalar(store.Connection, "SELECT count(*) FROM target;"), (long)report.Targets);
        Assert.Equal(Scalar(store.Connection, "SELECT count(*) FROM exposure_template;"), (long)report.ExposureTemplates);
        Assert.Equal(Scalar(store.Connection, "SELECT count(*) FROM exposure_plan;"), (long)report.ExposurePlans);

        // Every lifted row's provenance GUID resolves in the source (the lift half of
        // lift(project(store)) == store, verifiable per row).
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM project WHERE imported_from_ts_guid IS NULL OR imported_from_ts_guid NOT IN (SELECT guid FROM ts.project);"));
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM target WHERE parent_target_id IS NOT NULL AND (imported_from_ts_guid IS NULL OR imported_from_ts_guid NOT IN (SELECT guid FROM ts.target));"));
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM exposure_template WHERE imported_from_ts_guid IS NULL OR imported_from_ts_guid NOT IN (SELECT guid FROM ts.exposuretemplate);"));
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM exposure_plan WHERE imported_from_ts_guid IS NULL OR imported_from_ts_guid NOT IN (SELECT guid FROM ts.exposureplan);"));

        // Non-mosaic, non-parent targets carry provenance too.
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM target WHERE imported_from_ts_guid IS NOT NULL AND imported_from_ts_guid NOT IN (SELECT guid FROM ts.target);"));

        // Lift-invariant spot-check on a sample: names and desired counts survive by provenance join.
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM project p JOIN ts.project s ON s.guid = p.imported_from_ts_guid WHERE p.name <> s.name;"));
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM exposure_plan ep JOIN ts.exposureplan s ON s.guid = ep.imported_from_ts_guid WHERE ep.desired_count <> s.desired;"));

        // The user's data is all-J2000 today; the map must have landed every row on the library's J2000 = 2.
        Assert.Equal(0L, ScalarStore(store,
            "SELECT count(*) FROM target WHERE imported_from_ts_guid IS NOT NULL AND epoch_id <> 2;"));
    }

    /// <summary>Runs <paramref name="sql"/> on the store with the TS snapshot attached read-only as <c>ts</c>.</summary>
    private static object? ScalarStore(IntentStore store, string sql)
    {
        using (SqliteCommand attach = store.Connection.CreateCommand())
        {
            // Plain-path attach (URI filenames aren't guaranteed on an already-open connection);
            // nothing here writes to the attached snapshot.
            attach.CommandText = "ATTACH DATABASE $path AS ts;";
            attach.Parameters.AddWithValue("$path", TsDbPath);
            attach.ExecuteNonQuery();
        }
        try
        {
            using SqliteCommand cmd = store.Connection.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }
        finally
        {
            using SqliteCommand detach = store.Connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE ts;";
            detach.ExecuteNonQuery();
        }
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
