using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Sun;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunSeparationTests
    {
        // A target diametrically opposite the sun on summer solstice -- separation should
        // be ~180 deg.
        private static Target AntiSunTargetSummer()
        {
            // Sun's RA ~ 90 deg ~ 6h on 2026-06-21; opposite target is RA ~ 18h.
            return new Target("Antisolar", rightAscension: 18.0,
                declination: 23.4, north: false, directory: null, enabled: true);
        }

        [Fact]
        public void DegreesAt_AntiSolarTarget_NearOneEighty()
        {
            DateTime t = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            double sep = SunSeparation.DegreesAt(AntiSunTargetSummer(), TestLocations.PennsPark, t);
            Assert.InRange(sep, 175.0, 180.0);
        }

        [Fact]
        public void ObserveAt_DegreesAtAndObserveAtAgree()
        {
            DateTime t = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            Target tgt = AntiSunTargetSummer();
            double sepDirect = SunSeparation.DegreesAt(tgt, TestLocations.PennsPark, t);
            (double sepObserve, _, _) = SunSeparation.ObserveAt(tgt, TestLocations.PennsPark, t);
            Assert.Equal(sepDirect, sepObserve, precision: 9);
        }

        [Fact]
        public void ObserveAt_NullTarget_Throws()
        {
            DateTime t = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentNullException>(() =>
                SunSeparation.ObserveAt(null, TestLocations.PennsPark, t));
        }

        [Fact]
        public void ObserveAt_NullLocation_Throws()
        {
            DateTime t = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentNullException>(() =>
                SunSeparation.ObserveAt(AntiSunTargetSummer(), null, t));
        }

        [Fact]
        public void IntervalsBelowDeg_TargetNearSun_ReturnsAtLeastOneInterval()
        {
            // A "near-sun" target at the sun's approximate position: separation should be
            // small for many hours of the day.
            Target nearSun = new Target("NearSun", rightAscension: 6.0,
                declination: 23.0, north: true, directory: null, enabled: true);
            DateTime start = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            DateTime end   = start.AddHours(24);

            IReadOnlyList<(DateTime Start, DateTime End)> ivs =
                SunSeparation.IntervalsBelowDeg(nearSun, TestLocations.PennsPark, start, end, maxSepDeg: 5.0);

            Assert.NotEmpty(ivs);
            foreach (var iv in ivs)
            {
                Assert.True(iv.Start >= start && iv.End <= end);
                Assert.True(iv.End > iv.Start);
            }
        }

        [Fact]
        public void IntervalsBelowDeg_AntiSunTarget_NoIntervals()
        {
            // An anti-solar target stays ~180 deg away; no interval below 30 deg.
            Target far = AntiSunTargetSummer();
            DateTime start = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            IReadOnlyList<(DateTime Start, DateTime End)> ivs =
                SunSeparation.IntervalsBelowDeg(far, TestLocations.PennsPark, start, start.AddHours(24), maxSepDeg: 30.0);
            Assert.Empty(ivs);
        }

        [Fact]
        public void IntervalsBelowDeg_EndNotAfterStart_Throws()
        {
            Target tgt = AntiSunTargetSummer();
            DateTime start = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SunSeparation.IntervalsBelowDeg(tgt, TestLocations.PennsPark, start, start, 10.0));
        }
    }
}
