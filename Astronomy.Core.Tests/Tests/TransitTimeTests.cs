using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for TransitTime.DistanceFromMidpoint. TransitTime.UtcAtOrAfter is
    // exercised across the existing Session* test suite; no need to retest it here.
    public class TransitTimeTests
    {
        private static Location MakeLocation()
            => TestLocations.PennsPark.With(
                dateTime: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        // Tolerance note: TransitTime.UtcAtOrAfter is an analytic LST=RA inversion
        // whose FP precision depends on the searchFromUtc input. The test computes
        // transit once (with one searchFromUtc); DistanceFromMidpoint internally
        // recomputes transit with a different searchFromUtc (the session start).
        // The two computations agree to ~500us in practice, so assertions use
        // millisecond-level tolerance rather than tick-level.

        // Transit-centered window: midpoint IS transit, so distance is ~0.
        [Fact]
        public void DistanceFromMidpoint_TransitCenteredWindow_IsZero()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(1);
            DateTime sessionEnd   = transitUtc + TimeSpan.FromHours(1);

            TimeSpan actual = TransitTime.DistanceFromMidpoint(Target.Default, loc, sessionStart, sessionEnd);

            Assert.True(actual.Duration() < TimeSpan.FromMilliseconds(1),
                $"Expected ~0 (within 1ms), got {actual}");
        }

        // Window starts at transit, runs 2h forward: midpoint is transit + 1h, so
        // distance = transit - midpoint = -1h (transit is BEFORE midpoint).
        [Fact]
        public void DistanceFromMidpoint_WindowStartsAtTransit_ReturnsNegativeHalfDuration()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc;
            DateTime sessionEnd   = transitUtc + TimeSpan.FromHours(2);

            TimeSpan actual = TransitTime.DistanceFromMidpoint(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(-1.0, actual.TotalHours, 5);
        }

        // Window ends at transit, runs 2h backward: midpoint is transit - 1h, so
        // distance = transit - midpoint = +1h (transit is AFTER midpoint).
        [Fact]
        public void DistanceFromMidpoint_WindowEndsAtTransit_ReturnsPositiveHalfDuration()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(2);
            DateTime sessionEnd   = transitUtc;

            TimeSpan actual = TransitTime.DistanceFromMidpoint(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(1.0, actual.TotalHours, 5);
        }

        [Fact]
        public void DistanceFromMidpoint_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var t0 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var t1 = t0.AddHours(2);

            Assert.Throws<ArgumentNullException>(() => TransitTime.DistanceFromMidpoint(null, loc, t0, t1));
            Assert.Throws<ArgumentNullException>(() => TransitTime.DistanceFromMidpoint(Target.Default, null, t0, t1));
        }
    }
}
