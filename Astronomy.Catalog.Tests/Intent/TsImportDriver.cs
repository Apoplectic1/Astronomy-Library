using Astronomy.Catalog.Intent;
using Astronomy.Catalog.Intent.TsImport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

/// <summary>
/// The operational lift's runnable home: runs the one-time TS import against REAL paths and prints
/// a lift → verify report (entity counts vs direct source queries, provenance resolution, a
/// lift-invariant spot-check). Gated on env vars so plain <c>dotnet test</c> stays a silent no-op:
/// <c>INTENT_IMPORT_TS_DB</c> = the TS schedulerdb.sqlite to lift from;
/// <c>INTENT_IMPORT_STORE</c> = the intent-store file to create (must not already hold intent).
/// The caller owns the operational window around it (pause file sync, verify, resume).
/// </summary>
public sealed class TsImportDriver
{
    private readonly ITestOutputHelper mOut;

    public TsImportDriver(ITestOutputHelper output) => mOut = output;

    [Fact]
    public void RunLift_AndVerify()
    {
        string? tsDb = Environment.GetEnvironmentVariable("INTENT_IMPORT_TS_DB");
        string? storePath = Environment.GetEnvironmentVariable("INTENT_IMPORT_STORE");
        if (string.IsNullOrWhiteSpace(tsDb) || string.IsNullOrWhiteSpace(storePath) || !File.Exists(tsDb))
        {
            mOut.WriteLine("Skip: INTENT_IMPORT_TS_DB / INTENT_IMPORT_STORE env vars not set (or source missing).");
            return;
        }

        mOut.WriteLine($"Source : {tsDb}");
        mOut.WriteLine($"Store  : {storePath}");

        using IntentStore store = IntentStore.Open(storePath);
        TsImportReport report = TsIntentImporter.Import(tsDb, store, DateTimeOffset.UtcNow);

        mOut.WriteLine($"Lifted : {report.Profiles} profiles, {report.Projects} projects, " +
                       $"{report.Targets} targets ({report.MosaicParents} synthesized mosaic parents), " +
                       $"{report.ExposureTemplates} templates, {report.ExposurePlans} exposure plans");

        // ---- Verify 1: entity counts vs direct source queries.
        using SqliteConnection source = new(new SqliteConnectionStringBuilder
        { DataSource = tsDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        Verify("profile count", Scalar(source, "SELECT count(*) FROM (SELECT profileId FROM project UNION SELECT profileId FROM exposuretemplate);"), report.Profiles);
        Verify("project count", Scalar(source, "SELECT count(*) FROM project;"), report.Projects);
        Verify("target count", Scalar(source, "SELECT count(*) FROM target;"), report.Targets - report.MosaicParents);
        Verify("template count", Scalar(source, "SELECT count(*) FROM exposuretemplate;"), report.ExposureTemplates);
        Verify("exposure-plan count", Scalar(source, "SELECT count(*) FROM exposureplan;"), report.ExposurePlans);

        // ---- Verify 2 + 3: provenance GUIDs resolve in the source; spot-check that named fields
        //      survived the lift (the lift half of lift(project(store)) == store).
        Attach(store, tsDb);
        Verify("dangling project provenance", Scalar(store.Connection,
            "SELECT count(*) FROM project WHERE imported_from_ts_guid NOT IN (SELECT guid FROM ts.project);"), 0);
        Verify("dangling target provenance", Scalar(store.Connection,
            "SELECT count(*) FROM target WHERE imported_from_ts_guid IS NOT NULL AND imported_from_ts_guid NOT IN (SELECT guid FROM ts.target);"), 0);
        Verify("dangling template provenance", Scalar(store.Connection,
            "SELECT count(*) FROM exposure_template WHERE imported_from_ts_guid NOT IN (SELECT guid FROM ts.exposuretemplate);"), 0);
        Verify("dangling exposure-plan provenance", Scalar(store.Connection,
            "SELECT count(*) FROM exposure_plan WHERE imported_from_ts_guid NOT IN (SELECT guid FROM ts.exposureplan);"), 0);
        Verify("project name drift", Scalar(store.Connection,
            "SELECT count(*) FROM project p JOIN ts.project s ON s.guid = p.imported_from_ts_guid WHERE p.name <> s.name;"), 0);
        Verify("target name drift", Scalar(store.Connection,
            "SELECT count(*) FROM target t JOIN ts.target s ON s.guid = t.imported_from_ts_guid WHERE t.name <> s.name;"), 0);
        Verify("desired-count drift", Scalar(store.Connection,
            "SELECT count(*) FROM exposure_plan ep JOIN ts.exposureplan s ON s.guid = ep.imported_from_ts_guid WHERE ep.desired_count <> s.desired;"), 0);

        mOut.WriteLine("Verify : PASS — counts match, provenance resolves, spot-checks clean.");
    }

    private void Verify(string what, object? actual, long expected)
    {
        mOut.WriteLine($"  check: {what} = {actual} (expected {expected})");
        Assert.Equal(expected, actual);
    }

    private static void Attach(IntentStore store, string tsDb)
    {
        using SqliteCommand attach = store.Connection.CreateCommand();
        attach.CommandText = "ATTACH DATABASE $path AS ts;";
        attach.Parameters.AddWithValue("$path", tsDb);
        attach.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
