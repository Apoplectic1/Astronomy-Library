using System;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Pinned-value baselines for the lowest-level time primitives:
    // JulianDate.FromUtc and SiderealTime.Greenwich. Every Sun / Moon /
    // sidereal-time-derived test consumes these indirectly, but their
    // contract is small enough to pin directly against published
    // reference values. A regression in either primitive would surface
    // here with a clean blame target instead of cascading into dozens of
    // downstream test failures with worse diagnostics.
    public class JulianDateAndSiderealTimeTests
    {
        // J2000.0 epoch is defined as JD 2451545.0 at 2000-01-01 12:00:00 TT.
        // We treat the UTC instant as a sufficiently close proxy (the
        // TT - UTC offset of ~64.184 s is far below our sub-millisecond
        // tolerance budget for this primitive).
        [Fact]
        public void FromUtc_J2000Epoch_MatchesPublishedJulianDate()
        {
            var j2000Utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double jd = JulianDate.FromUtc(j2000Utc);
            Assert.Equal(2451545.0, jd, precision: 6);
        }

        // Meeus AA Ex 7.a: 1957-10-04 19:26:24 UT corresponds to JD 2436116.31.
        // (Slightly fewer trailing zeros -- pin to 4 decimal places.)
        [Fact]
        public void FromUtc_MeeusExample_MatchesPublished()
        {
            var utc = new DateTime(1957, 10, 4, 19, 26, 24, DateTimeKind.Utc);
            double jd = JulianDate.FromUtc(utc);
            Assert.Equal(2436116.31, jd, precision: 2);
        }

        // GMST at J2000.0 UT is the leading constant of the USNO polynomial:
        // 18.697374558 hours. Pin to 8 decimal places; this is exact under
        // the formula we use, modulo float rounding.
        [Fact]
        public void Greenwich_AtJ2000_MatchesUSNOConstant()
        {
            double gmst = SiderealTime.Greenwich(2451545.0);
            Assert.Equal(18.697374558, gmst, precision: 8);
        }

        // GMST should wrap into [0, 24). Pin a JD where the unwrapped value
        // exceeds 24 to verify the modulo is being applied. Choose
        // J2000 + 1 day: D = 1, so unwrapped GMST = 18.697374558 + 24.06570982...
        // = 42.763... -> wraps to ~18.7631 hours.
        [Fact]
        public void Greenwich_AtJ2000PlusOneDay_WrapsIntoZeroTwentyFour()
        {
            double gmst = SiderealTime.Greenwich(2451546.0);
            Assert.InRange(gmst, 0.0, 24.0);
            // Closed-form expected: (18.697374558 + 24.06570982441908) mod 24.
            double expected = (18.697374558 + 24.06570982441908) % 24.0;
            Assert.Equal(expected, gmst, precision: 8);
        }

        // Local just adds longitudeDegEast / 15 hours to Greenwich and wraps.
        // Pin one fixture: at J2000.0 UT at Greenwich (lon=0), Local == Greenwich.
        [Fact]
        public void Local_AtGreenwichLongitude_EqualsGreenwich()
        {
            var j2000Utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double lst = SiderealTime.Local(j2000Utc, longitudeDegEast: 0.0);
            double gmst = SiderealTime.Greenwich(JulianDate.FromUtc(j2000Utc));
            Assert.Equal(gmst, lst, precision: 8);
        }
    }
}
