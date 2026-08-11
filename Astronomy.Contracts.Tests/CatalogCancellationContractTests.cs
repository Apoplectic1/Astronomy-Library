using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #26 — cancellation throws; no partial result.
/// </summary>
public sealed class CatalogCancellationContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #26:
    //   "A cancelled Catalog call THROWS; no partial (truncated) result is ever
    //    returned." Every long-running entry point takes an OPTIONAL token and
    //    genuinely observes it — which is exactly why a regression here is
    //    compiler-invisible: nothing breaks if the token silently stops being
    //    observed; the call just starts returning truncated graphs/reports as if
    //    they were complete.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Reader_CancelledToken_ThrowsInsteadOfTruncatedList()
    {
        // The reader observes the token PER ROW of the open data reader, so the pin
        // needs at least one row — with rows present and a cancelled token, the read
        // must throw rather than return however many rows happened to be mapped.
        string db = NewProjectDb();
        try
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();
            using TargetSchedulerReader reader = new(db);

            Assert.ThrowsAny<OperationCanceledException>(() => reader.ReadProjects(cts.Token));
        }
        finally
        {
            Cleanup(db);
        }
    }

    [Fact]
    public void Resolver_CancelledToken_Throws()
    {
        // Resolve checks at each phase boundary (plus per TS target in the anchoring
        // pass), so a pre-cancelled token must throw even before any real work.
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => TargetResolver.Resolve([], TsPlanData.Empty, createdAtUnix: 0, options: null, ct: cts.Token));
    }

    [Fact]
    public async Task Scanner_CancelledToken_Throws()
    {
        string root = Path.Combine(Path.GetTempPath(), $"contract_scan_cancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "M1 - Crab", "Captures"));
        try
        {
            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ImageLibraryScanner.ScanAsync(root, cts.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Minimal TS db for the reader pin: the ctor needs only an existing file (opened
    // read-only + PRAGMA user_version); ReadProjects needs the project table, with one
    // row so the per-row token check is reached.
    private static string NewProjectDb()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ts_cancel_contract_{Guid.NewGuid():N}.db");
        using SqliteConnection c = new($"Data Source={path}");
        c.Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE project (Id INTEGER PRIMARY KEY, profileId TEXT, name TEXT, state INTEGER, " +
            "priority INTEGER, minimumaltitude REAL, isMosaic INTEGER, guid TEXT);" +
            "INSERT INTO project VALUES (1, 'profile-1', 'P1', 1, 0, NULL, 0, 'g-1');";
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearPool(c);
        return path;
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best-effort cleanup of throwaway test artifacts */ }
        }
    }
}
