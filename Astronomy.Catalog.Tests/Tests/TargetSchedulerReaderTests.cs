using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class TargetSchedulerReaderTests
{
    // Shared TS working db at Processing\Catalog\TS Database\ (re-copyable BIRDWATCHER snapshot, NINA-nightly schema;
    // read by TS-consuming apps). It is a LIVING db (re-copied as imaging progresses + schema bumps each nightly), so
    // assertions are structural invariants, not exact counts or an exact user_version.
    private const string TsDbPath =
        @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";

    [Fact]
    public void ReadsDevDb_Invariants()
    {
        if (!File.Exists(TsDbPath))
            return; // silent no-op when the dev db isn't present (matches the suite convention)

        using TargetSchedulerReader reader = new(TsDbPath);

        // Schema floor: at least the baseline we support. Newer-than-tested is allowed — the nightly bumps
        // user_version regularly, and the reader proceeds regardless (IsNewerThanTested is only a soft signal).
        Assert.True(reader.SchemaUserVersion >= 24);

        Assert.NotEmpty(reader.ReadProjects());
        Assert.NotEmpty(reader.ReadExposureTemplates());
        Assert.NotEmpty(reader.ReadExposurePlans());
        _ = reader.ReadAcquiredImages();   // smoke: reads without throwing (count varies with imaging)

        IReadOnlyList<TsTarget> targets = reader.ReadTargets();
        Assert.NotEmpty(targets);

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
