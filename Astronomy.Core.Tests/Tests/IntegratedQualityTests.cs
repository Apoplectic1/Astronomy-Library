using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for IntegratedQuality.HalvesAroundMidpoint. OverSession (the primitive
    // it composes) is exercised across the existing Session* test suite; no need
    // to retest it here.
    public class IntegratedQualityTests
    {
        private static Location MakeLocation() => TestLocations.PennsPark;

        // sin(alt * pi/180) -- smooth, monotone in altitude. Standard quality proxy.
        private static readonly Func<double, double> SinAltitude =
            alt => Math.Sin(alt * Math.PI / 180.0);

        // Transit-centered session: altitude curve is symmetric around transit (HA = 0),
        // so first and second halves of any quality integral should be ~equal modulo
        // Simpson tolerance.
        [Fact]
        public void HalvesAroundMidpoint_TransitCenteredSession_HalvesAreEqual()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(2);
            DateTime sessionEnd   = transitUtc + TimeSpan.FromHours(2);

            var (first, second) = IntegratedQuality.HalvesAroundMidpoint(
                Target.Default, loc, sessionStart, sessionEnd, SinAltitude);

            Assert.Equal(first, second, 6);
        }

        // Wall-pushed session entirely before transit: altitude monotone increasing
        // toward transit, so quality grows. Second half should integrate higher than
        // first half.
        [Fact]
        public void HalvesAroundMidpoint_PreTransitSession_SecondHalfHigher()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(4);
            DateTime sessionEnd   = transitUtc - TimeSpan.FromMinutes(30);

            var (first, second) = IntegratedQuality.HalvesAroundMidpoint(
                Target.Default, loc, sessionStart, sessionEnd, SinAltitude);

            Assert.True(second > first,
                $"Expected second > first for pre-transit session; got first={first}, second={second}");
        }

        // Halves must approximately sum to the same thing as OverSession over the
        // whole window (modulo Simpson approximation error -- splitting actually
        // gives more segments than one big Simpson, so the sum may be slightly
        // more accurate than the full single-call answer).
        [Fact]
        public void HalvesAroundMidpoint_SumApproximatesFullIntegral()
        {
            var loc = MakeLocation();
            DateTime sessionStart = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            DateTime sessionEnd   = sessionStart.AddHours(3);
            TimeSpan duration = sessionEnd - sessionStart;

            var (first, second) = IntegratedQuality.HalvesAroundMidpoint(
                Target.Default, loc, sessionStart, sessionEnd, SinAltitude);
            double full = IntegratedQuality.OverSession(
                Target.Default, loc, sessionStart, duration, SinAltitude);

            Assert.Equal(full, first + second, 3);
        }

        [Fact]
        public void HalvesAroundMidpoint_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var t0 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var t1 = t0.AddHours(2);

            Assert.Throws<ArgumentNullException>(() =>
                IntegratedQuality.HalvesAroundMidpoint(null, loc, t0, t1, SinAltitude));
            Assert.Throws<ArgumentNullException>(() =>
                IntegratedQuality.HalvesAroundMidpoint(Target.Default, null, t0, t1, SinAltitude));
            Assert.Throws<ArgumentNullException>(() =>
                IntegratedQuality.HalvesAroundMidpoint(Target.Default, loc, t0, t1, null));
        }

        // Non-positive total duration must return (0, 0) consistently with how
        // OverSession handles non-positive durations.
        [Fact]
        public void HalvesAroundMidpoint_NonPositiveDuration_ReturnsZeroes()
        {
            var loc = MakeLocation();
            var t0 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            // end == start
            var (first1, second1) = IntegratedQuality.HalvesAroundMidpoint(
                Target.Default, loc, t0, t0, SinAltitude);
            Assert.Equal(0.0, first1, 12);
            Assert.Equal(0.0, second1, 12);

            // end < start (negative duration on both halves)
            var (first2, second2) = IntegratedQuality.HalvesAroundMidpoint(
                Target.Default, loc, t0.AddHours(1), t0, SinAltitude);
            Assert.Equal(0.0, first2, 12);
            Assert.Equal(0.0, second2, 12);
        }
    }
}
