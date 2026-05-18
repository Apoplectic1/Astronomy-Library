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
    }
}
