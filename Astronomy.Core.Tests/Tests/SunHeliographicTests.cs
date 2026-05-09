using System;
using Astronomy.Core.Sun;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunHeliographicTests
    {
        [Fact]
        public void CarringtonRotationNumberAt_2026Era_InPlausibleRange()
        {
            // Carrington rotation count for mid-2020s should be in [2280, 2330].
            // Tabulated values across NASA / NOAA / ESA differ by +/- 1 due to differing
            // epoch conventions; we don't pin to a specific tabulation here. The synodic-
            // step test below verifies the period directly.
            double N = SunHeliographic.CarringtonRotationNumberAt(
                new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc));
            Assert.InRange(N, 2280.0, 2330.0);
        }

        [Fact]
        public void CarringtonRotationNumberAt_Increases_AcrossOneRotation()
        {
            // A 27.2753-day step advances N by exactly 1.
            DateTime t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double n0 = SunHeliographic.CarringtonRotationNumberAt(t0);
            double n1 = SunHeliographic.CarringtonRotationNumberAt(t0.AddDays(27.2753));
            Assert.Equal(1.0, n1 - n0, precision: 6);
        }

        [Fact]
        public void DiskCenterAt_PositionAngleAndB0_WithinExpectedRanges()
        {
            // Sample over a year; the three components must stay in their canonical
            // ranges: P ~+/-26.4 deg, B0 ~+/-7.3 deg, L0 in [0, 360).
            DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 365; day += 7)
            {
                (double P, double B0, double L0) = SunHeliographic.DiskCenterAt(t.AddDays(day));
                Assert.InRange(P, -26.5, 26.5);
                Assert.InRange(B0, -7.3, 7.3);
                Assert.InRange(L0, 0.0, 360.0);
            }
        }

        [Fact]
        public void DiskCenterAt_B0_OscillatesSinusoidallyOverYear()
        {
            // B0 swings approximately +/- 7.25 deg over a year, hitting 0 four times.
            // We just check that the year-sample max and min span >= 12 deg.
            double minB0 = double.PositiveInfinity;
            double maxB0 = double.NegativeInfinity;
            DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 365; day++)
            {
                (_, double B0, _) = SunHeliographic.DiskCenterAt(t.AddDays(day));
                if (B0 < minB0) minB0 = B0;
                if (B0 > maxB0) maxB0 = B0;
            }
            Assert.True(maxB0 - minB0 >= 12.0,
                $"B0 swing {maxB0 - minB0:F2} smaller than expected ~14.5 deg");
        }
    }
}
