using System;
using Astronomy.Core.Sun;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunPowerTests
    {
        [Fact]
        public void ExtraterrestrialIrradianceAt_BoundedAcrossYear()
        {
            // Solar constant ~1361 W/m²; over a year I_0 swings by ±3.3% due to
            // Earth-Sun distance variation -> [~1314, ~1408].
            double minI = double.PositiveInfinity, maxI = double.NegativeInfinity;
            DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int day = 0; day < 365; day += 7)
            {
                double I = SunPower.ExtraterrestrialIrradianceAt(t.AddDays(day));
                if (I < minI) minI = I;
                if (I > maxI) maxI = I;
            }
            Assert.InRange(minI, 1310.0, 1320.0);
            Assert.InRange(maxI, 1400.0, 1415.0);
        }

        [Fact]
        public void ExtraterrestrialIrradianceAt_AtPerihelion_ExceedsAphelion()
        {
            double iPeri = SunPower.ExtraterrestrialIrradianceAt(
                new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));
            double iAph = SunPower.ExtraterrestrialIrradianceAt(
                new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));
            Assert.True(iPeri > iAph, $"perihelion ({iPeri}) > aphelion ({iAph})");
        }

        [Fact]
        public void ClearSkyDirectNormalAt_BelowHorizon_IsZero()
        {
            // Penns Park, 2026-06-21 at UTC 04:00 -- before sunrise on this date.
            double dni = SunPower.ClearSkyDirectNormalAt(
                TestLocations.PennsPark,
                new DateTime(2026, 6, 21, 4, 0, 0, DateTimeKind.Utc));
            Assert.Equal(0.0, dni);
        }

        [Fact]
        public void ClearSkyDirectNormalAt_AtNoon_PositiveAndBelowExtraterrestrial()
        {
            DateTime noon = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            double i0  = SunPower.ExtraterrestrialIrradianceAt(noon);
            double dni = SunPower.ClearSkyDirectNormalAt(TestLocations.PennsPark, noon);
            Assert.True(dni > 0.0);
            Assert.True(dni < i0,
                $"DNI ({dni}) should be < extraterrestrial ({i0}) due to atmospheric attenuation");
            // Plausible clear-sky DNI at solar noon mid-summer mid-latitude: 800-1050 W/m².
            Assert.InRange(dni, 750.0, 1050.0);
        }

        [Fact]
        public void ClearSkyDirectNormalAt_HazyAtmosphereDelivesLessThanClean()
        {
            // Linke turbidity 5 (hazy) should produce less DNI than 2 (clean).
            DateTime noon = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            double dniClean = SunPower.ClearSkyDirectNormalAt(TestLocations.PennsPark, noon, linkeTurbidity: 2.0);
            double dniHazy  = SunPower.ClearSkyDirectNormalAt(TestLocations.PennsPark, noon, linkeTurbidity: 5.0);
            Assert.True(dniClean > dniHazy);
        }

        [Fact]
        public void ClearSkyGlobalHorizontalAt_BelowHorizon_IsZero()
        {
            double ghi = SunPower.ClearSkyGlobalHorizontalAt(
                TestLocations.PennsPark,
                new DateTime(2026, 6, 21, 4, 0, 0, DateTimeKind.Utc));
            Assert.Equal(0.0, ghi);
        }

        [Fact]
        public void ClearSkyGlobalHorizontalAt_AtNoon_Positive()
        {
            DateTime noon = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            double ghi = SunPower.ClearSkyGlobalHorizontalAt(TestLocations.PennsPark, noon);
            Assert.True(ghi > 0.0);
            // GHI on a horizontal panel at solar noon mid-summer mid-latitude:
            // typically 800-1200 W/m² for the simplified Linke-turbidity model
            // (slightly higher than radiative-transfer codes due to the linear
            // diffuse approximation -- documented on the public method).
            Assert.InRange(ghi, 750.0, 1200.0);
        }

        [Fact]
        public void OptimalAnnualTiltDeg_AtFortyDegLat_AroundThirtyThree()
        {
            // Christofides 2002: 0.76 * |lat| + 3.1. At lat=40: 33.5 deg.
            double tilt = SunPower.OptimalAnnualTiltDeg(TestLocations.PennsPark);
            Assert.Equal(33.5, tilt, precision: 0);  // Penns Park is 40.28 N; expect ~33.7
        }

        [Fact]
        public void OptimalSeasonalTiltDeg_AtSummerSolstice_LessThanLatitude()
        {
            // Summer: |lat - dec| with dec ~ +23.4 -> tilt ~ |40.28 - 23.4| = 16.9.
            double tilt = SunPower.OptimalSeasonalTiltDeg(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            Assert.InRange(tilt, 16.0, 18.0);
            Assert.True(tilt < TestLocations.PennsPark.Latitude);
        }

        [Fact]
        public void OptimalSeasonalTiltDeg_AtWinterSolstice_GreaterThanLatitude()
        {
            // Winter: |lat - dec| with dec ~ -23.4 -> tilt ~ |40.28 - (-23.4)| = 63.7.
            double tilt = SunPower.OptimalSeasonalTiltDeg(TestLocations.PennsPark, new DateOnly(2026, 12, 21));
            Assert.InRange(tilt, 63.0, 64.5);
            Assert.True(tilt > TestLocations.PennsPark.Latitude);
        }

        [Fact]
        public void ClearSkyDirectNormalAt_NullLocation_Throws()
        {
            DateTime t = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentNullException>(() =>
                SunPower.ClearSkyDirectNormalAt(null, t));
        }
    }
}
