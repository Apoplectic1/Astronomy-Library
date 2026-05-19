using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Moon;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Round-trip contract guards for AstroUtil's NINA-mirror moon surfaces
    // (GetMoonRiseAndSet, GetMoonPhaseName). These public methods exist solely
    // so downstream consumers ported from NINA's Astrometry namespace can drop
    // Astronomy.Core types in unchanged; they have zero internal callers in
    // the Library. Without explicit tests, a refactor of the Meeus path or the
    // synodic-phase bucketing could silently break the port contract.
    public class AstroUtilMoonTests
    {
        // Reference new moon: 2000-01-06 18:14 UTC, matches
        // LunarAge.NewMoonReferenceJd to sub-second precision. The phase
        // bucketing puts ages < half-bucket-width (~1.85 d) into "New Moon".
        [Fact]
        public void GetMoonPhaseName_AtNewMoonReference_ReturnsNewMoon()
        {
            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            Assert.Equal("New Moon", AstroUtil.GetMoonPhaseName(newMoon));
        }

        // Quarter-period strides walk through the four cardinal phases.
        [Theory]
        [InlineData(0.00, "New Moon")]
        [InlineData(0.25, "First Quarter")]
        [InlineData(0.50, "Full Moon")]
        [InlineData(0.75, "Last Quarter")]
        public void GetMoonPhaseName_AtCardinalFractions_ReturnsCardinalNames(
            double cyclesFromNew, string expectedName)
        {
            var instant = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)
                .AddDays(cyclesFromNew * LunarAge.SynodicMonthDays);
            Assert.Equal(expectedName, AstroUtil.GetMoonPhaseName(instant));
        }

        // Walking a full synodic period in fine steps must only ever yield the
        // eight canonical names -- any other string is a bucketing bug.
        [Fact]
        public void GetMoonPhaseName_AcrossSynodicCycle_ReturnsOnlyCanonicalNames()
        {
            var canonical = new[]
            {
                "New Moon", "Waxing Crescent", "First Quarter", "Waxing Gibbous",
                "Full Moon", "Waning Gibbous", "Last Quarter", "Waning Crescent"
            };

            var reference = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            for (double t = 0; t < LunarAge.SynodicMonthDays; t += 0.1)
            {
                string name = AstroUtil.GetMoonPhaseName(reference.AddDays(t));
                Assert.Contains(name, canonical);
            }
        }

        // GetMoonRiseAndSet is a thin wrapper around MoonPosition.RiseSet that
        // applies the elevation-corrected h0 = 0.125 - HorizonDipDeg term.
        // Pin a typical date+location case so a future refactor of either side
        // can't silently break the wrapper shape.
        [Fact]
        public void GetMoonRiseAndSet_AtMidLatitude_PopulatesBothEvents()
        {
            // Penns Park: 40.28N, 74.997W (east-positive longitude = -74.997).
            // 2026-05-18 is mid-waxing-crescent (new moon was May 16), so both
            // rise and set fall reliably within the calendar-day search window.
            var dateUtc = new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);
            var ev = AstroUtil.GetMoonRiseAndSet(
                dateUtc, latDeg: 40.282835, lonEastDeg: -74.997369, elevationM: 0.0);

            Assert.NotNull(ev.Rise);
            Assert.NotNull(ev.Set);
            Assert.True(Math.Abs((ev.Rise.Value - dateUtc).TotalHours) < 36,
                $"rise {ev.Rise:O} too far from input {dateUtc:O}");
            Assert.True(Math.Abs((ev.Set.Value - dateUtc).TotalHours) < 36,
                $"set {ev.Set:O} too far from input {dateUtc:O}");
        }

        // Elevation parameter must shift rise earlier and set later (horizon
        // dip lowers the effective horizon, so the moon clears it sooner and
        // disappears below it later). 1000 m gives ~3-4 min of shift, well
        // above DateTime tick noise.
        [Fact]
        public void GetMoonRiseAndSet_ElevationShiftsRiseEarlierAndSetLater()
        {
            var dateUtc = new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);
            var seaLevel = AstroUtil.GetMoonRiseAndSet(
                dateUtc, 40.282835, -74.997369, elevationM: 0.0);
            var elevated = AstroUtil.GetMoonRiseAndSet(
                dateUtc, 40.282835, -74.997369, elevationM: 1000.0);

            Assert.NotNull(seaLevel.Rise);
            Assert.NotNull(seaLevel.Set);
            Assert.NotNull(elevated.Rise);
            Assert.NotNull(elevated.Set);

            Assert.True(elevated.Rise.Value < seaLevel.Rise.Value,
                $"elevated rise {elevated.Rise:O} should be earlier than sea-level {seaLevel.Rise:O}");
            Assert.True(elevated.Set.Value > seaLevel.Set.Value,
                $"elevated set {elevated.Set:O} should be later than sea-level {seaLevel.Set:O}");
        }

        // GetMoonRiseAndSetForNight bracket convention: scan 3 UTC days, return
        // (latest rise <= dawn, earliest set >= dusk). Penns Park night of 2026-05-18
        // (waxing crescent, ~7% illum) has moon up at dusk and setting shortly after,
        // so the relevant set falls inside the night window itself. The old day-based
        // GetMoonRiseAndSet called with an evening UTC instant returns yesterday's
        // set instead (today's set lands on UTC May 19, not UTC May 18).
        [Fact]
        public void GetMoonRiseAndSetForNight_WaxingCrescent_SetFallsWithinNightWindow()
        {
            var duskUtc = new DateTime(2026, 5, 19, 2, 5, 0, DateTimeKind.Utc);   // 22:05 EDT May 18
            var dawnUtc = new DateTime(2026, 5, 19, 7, 47, 0, DateTimeKind.Utc);  // 03:47 EDT May 19

            var ev = AstroUtil.GetMoonRiseAndSetForNight(
                duskUtc, dawnUtc, latDeg: 40.282835, lonEastDeg: -74.997369, elevationM: 0.0);

            Assert.NotNull(ev.Set);
            Assert.True(ev.Set.Value >= duskUtc,
                $"set {ev.Set:O} earlier than dusk {duskUtc:O} -- bracket rule violated");
            Assert.True(ev.Set.Value < duskUtc.AddDays(1),
                $"set {ev.Set:O} more than 24h after dusk {duskUtc:O}");
        }

        [Fact]
        public void GetMoonRiseAndSetForNight_WaxingCrescent_RisePrecedesNight()
        {
            // For a waxing crescent, the moon rose ~mid-morning the same local day
            // and is descending toward set during the night -- so the bracket rule
            // returns this-morning's rise (latest rise <= dawn).
            var duskUtc = new DateTime(2026, 5, 19, 2, 5, 0, DateTimeKind.Utc);
            var dawnUtc = new DateTime(2026, 5, 19, 7, 47, 0, DateTimeKind.Utc);

            var ev = AstroUtil.GetMoonRiseAndSetForNight(
                duskUtc, dawnUtc, 40.282835, -74.997369, 0.0);

            Assert.NotNull(ev.Rise);
            Assert.True(ev.Rise.Value <= dawnUtc,
                $"rise {ev.Rise:O} after dawn {dawnUtc:O} -- bracket rule violated");
            // The rise that put the moon up for this night is the prior local
            // morning's rise -- well before dusk, but the latest <= dawn.
            Assert.True(ev.Rise.Value < duskUtc,
                $"waxing-crescent rise {ev.Rise:O} should precede dusk {duskUtc:O}");
        }

        // The whole point of the For-Night variant: its set is the one DURING the
        // night, not the prior UTC-day's evening set. Direct A/B against the legacy
        // calendar-day API pinned to the same evening's UTC instant.
        [Fact]
        public void GetMoonRiseAndSetForNight_PicksLaterSetThanCalendarDayApi()
        {
            // 23:36 UTC May 18 = 19:36 EDT May 18, the failure case from the
            // observation dialog feedback.
            var observerUtc = new DateTime(2026, 5, 18, 23, 36, 0, DateTimeKind.Utc);
            var duskUtc = new DateTime(2026, 5, 19, 2, 5, 0, DateTimeKind.Utc);
            var dawnUtc = new DateTime(2026, 5, 19, 7, 47, 0, DateTimeKind.Utc);

            var legacy = AstroUtil.GetMoonRiseAndSet(
                observerUtc, 40.282835, -74.997369, 0.0);
            var bracket = AstroUtil.GetMoonRiseAndSetForNight(
                duskUtc, dawnUtc, 40.282835, -74.997369, 0.0);

            Assert.NotNull(legacy.Set);
            Assert.NotNull(bracket.Set);
            Assert.True(bracket.Set.Value > legacy.Set.Value,
                $"bracket-set {bracket.Set:O} should be later than calendar-day-set " +
                $"{legacy.Set:O} (legacy returns the prior local evening's set)");
            Assert.True(bracket.Set.Value > observerUtc,
                $"bracket-set {bracket.Set:O} must be after observer instant {observerUtc:O} " +
                "(tonight's set, not yesterday's)");
        }

        [Fact]
        public void GetMoonRiseAndSetForNight_MinValueWindow_ReturnsNullEvents()
        {
            // NightWindow uses DateTime.MinValue as the "no astronomical night here"
            // sentinel (polar summer). The method must short-circuit rather than
            // throwing OverflowException on MinValue.AddDays(-1).
            var ev = AstroUtil.GetMoonRiseAndSetForNight(
                DateTime.MinValue, DateTime.MinValue, 40.0, -75.0, 0.0);
            Assert.Null(ev.Rise);
            Assert.Null(ev.Set);
        }
    }
}
