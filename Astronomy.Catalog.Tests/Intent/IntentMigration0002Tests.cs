using System.Reflection;
using Astronomy.Catalog.Intent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// Migration 0002 against a POPULATED version-1 store — the R10 table-rebuild proven on real data,
// not just a fresh file: a genuine v1 store (real 0001 script) is populated, closed, and reopened
// through the normal migrate-on-open path; every row and FK link must survive, the relaxed column
// must accept NULL afterward, and the rebuild must leave the project indexes under their names.
public sealed class IntentMigration0002Tests
{
    [Fact]
    public void PopulatedV1Store_MigratesInPlace_DataAndLinksIntact()
    {
        string path = NewStorePath();
        Guid profileId = Guid.NewGuid(), projectId = Guid.NewGuid(), targetId = Guid.NewGuid();

        // ---- Build a genuine version-1 store: apply only the real 0001 script, then populate it.
        using (SqliteConnection db = OpenRaw(path))
        {
            IntentMigrations.Apply(db, [ReadEmbeddedScript(1, "initial")]);
            Assert.Equal(1L, IntentMigrations.ReadUserVersion(db));

            Exec(db, "INSERT INTO profile (id, name, nina_profile_guid, created_at) VALUES ($p, 'Rig A', NULL, 100);",
                ("$p", GuidBlob.ToBlob(profileId)));

            // v1 really is NOT NULL: a project without minimum_time_minutes is refused pre-migration.
            SqliteException notNull = Assert.Throws<SqliteException>(() => Exec(db,
                "INSERT INTO project (id, profile_id, name, state_id, priority_id, minimum_time_minutes, created_at) " +
                "VALUES ($id, $p, 'Refused', 0, 1, NULL, 150);",
                ("$id", GuidBlob.ToBlob(Guid.NewGuid())), ("$p", GuidBlob.ToBlob(profileId))));
            Assert.Contains("minimum_time_minutes", notNull.Message);

            Exec(db,
                "INSERT INTO project (id, profile_id, name, description, state_id, priority_id, minimum_time_minutes, " +
                "created_at, imported_from_ts_guid) VALUES ($id, $p, 'M 31', 'Andromeda', 1, 2, 120, 200, 'p-101');",
                ("$id", GuidBlob.ToBlob(projectId)), ("$p", GuidBlob.ToBlob(profileId)));
            Exec(db,
                "INSERT INTO target (id, project_id, name, ra_hours, dec_degrees_signed, created_at) " +
                "VALUES ($id, $proj, 'M 31', 0.712, 41.269, 300);",
                ("$id", GuidBlob.ToBlob(targetId)), ("$proj", GuidBlob.ToBlob(projectId)));
        }

        // ---- Normal migrate-on-open applies 0002 to the populated store, in place.
        using IntentStore store = IntentStore.Open(path);

        Assert.Equal(2L, IntentMigrations.ReadUserVersion(store.Connection));
        Assert.Equal(1L, Scalar(store.Connection,
            "SELECT count(*) FROM schema_migration WHERE version = 2 AND name = 'minimum_time_nullable';"));

        // Every row survived with values intact; the target -> project FK link still resolves.
        Assert.Equal("Andromeda", Scalar(store.Connection, "SELECT description FROM project WHERE imported_from_ts_guid = 'p-101';"));
        Assert.Equal(120L, Scalar(store.Connection, "SELECT minimum_time_minutes FROM project WHERE imported_from_ts_guid = 'p-101';"));
        Assert.Equal(200L, Scalar(store.Connection, "SELECT created_at FROM project WHERE imported_from_ts_guid = 'p-101';"));
        Assert.Equal("M 31", Scalar(store.Connection,
            "SELECT t.name FROM target t JOIN project p ON p.id = t.project_id WHERE p.imported_from_ts_guid = 'p-101';"));

        // The relaxed column now accepts NULL (NULL = no minimum).
        Exec(store.Connection,
            "INSERT INTO project (id, profile_id, name, state_id, priority_id, minimum_time_minutes, created_at) " +
            "VALUES ($id, $p, 'No minimum', 0, 1, NULL, 400);",
            ("$id", GuidBlob.ToBlob(Guid.NewGuid())), ("$p", GuidBlob.ToBlob(profileId)));
        Assert.Equal(1L, Scalar(store.Connection, "SELECT count(*) FROM project WHERE minimum_time_minutes IS NULL;"));

        // The rebuild recreated the four project indexes under their original names.
        Assert.Equal(4L, Scalar(store.Connection,
            "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'project' AND name LIKE 'ix_project_%';"));

        // And enum CHECKs survived the rebuild (companion constraint, not just the lookup FK).
        Assert.Throws<SqliteException>(() => Exec(store.Connection,
            "INSERT INTO project (id, profile_id, name, state_id, priority_id, created_at) VALUES ($id, $p, 'Bad state', 9, 1, 500);",
            ("$id", GuidBlob.ToBlob(Guid.NewGuid())), ("$p", GuidBlob.ToBlob(profileId))));
    }

    /// <summary>Reads a real embedded migration script by version + name (the library's own resources).</summary>
    private static IntentMigrations.MigrationScript ReadEmbeddedScript(int version, string name)
    {
        Assembly assembly = typeof(IntentMigrations).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(r => r.EndsWith($"{version:0000}_{name}.sql", StringComparison.Ordinal));
        return new IntentMigrations.MigrationScript(version, name, () =>
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        });
    }

    private static string NewStorePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "intent.db");
    }

    private static SqliteConnection OpenRaw(string path)
    {
        SqliteConnection db = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static void Exec(SqliteConnection db, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach ((string name, object value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
