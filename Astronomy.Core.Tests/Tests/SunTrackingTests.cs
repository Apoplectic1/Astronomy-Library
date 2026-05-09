using System;
using System.Collections.Generic;
using Astronomy.Core.Sun;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunTrackingTests
    {
        private static readonly DateTime SummerNoonUtc =
            new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);

        // ---------------- AngularRateAt ----------------

        [Fact]
        public void AngularRateAt_AtTransit_AltRateNearZero()
        {
            // At solar transit the sun is at maximum altitude -- altitude rate must be
            // ~0. Azimuth rate is large (sun crossing meridian).
            DateTime transit = SunEvents.TransitOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            (double altRate, double azRate) = SunTracking.AngularRateAt(TestLocations.PennsPark, transit);

            Assert.InRange(altRate, -1e-4, 1e-4);
            // Az rate magnitude should be substantially larger than alt rate at transit.
            Assert.True(Math.Abs(azRate) > 1e-3);
        }

        [Fact]
        public void AngularRateAt_BoundedAroundMeanSolarMotion()
        {
            // Sun moves ~360 deg per ~24 hours = ~0.0042 deg/sec mean angular speed.
            // Per-component rates must remain within a reasonable multiple of that.
            for (int hour = 0; hour < 24; hour += 2)
            {
                DateTime t = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc).AddHours(hour);
                (double altRate, double azRate) = SunTracking.AngularRateAt(TestLocations.PennsPark, t);
                Assert.InRange(altRate, -0.01, 0.01);
                // Azimuth rate can spike during slow drift near horizon -- generous bound.
                Assert.InRange(azRate, -0.05, 0.05);
            }
        }

        // ---------------- Schedule ----------------

        [Fact]
        public void Schedule_FirstSampleAtStart_LastSampleWithinStep()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            DateTime end   = start.AddHours(1);
            TimeSpan step  = TimeSpan.FromMinutes(1);

            IReadOnlyList<(DateTime Utc, AltAz Pos)> sched =
                SunTracking.Schedule(TestLocations.PennsPark, start, end, step);

            Assert.True(sched.Count > 0);
            Assert.Equal(start, sched[0].Utc);
            Assert.True(sched[sched.Count - 1].Utc <= end);
            Assert.True(sched[sched.Count - 1].Utc.Add(step) > end);
        }

        [Fact]
        public void Schedule_TimestampsMonotonic()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            IReadOnlyList<(DateTime Utc, AltAz Pos)> sched =
                SunTracking.Schedule(TestLocations.PennsPark, start, start.AddHours(2), TimeSpan.FromMinutes(5));

            for (int i = 1; i < sched.Count; i++)
                Assert.True(sched[i].Utc > sched[i - 1].Utc);
        }

        [Fact]
        public void Schedule_LengthMatchesExpected_OneHourAtSixtySecond()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            IReadOnlyList<(DateTime Utc, AltAz Pos)> sched =
                SunTracking.Schedule(TestLocations.PennsPark, start, start.AddHours(1), TimeSpan.FromSeconds(60));
            // 0..3600 sec at 60s step inclusive of both ends -> 61 entries.
            Assert.Equal(61, sched.Count);
        }

        [Fact]
        public void Schedule_StepBelowOneSecond_Throws()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SunTracking.Schedule(TestLocations.PennsPark, start, start.AddHours(1), TimeSpan.FromMilliseconds(500)));
        }

        [Fact]
        public void Schedule_EndNotAfterStart_Throws()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SunTracking.Schedule(TestLocations.PennsPark, start, start, TimeSpan.FromMinutes(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SunTracking.Schedule(TestLocations.PennsPark, start, start.AddSeconds(-1), TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void Schedule_NullLocation_Throws()
        {
            DateTime start = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentNullException>(() =>
                SunTracking.Schedule(null, start, start.AddHours(1), TimeSpan.FromMinutes(1)));
        }

        // ---------------- AirMassKastenYoung ----------------

        [Fact]
        public void AirMassKastenYoung_AtZenith_IsOne()
        {
            Assert.Equal(1.0, SunTracking.AirMassKastenYoung(90.0), precision: 6);
        }

        [Fact]
        public void AirMassKastenYoung_AtThirty_NearTwo()
        {
            // Kasten-Young at 30 deg: ~1.9939.
            double am = SunTracking.AirMassKastenYoung(30.0);
            Assert.InRange(am, 1.99, 2.00);
        }

        [Fact]
        public void AirMassKastenYoung_AtHorizon_FiniteLargePositive()
        {
            double am = SunTracking.AirMassKastenYoung(0.0);
            Assert.InRange(am, 37.0, 39.0);
        }

        [Fact]
        public void AirMassKastenYoung_BelowHorizon_PositiveInfinity()
        {
            Assert.Equal(double.PositiveInfinity, SunTracking.AirMassKastenYoung(-1.0));
            Assert.Equal(double.PositiveInfinity, SunTracking.AirMassKastenYoung(-30.0));
        }

        [Fact]
        public void AirMassKastenYoung_StrictlyDecreasingWithAltitude()
        {
            // Stops at alt=89: the >=90 short-circuit returns exactly 1.0, while the
            // formula's natural value at alt=89 is ~0.99986. The non-monotonic step from
            // formula->short-circuit at the boundary is intentional (the formula's small
            // residual at zenith is a curve-fit artifact; we honour the textbook
            // AM(zenith)=1).
            double prev = double.PositiveInfinity;
            for (double alt = 0.0; alt <= 89.0; alt += 1.0)
            {
                double am = SunTracking.AirMassKastenYoung(alt);
                Assert.True(am <= prev, $"non-monotonic at alt={alt}: prev={prev}, am={am}");
                prev = am;
            }
        }
    }
}
