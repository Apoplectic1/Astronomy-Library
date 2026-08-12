using Astronomy.Catalog.Intent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The migration framework's own contract, exercised with throwaway script sets (internal Apply):
// in-order application with log rows, mid-script rollback to the prior version, newer-store abort,
// and user_version always in sync with the schema_migration log.
public sealed class IntentMigrationsTests
{
    [Fact]
    public void FreshStore_MigratesToLatest_LogAndUserVersionAgree()
    {
        using SqliteConnection db = NewDb();
        IntentMigrations.Migrate(db);

        Assert.Equal(IntentMigrations.LatestVersion, IntentMigrations.ReadUserVersion(db));
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM schema_migration WHERE version = 1 AND name = 'initial';"));
        Assert.Equal((long)IntentMigrations.LatestVersion, Scalar(db, "SELECT max(version) FROM schema_migration;"));
    }

    [Fact]
    public void PendingScripts_ApplyInOrder_EachLogged()
    {
        using SqliteConnection db = NewDb();
        IntentMigrations.Apply(db, [
            new IntentMigrations.MigrationScript(1, "a", () => "CREATE TABLE a (x INTEGER);"),
            new IntentMigrations.MigrationScript(2, "b", () => "CREATE TABLE b (y INTEGER);"),
        ]);

        Assert.Equal(2L, IntentMigrations.ReadUserVersion(db));
        Assert.Equal("a,b", Scalar(db, "SELECT group_concat(name) FROM (SELECT name FROM schema_migration ORDER BY version);"));
        Assert.Equal(2L, Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('a', 'b');"));
    }

    [Fact]
    public void ReopenedOlderStore_AppliesOnlyPendingScripts()
    {
        using SqliteConnection db = NewDb();
        IntentMigrations.MigrationScript v1 = new(1, "a", () => "CREATE TABLE a (x INTEGER);");
        IntentMigrations.Apply(db, [v1]);
        IntentMigrations.Apply(db, [v1, new IntentMigrations.MigrationScript(2, "b", () => "CREATE TABLE b (y INTEGER);")]);

        Assert.Equal(2L, IntentMigrations.ReadUserVersion(db));
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM schema_migration WHERE version = 1;"));   // not re-applied
    }

    [Fact]
    public void FailingScript_RollsBack_PriorVersionIntact()
    {
        using SqliteConnection db = NewDb();
        IntentMigrations.MigrationScript v1 = new(1, "a", () => "CREATE TABLE a (x INTEGER);");
        IntentMigrations.Apply(db, [v1]);

        IntentStoreException ex = Assert.Throws<IntentStoreException>(() => IntentMigrations.Apply(db, [
            v1,
            new IntentMigrations.MigrationScript(2, "boom", () => "CREATE TABLE b (y INTEGER); INSERT INTO no_such_table VALUES (1);"),
        ]));

        Assert.Contains("0002_boom", ex.Message);
        Assert.Equal(1L, IntentMigrations.ReadUserVersion(db));
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM sqlite_master WHERE name = 'b';"));       // rolled back
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM schema_migration WHERE version = 2;"));
    }

    [Fact]
    public void ScriptBreakingReferentialIntegrity_FailsAndRollsBack()
    {
        // The framework suspends FK enforcement around scripts (R10 rebuilds need it) but gates
        // every commit on a whole-store foreign_key_check — a dangling reference must roll back.
        using SqliteConnection db = NewDb();
        IntentMigrations.MigrationScript v1 = new(1, "a",
            () => "CREATE TABLE p (id INTEGER PRIMARY KEY); CREATE TABLE c (pid INTEGER REFERENCES p(id)); " +
                  "INSERT INTO p VALUES (1); INSERT INTO c VALUES (1);");
        IntentMigrations.Apply(db, [v1]);

        IntentStoreException ex = Assert.Throws<IntentStoreException>(() => IntentMigrations.Apply(db, [
            v1,
            new IntentMigrations.MigrationScript(2, "dangle", () => "DELETE FROM p;"),
        ]));

        Assert.Contains("foreign_key_check", ex.Message);
        Assert.Equal(1L, IntentMigrations.ReadUserVersion(db));
        Assert.Equal(1L, Scalar(db, "SELECT count(*) FROM p;"));    // rolled back — the parent row is restored
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM schema_migration WHERE version = 2;"));
    }

    [Fact]
    public void NewerStore_AbortsBeforeAnyWrite()
    {
        using SqliteConnection db = NewDb();
        Exec(db, $"PRAGMA user_version = {IntentMigrations.LatestVersion + 1};");

        IntentStoreException ex = Assert.Throws<IntentStoreException>(() => IntentMigrations.Migrate(db));

        Assert.Contains("newer", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, Scalar(db, "SELECT count(*) FROM sqlite_master;"));                        // nothing written
    }

    private static SqliteConnection NewDb()
    {
        string dir = Path.Combine(Path.GetTempPath(), "al-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        SqliteConnection db = new(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(dir, "intent.db"), Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static void Exec(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
