using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for NightCalculator, focused on edge cases around the UTC-day boundary
    // of astronomical dusk. SunEphemeris.RiseSet returns ONE set event per UTC
    // calendar day; on dates where local evening astronomical dusk straddles 00:00
    // UTC, two -18-deg down-crossings land on the same UTC day and Meeus silently
    // drops one. NightCalculator.BracketingPair detects the dropped-dusk signature
    // (a >18 h "night") and recovers via a backward sweep + bisect from endingDawn.
    // These tests cover the autumn-equinox collision window at Penns Park EDT
    // (around Oct 8-10 each year) -- without the fix, the floor of the year/sessions
    // chart curves would plateau for one day before catching up.
    public class NightCalculatorTests
    {
        // Penns Park (40.28 N, 75.00 W) during EDT: local sunset on Oct 8 is ~6:30
        // PM EDT (~10:30 PM UTC), astronomical dusk is ~8:00 PM EDT (= 00:00 UTC
        // the next day). Two consecutive evenings' dusks therefore both fall on the
        // same UTC calendar day Oct 9 (one ~00:00 UTC, the other ~23:59 UTC). The
        // sweep in this date range exercises the dusk-collision branch.
        [Fact]
        public void StartingDusk_AdvancesEveryNight_AcrossUtcDayCollision()
        {
            var seenDusks = new System.Collections.Generic.List<DateTime>();
            for (int day = 6; day <= 14; day++)
            {
                var seed = new DateTime(2026, 10, day, 21, 0, 0, DateTimeKind.Local);
                var loc = TestLocations.PennsPark;
                var night = NightCalculator.ComputeNight(loc, seed);

                Assert.True(night.IsValid, $"Oct {day}: expected a valid night at Penns Park EDT");

                if (seenDusks.Count > 0)
                {
                    var prev = seenDusks[seenDusks.Count - 1];
                    Assert.True(night.AstronomicalDusk != prev,
                        $"Oct {day}: dusk {night.AstronomicalDusk:O} must differ from prior night's dusk {prev:O} " +
                        "(the dusk-collision bug produced two adjacent rows with bit-identical dusks)");
                }
                seenDusks.Add(night.AstronomicalDusk);
            }
        }

        // A real astronomical night at mid-latitudes is at most ~14 h. The
        // dusk-collision bug produced a 33-hour "night" -- prior evening's dusk
        // paired with the next morning's dawn, with a sunlit day in between. This
        // guard fires regardless of which UTC offset is in play, so it doubles as
        // a regression net for the spring-equinox mirror window and the DST-shifted
        // bands later in the year.
        [Fact]
        public void NightDuration_NeverExceedsRealNight_AcrossUtcDayCollision()
        {
            for (int day = 6; day <= 14; day++)
            {
                var seed = new DateTime(2026, 10, day, 21, 0, 0, DateTimeKind.Local);
                var loc = TestLocations.PennsPark;
                var night = NightCalculator.ComputeNight(loc, seed);

                Assert.True(night.IsValid, $"Oct {day}: expected a valid night");
                var span = night.AstronomicalDawn - night.AstronomicalDusk;
                Assert.True(span > TimeSpan.Zero && span < TimeSpan.FromHours(15),
                    $"Oct {day}: night duration {span} outside the physical range (0, 15 h) " +
                    "-- the dusk-collision bug yielded ~33 h here");
            }
        }
    }
}
