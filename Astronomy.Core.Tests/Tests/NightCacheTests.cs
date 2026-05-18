using System;
using System.Threading;
using Astronomy.Core.Night;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for NightCache: the static date helpers (ComputeYearStartDay /
    // ComputeYearDaysCount) plus the constructor's null / range guards and the
    // cancellation-token path. ComputeYearStartDay had a documented off-by-one
    // pre-2026-05-04 -- it returned the last day of the PRIOR month, shifting
    // year/sessions chart x-axis labels by one bin. These tests pin the fixed
    // contract so the regression cannot return silently.
    public class NightCacheTests
    {
        // ComputeYearStartDay reduces any seed to the first day of its calendar
        // month at the same time-of-day. The off-by-one bug (seed.AddDays(-seed.Day))
        // would have returned the LAST day of the prior month for these cases.
        [Theory]
        [InlineData(2026,  5, 15, 14, 30,  0, 2026,  5,  1)] // mid-month
        [InlineData(2026,  5,  1,  0,  0,  0, 2026,  5,  1)] // already first
        [InlineData(2026,  5, 31, 23, 59, 59, 2026,  5,  1)] // last of month
        [InlineData(2026, 12, 31, 12,  0,  0, 2026, 12,  1)] // year boundary
        [InlineData(2024,  2, 29,  6,  0,  0, 2024,  2,  1)] // leap-day seed
        public void ComputeYearStartDay_ReducesToFirstOfMonth_PreservingTimeOfDay(
            int sy, int sM, int sd, int sh, int sm, int ss,
            int ey, int eM, int ed)
        {
            var seed = new DateTime(sy, sM, sd, sh, sm, ss, DateTimeKind.Utc);
            var startDay = NightCache.ComputeYearStartDay(seed);
            Assert.Equal(new DateTime(ey, eM, ed, sh, sm, ss, DateTimeKind.Utc), startDay);
        }

        // Cache-invalidation logic in downstream consumers (TP's LocationsCacheEquivalent)
        // compares ComputeYearStartDay across seeds with the seed's original Kind preserved;
        // a silent kind flip would defeat that comparison.
        [Theory]
        [InlineData(DateTimeKind.Utc)]
        [InlineData(DateTimeKind.Local)]
        [InlineData(DateTimeKind.Unspecified)]
        public void ComputeYearStartDay_PreservesKind(DateTimeKind kind)
        {
            var seed = new DateTime(2026, 7, 15, 10, 0, 0, kind);
            var startDay = NightCache.ComputeYearStartDay(seed);
            Assert.Equal(kind, startDay.Kind);
        }

        // ComputeYearDaysCount picks up an extra day when the [start, start+1y)
        // window straddles a leap-day Feb 29.
        [Theory]
        [InlineData(2026,  5,  1, 365)] // non-leap window
        [InlineData(2023,  3,  1, 366)] // window straddles 2024-02-29
        [InlineData(2024,  3,  1, 365)] // window starts after 2024-02-29
        public void ComputeYearDaysCount_HandlesLeapBoundary(
            int y, int M, int d, int expected)
        {
            var seed = new DateTime(y, M, d, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(expected, NightCache.ComputeYearDaysCount(seed));
        }

        [Fact]
        public void Ctor_NullLocation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NightCache(null!, DateTime.UtcNow, DateTime.UtcNow, 1));
        }

        [Fact]
        public void Ctor_NegativeYearDaysCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NightCache(TestLocations.PennsPark, DateTime.UtcNow, DateTime.UtcNow, -1));
        }

        // Zero is the degenerate-but-valid case: no per-day entries, just the
        // Starting window for the supplied moment.
        [Fact]
        public void Ctor_ZeroYearDaysCount_BuildsEmptyYear()
        {
            var seed = new DateTime(2026, 5, 1, 21, 0, 0, DateTimeKind.Local);
            var cache = new NightCache(TestLocations.PennsPark, seed, seed, 0);

            Assert.Empty(cache.YearDays);
            Assert.Equal(seed, cache.YearStartDay);
        }

        [Fact]
        public void Ctor_SmallYear_PopulatesYearDaysAndEchoesStart()
        {
            var seed = new DateTime(2026, 5, 1, 21, 0, 0, DateTimeKind.Local);
            var cache = new NightCache(TestLocations.PennsPark, seed, seed, 7);

            Assert.Equal(seed, cache.YearStartDay);
            Assert.Equal(7, cache.YearDays.Count);
            foreach (var nw in cache.YearDays)
            {
                Assert.True(nw.IsValid, "mid-spring Penns Park should always have a valid night");
            }
        }

        // Pre-cancelled token must throw before any per-day work runs -- proves
        // the loop checks ThrowIfCancellationRequested before the next ComputeNight.
        [Fact]
        public void Ctor_AlreadyCancelledToken_Throws()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var seed = new DateTime(2026, 5, 1, 21, 0, 0, DateTimeKind.Local);

            Assert.Throws<OperationCanceledException>(() =>
                new NightCache(TestLocations.PennsPark, seed, seed, 30, cts.Token));
        }
    }
}
