using Astronomy.Core.Night;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the NightCache year-grid statics — CONSUMERS.md
/// "Semantic assumptions" #7 (call-order / lifecycle class).
/// </summary>
public sealed class NightCacheContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #7:
    //   "Night.NightCache.ComputeYearStartDay/Count are pure statics called before the ctor."
    // TP computes the year-grid anchor + length to decide whether a cached NightCache is
    // reusable BEFORE (and without) constructing one (the construction is the expensive
    // per-night loop). So both must be usable as standalone statics with no instance, and
    // return sane results: the anchor is the 1st of the seed's month at the same kind, and
    // the count is the day-span of exactly one year from that anchor (365 or 366).
    // ---------------------------------------------------------------------------

    [Fact]
    public void ComputeYearStartDay_IsFirstOfSeedMonth_PreservingKind()
    {
        // Called with NO NightCache instance in scope — pure static usable before the ctor.
        var seed = new DateTime(2026, 6, 28, 9, 30, 0, DateTimeKind.Utc);

        DateTime start = NightCache.ComputeYearStartDay(seed);

        Assert.Equal(2026, start.Year);
        Assert.Equal(6, start.Month);
        Assert.Equal(1, start.Day);                      // first of the seed's month (not the last of the prior — the 2026-05-04 off-by-one fix)
        Assert.Equal(DateTimeKind.Utc, start.Kind);      // kind preserved
    }

    [Theory]
    [InlineData(2026, 6, 28, 365)]   // 2026-07..2027-06 spans no Feb-29 → 365
    [InlineData(2024, 1, 15, 366)]   // 2024-01..2025-01 includes the 2024 leap day → 366
    public void ComputeYearDaysCount_IsOneYearSpan(int y, int m, int d, int expected)
    {
        var seed = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);

        int count = NightCache.ComputeYearDaysCount(seed);

        // Consistency with the anchor: count == days from anchor to anchor+1yr.
        DateTime start = NightCache.ComputeYearStartDay(seed);
        Assert.Equal((int)start.AddYears(1).Subtract(start).TotalDays, count);
        Assert.Equal(expected, count);
        Assert.InRange(count, 365, 366);                 // always a sane one-year span
    }
}
