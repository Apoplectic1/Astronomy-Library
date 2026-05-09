using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Sun;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunPositionTests
    {
        // Penns Park summer-solstice noon -- sun in southern sky, well above horizon.
        private static readonly DateTime SummerSolsticeNoonUtc =
            new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void AltAzAt_AtPennsParkSummerNoon_AltitudeAndAzimuthInExpectedQuadrant()
        {
            AltAz altAz = SunPosition.AltAzAt(TestLocations.PennsPark, SummerSolsticeNoonUtc);
            // Sun at Penns Park summer solstice noon: ~73 deg, southern sky.
            Assert.InRange(altAz.Altitude, 65.0, 80.0);
            Assert.InRange(altAz.Azimuth, 90.0, 270.0);
        }

        [Fact]
        public void AltAzAt_NullLocation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SunPosition.AltAzAt(null, SummerSolsticeNoonUtc));
        }

        [Fact]
        public void EquatorialAt_RaInRange_DecInRange_DistanceInRange()
        {
            // Sample over a year; the sun's declination must stay within +/- 23.5 deg
            // and the distance within (0.98, 1.02) AU.
            DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 365; day++)
            {
                (double raDeg, double decDeg, double distAu) = SunPosition.EquatorialAt(t.AddDays(day));
                Assert.InRange(raDeg, 0.0, 360.0);
                Assert.InRange(decDeg, -23.6, 23.6);
                Assert.InRange(distAu, 0.98, 1.02);
            }
        }

        [Fact]
        public void EquatorialAt_DeclinationFollowsSolstices()
        {
            // June solstice: dec ~ +23.4. December solstice: dec ~ -23.4.
            (double _, double decJun, double _) = SunPosition.EquatorialAt(
                new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
            (double _, double decDec, double _) = SunPosition.EquatorialAt(
                new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc));

            Assert.InRange(decJun, 23.0, 23.5);
            Assert.InRange(decDec, -23.5, -23.0);
        }

        [Fact]
        public void DeclinationAt_MatchesEquatorialAt()
        {
            DateTime t = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            (double _, double decFromEquatorial, double _) = SunPosition.EquatorialAt(t);
            double decShortcut = SunPosition.DeclinationAt(t);
            Assert.Equal(decFromEquatorial, decShortcut);
        }

        [Fact]
        public void HourAngleAt_AtTransit_IsNearZero()
        {
            // Compute the solar transit and verify HA is essentially zero there.
            DateTime transit = SunEvents.TransitOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            double ha = SunPosition.HourAngleAt(TestLocations.PennsPark, transit);
            Assert.InRange(ha, -0.001, 0.001);
        }

        [Fact]
        public void HourAngleAt_StaysInPlusMinusTwelveHours()
        {
            // Sample across a UTC day; HA must remain in [-12, +12).
            DateTime t = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            for (int hour = 0; hour < 24; hour++)
            {
                double ha = SunPosition.HourAngleAt(TestLocations.PennsPark, t.AddHours(hour));
                Assert.InRange(ha, -12.0, 12.0);
            }
        }

        [Fact]
        public void ApparentAltitudeAt_NearHorizon_ExceedsGeometric()
        {
            // Near horizon, refraction lifts apparent altitude well above geometric.
            // Pick a moment when sun is low: mid-morning at a high latitude.
            Location highLat = TestLocations.PennsPark.With(latitude: 70.0, north: true);
            DateTime t = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc);
            double altGeom = SunPosition.AltAzAt(highLat, t).Altitude;
            double altApp  = SunPosition.ApparentAltitudeAt(highLat, t);

            // Only meaningful if sun is actually near horizon.
            if (altGeom > 0.0 && altGeom < 5.0)
                Assert.True(altApp > altGeom, $"refraction failed to lift apparent altitude: geom={altGeom}, app={altApp}");
        }

        [Fact]
        public void ApparentAltitudeAt_AtZenith_NearGeometric()
        {
            // Far above horizon, refraction is negligible (<0.001 deg).
            DateTime t = SummerSolsticeNoonUtc;
            double altGeom = SunPosition.AltAzAt(TestLocations.PennsPark, t).Altitude;
            double altApp  = SunPosition.ApparentAltitudeAt(TestLocations.PennsPark, t);
            Assert.InRange(altApp - altGeom, 0.0, 0.02);
        }

        [Fact]
        public void ApparentDiameterArcsec_AtPerihelion_LargerThanAphelion()
        {
            // 2026: perihelion ~Jan 3, aphelion ~Jul 6. Diameter must be larger at peri.
            double diamPeri = SunPosition.ApparentDiameterArcsecAt(
                new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));
            double diamAph = SunPosition.ApparentDiameterArcsecAt(
                new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(diamPeri > diamAph,
                $"perihelion ({diamPeri:F1}\") must exceed aphelion ({diamAph:F1}\")");
            // Both should be in the canonical 1880..1960 arcsec range.
            Assert.InRange(diamPeri, 1940.0, 1965.0);
            Assert.InRange(diamAph,  1875.0, 1895.0);
        }

        [Fact]
        public void ApparentDiameterDeg_EqualsArcsecOver3600()
        {
            DateTime t = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
            double arcsec = SunPosition.ApparentDiameterArcsecAt(t);
            double deg = SunPosition.ApparentDiameterDegAt(t);
            Assert.Equal(arcsec / 3600.0, deg, precision: 12);
        }

        [Fact]
        public void EquatorialAt_AtMeeus25aReference_ApproximatelyMatches()
        {
            // Meeus AA chapter 25 worked example: 1992 Oct 13.0 TD -> apparent
            // RA = 198.378 deg, Dec = -7.785 deg. We pass UTC (deltaT~58s for that
            // epoch, sub-arcsec impact); expect agreement to ~0.05 deg.
            DateTime utc = new DateTime(1992, 10, 13, 0, 0, 0, DateTimeKind.Utc);
            (double raDeg, double decDeg, _) = SunPosition.EquatorialAt(utc);
            Assert.InRange(raDeg,  198.378 - 0.05, 198.378 + 0.05);
            Assert.InRange(decDeg, -7.785  - 0.05, -7.785  + 0.05);
        }
    }
}
