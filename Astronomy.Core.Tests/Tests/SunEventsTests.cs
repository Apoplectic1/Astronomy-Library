using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Locations;
using Astronomy.Core.Sun;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class SunEventsTests
    {
        [Fact]
        public void RiseSetOn_PennsParkSummer_BothPresentAndUtc()
        {
            // Penns Park lon = -75 deg, so its solar day straddles UTC midnight. On a
            // summer date, the "set" returned by Meeus 15 wraps to the very early UTC day
            // (yesterday's sunset that fell at ~UTC 00:30) while "rise" lands at UTC ~09:30
            // -- both within the requested UTC date but with Set < Rise. We just verify
            // both are present and tagged Utc; the wrap behaviour is intrinsic to Meeus 15
            // and matches the existing AstroUtil contract.
            RiseAndSetEvent ev = SunEvents.RiseSetOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            Assert.NotNull(ev.Rise);
            Assert.NotNull(ev.Set);
            Assert.Equal(DateTimeKind.Utc, ev.Rise.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, ev.Set.Value.Kind);
        }

        [Fact]
        public void RiseSetOn_ElevatedObserver_RiseEarlierThanSeaLevel()
        {
            // Bumping the observer to 10000 m extends the day by ~22 min total
            // (~11 min earlier rise, ~11 min later set).
            DateOnly date = new DateOnly(2026, 6, 21);
            RiseAndSetEvent seaLevel = SunEvents.RiseSetOn(TestLocations.PennsPark, date);
            RiseAndSetEvent high = SunEvents.RiseSetOn(
                TestLocations.PennsPark.With(elevation: 10000.0), date);

            Assert.NotNull(seaLevel.Rise);
            Assert.NotNull(high.Rise);
            Assert.True(high.Rise.Value < seaLevel.Rise.Value,
                $"high-elevation rise ({high.Rise}) should precede sea-level rise ({seaLevel.Rise})");
        }

        [Fact]
        public void CivilTwilightOn_NotElevationCorrected()
        {
            // The -6 deg threshold references the celestial horizontal plane by
            // convention; observer elevation should NOT shift the time.
            DateOnly date = new DateOnly(2026, 6, 21);
            RiseAndSetEvent atZero = SunEvents.CivilTwilightOn(TestLocations.PennsPark, date);
            RiseAndSetEvent atHigh = SunEvents.CivilTwilightOn(
                TestLocations.PennsPark.With(elevation: 10000.0), date);

            Assert.NotNull(atZero.Rise);
            Assert.NotNull(atHigh.Rise);
            // Should match within numeric noise (sub-second).
            TimeSpan delta = (atHigh.Rise.Value - atZero.Rise.Value).Duration();
            Assert.True(delta < TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void AstronomicalTwilightOn_ProducesUtcInstants()
        {
            RiseAndSetEvent ev = SunEvents.AstronomicalTwilightOn(
                TestLocations.PennsPark, new DateOnly(2026, 3, 21));
            if (ev.Rise.HasValue) Assert.Equal(DateTimeKind.Utc, ev.Rise.Value.Kind);
            if (ev.Set.HasValue)  Assert.Equal(DateTimeKind.Utc, ev.Set.Value.Kind);
        }

        [Fact]
        public void CrossingsOn_AtMinusFour_BetweenOfficialAndCivil()
        {
            // Threshold -4 deg sits between official sunrise (-0.833) and civil (-6).
            // Morning rise at -4 should be later than at -6 (sun has to climb higher).
            DateOnly date = new DateOnly(2026, 6, 21);
            RiseAndSetEvent atFour  = SunEvents.CrossingsOn(TestLocations.PennsPark, date, -4.0);
            RiseAndSetEvent atSix   = SunEvents.CivilTwilightOn(TestLocations.PennsPark, date);

            Assert.NotNull(atFour.Rise);
            Assert.NotNull(atSix.Rise);
            Assert.True(atFour.Rise.Value > atSix.Rise.Value);
        }

        [Fact]
        public void TransitOn_HourAngleAtResultIsZero()
        {
            DateTime transit = SunEvents.TransitOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            double ha = SunPosition.HourAngleAt(TestLocations.PennsPark, transit);
            Assert.InRange(ha, -0.001, 0.001);
        }

        [Fact]
        public void TransitOn_AtPennsParkNearLocalSolarNoon()
        {
            // Penns Park lon = -75 deg -> local solar noon ~UTC 17:00 (modulo
            // equation-of-time, which is small near solstice).
            DateTime transit = SunEvents.TransitOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            // Expect near 17:00 UTC, +/- 30 minutes.
            DateTime expected = new DateTime(2026, 6, 21, 17, 0, 0, DateTimeKind.Utc);
            TimeSpan delta = (transit - expected).Duration();
            Assert.True(delta < TimeSpan.FromMinutes(30),
                $"transit {transit:o} too far from expected {expected:o} (delta {delta})");
        }

        [Fact]
        public void NoonAltitudeOn_AtSummerSolstice_MatchesFormula()
        {
            // Penns Park (lat 40.28 N) on summer solstice: noon alt ~ 90 - 40.28 + 23.4 = ~73.1 deg.
            double noonAlt = SunEvents.NoonAltitudeOn(TestLocations.PennsPark, new DateOnly(2026, 6, 21));
            Assert.InRange(noonAlt, 72.5, 73.5);
        }

        [Fact]
        public void DayLengthOn_NearEquinox_NearTwelveHours()
        {
            // March equinox 2026 is around March 20. Day length close to 12h, slightly
            // over due to refraction + disc semi-diameter (sun "rises" when its centre
            // is at -0.833 deg).
            TimeSpan dayLen = SunEvents.DayLengthOn(TestLocations.PennsPark, new DateOnly(2026, 3, 20));
            Assert.InRange(dayLen.TotalHours, 11.9, 12.3);
        }

        [Fact]
        public void NextRise_FromMidday_RollsToNextDay()
        {
            // After today's sunrise at Penns Park, NextRise should land on tomorrow.
            DateTime fromUtc = new DateTime(2026, 6, 21, 18, 0, 0, DateTimeKind.Utc); // mid-afternoon UTC
            DateTime? next = SunEvents.NextRise(TestLocations.PennsPark, fromUtc);

            Assert.NotNull(next);
            Assert.True(next.Value >= fromUtc);
            // Should be within the next 36 hours (next morning).
            Assert.True(next.Value - fromUtc < TimeSpan.FromHours(36));
        }

        [Fact]
        public void EquationOfTimeMinutes_PinnedDates()
        {
            // EoT calibration points (varies by year ±0.5 min):
            //   ~Nov 3:  +16.4 min
            //   ~Feb 11: -14.2 min
            //   ~Apr 15:  ~0
            double eotNov = SunEvents.EquationOfTimeMinutes(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc));
            double eotFeb = SunEvents.EquationOfTimeMinutes(new DateTime(2026, 2, 11, 12, 0, 0, DateTimeKind.Utc));
            double eotApr = SunEvents.EquationOfTimeMinutes(new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc));

            Assert.InRange(eotNov, 15.5, 17.0);
            Assert.InRange(eotFeb, -14.7, -13.5);
            Assert.InRange(eotApr, -0.7, 0.7);
        }

        [Fact]
        public void EquationOfTimeMinutes_BoundedAcrossYear()
        {
            // EoT range across a year is [-15, +17] min approximately. Sample weekly.
            double minE = double.PositiveInfinity, maxE = double.NegativeInfinity;
            for (int day = 0; day < 365; day += 7)
            {
                DateTime t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day);
                double e = SunEvents.EquationOfTimeMinutes(t);
                if (e < minE) minE = e;
                if (e > maxE) maxE = e;
            }
            Assert.InRange(minE, -16.0, -13.0);
            Assert.InRange(maxE,  15.0,  18.0);
        }
    }
}
