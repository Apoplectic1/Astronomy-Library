using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class TargetSchedulerReaderTests
{
    // The pinned TS snapshot documented in TS_SCHEDULER_INGEST.md (schema user_version 24).
    private const string SnapshotPath =
        @"E:\Projects\VisualStudio\Astronomy\IntervalScheduler\TS DataBase Example\schedulerdb.sqlite";

    // Documented row counts for the pinned snapshot.
    private const int ExpectedProjects = 10;
    private const int ExpectedTargets = 102;
    private const int ExpectedExposurePlans = 662;
    private const int ExpectedAcquiredImages = 1178;

    [Fact]
    public void ReadsPinnedSnapshot_CountsAndInvariants()
    {
        if (!File.Exists(SnapshotPath))
            return; // Silent no-op when the pinned snapshot isn't present (matches the NINA smoke-test convention).

        using TargetSchedulerReader reader = new(SnapshotPath);

        Assert.Equal(TargetSchedulerReader.TestedUserVersion, reader.SchemaUserVersion);
        Assert.False(reader.IsNewerThanTested);

        Assert.Equal(ExpectedProjects, reader.ReadProjects().Count);
        Assert.Equal(ExpectedExposurePlans, reader.ReadExposurePlans().Count);
        Assert.Equal(ExpectedAcquiredImages, reader.ReadAcquiredImages().Count);

        IReadOnlyList<TsTarget> targets = reader.ReadTargets();
        Assert.Equal(ExpectedTargets, targets.Count);

        // Documented invariants: all J2000, coordinates within range.
        Assert.All(targets, t =>
        {
            Assert.Equal(2, t.EpochCode);
            if (t.Ra is double ra)
                Assert.InRange(ra, 0.0, 24.0);
            if (t.Dec is double dec)
                Assert.InRange(dec, -90.0, 90.0);
        });
    }
}
