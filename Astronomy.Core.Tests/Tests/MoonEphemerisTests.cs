using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Correctness guard for MoonEphemeris.Sample: per-sample fields must match
    // the underlying AstroUtil / MoonAvoidance / SkyBrightness primitives that
    // downstream consumers would call inline. The wrapper exists to centralize
    // the per-minute compute; tests verify it stays in lockstep with the
    // primitives.
    public class MoonEphemerisTests
    {
        [Theory]
        [InlineData(60)]
        [InlineData(360)]
        [InlineData(720)]
        public void Sample_Count_MatchesAndContents(int count)
        {
            Location loc = TestLocations.PennsPark;
            DateTime startUtc = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
            TimeSpan step = TimeSpan.FromMinutes(1);

            var samples = MoonEphemeris.Sample(loc, startUtc, step, count);

            Assert.Equal(count, samples.Count);

            // Spot-check first / mid / last sample fields against direct primitive calls.
            int[] picks = { 0, count / 2, count - 1 };
            double latDeg = loc.North ? +loc.Latitude  : -loc.Latitude;
            double lonEast = loc.West ? -loc.Longitude :  loc.Longitude;
            var observer = new ObserverInfo(latDeg, lonEast, loc.Elevation);

            foreach (int i in picks)
            {
                DateTime t = startUtc + TimeSpan.FromTicks(step.Ticks * i);
                MoonSample s = samples[i];

                (double expAlt, double expAz) = AstroUtil.GetMoonAltAz(t, observer);
                Assert.True(Math.Abs(expAlt - s.AltDegGeometric) < 1e-9,
                    $"i={i}: Alt expected {expAlt}, got {s.AltDegGeometric}");
                Assert.True(Math.Abs(expAz - s.AzDeg) < 1e-9,
                    $"i={i}: Az expected {expAz}, got {s.AzDeg}");

                double expAge = LunarAge.DaysAt(t);
                Assert.True(Math.Abs(expAge - s.AgeDays) < 1e-9,
                    $"i={i}: AgeDays expected {expAge}, got {s.AgeDays}");

                double expIllum = AstroUtil.GetMoonIllumination(t);
                Assert.True(Math.Abs(expIllum - s.IlluminatedFrac) < 1e-9,
                    $"i={i}: IlluminatedFrac expected {expIllum}, got {s.IlluminatedFrac}");

                // Apparent altitude = geometric + Saemundsson refraction (>= 0).
                Assert.True(s.AltDegApparent >= s.AltDegGeometric - 1e-12,
                    $"i={i}: Apparent {s.AltDegApparent} should be >= Geometric {s.AltDegGeometric}");

                // Distance positive and within plausible Earth-Moon range.
                Assert.InRange(s.DistanceKm, 350_000, 410_000);
            }
        }

        [Fact]
        public void Sample_NightOverload_DelegatesToCountForm()
        {
            Location loc = TestLocations.PennsPark;
            DateTime dusk = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
            DateTime dawn = new DateTime(2026, 11, 16,  4, 0, 0, DateTimeKind.Utc);
            NightWindow night = new NightWindow
            {
                AstronomicalDusk = dusk,
                AstronomicalDawn = dawn,
                LunarIlluminationFraction = 0.5,
            };

            var nightSamples = MoonEphemeris.Sample(loc, night, TimeSpan.FromMinutes(1));

            // (dawn - dusk) / 1 min + 1 = 361 samples.
            Assert.Equal(361, nightSamples.Count);

            // First sample should equal the per-minute call at dusk.
            double latDeg = loc.North ? +loc.Latitude  : -loc.Latitude;
            double lonEast = loc.West ? -loc.Longitude :  loc.Longitude;
            var observer = new ObserverInfo(latDeg, lonEast, loc.Elevation);
            (double expAlt, double expAz) = AstroUtil.GetMoonAltAz(dusk, observer);
            Assert.True(Math.Abs(expAlt - nightSamples[0].AltDegGeometric) < 1e-9);
            Assert.True(Math.Abs(expAz - nightSamples[0].AzDeg) < 1e-9);
        }

        [Fact]
        public void Sample_InvalidNight_ReturnsEmpty()
        {
            Location loc = TestLocations.PennsPark;
            NightWindow polar = default; // Invalid (DateTime.MinValue bounds).

            var samples = MoonEphemeris.Sample(loc, polar, TimeSpan.FromMinutes(1));

            Assert.Empty(samples);
        }

        [Fact]
        public void Sample_CountZero_ReturnsEmpty()
        {
            Location loc = TestLocations.PennsPark;
            var samples = MoonEphemeris.Sample(
                loc, new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), 0);
            Assert.Empty(samples);
        }

        [Fact]
        public void Sample_NegativeCount_Throws()
        {
            Location loc = TestLocations.PennsPark;
            Assert.Throws<ArgumentException>(() => MoonEphemeris.Sample(
                loc, new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), -1));
        }

        [Fact]
        public void Sample_NonPositiveStep_Throws()
        {
            Location loc = TestLocations.PennsPark;
            Assert.Throws<ArgumentException>(() => MoonEphemeris.Sample(
                loc, new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.Zero, 10));
        }
    }
}
