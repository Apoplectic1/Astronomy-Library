using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for the magnitude-plus-flag hemisphere convention's
    // sign-takes-precedence-over-flag normalisation in the Location and
    // Target constructors. CLAUDE.md states the rule ("a negative magnitude
    // is flipped positive and the corresponding flag is inverted"); these
    // tests pin it at the type boundary so a future refactor can't silently
    // shift it.
    public class HemisphereNormalisationTests
    {
        // (input latitude, input north flag) -> (stored Latitude, stored North).
        // Sign of the magnitude takes precedence over the explicit flag.
        [Theory]
        [InlineData( 40.0, true,   40.0, true)]   // already canonical
        [InlineData(-40.0, true,   40.0, false)]  // negative magnitude flips flag
        [InlineData( 40.0, false,  40.0, false)]  // already canonical southern
        [InlineData(-40.0, false,  40.0, true)]   // negative magnitude flips back to true
        public void Location_LatitudeSign_NormalisesAgainstNorthFlag(
            double inputLat, bool inputNorth, double expectedLat, bool expectedNorth)
        {
            var loc = MakeLocation(latitude: inputLat, north: inputNorth);
            Assert.Equal(expectedLat, loc.Latitude);
            Assert.Equal(expectedNorth, loc.North);
        }

        // Same shape for longitude / West.
        [Theory]
        [InlineData( 75.0, true,   75.0, true)]
        [InlineData(-75.0, true,   75.0, false)]
        [InlineData( 75.0, false,  75.0, false)]
        [InlineData(-75.0, false,  75.0, true)]
        public void Location_LongitudeSign_NormalisesAgainstWestFlag(
            double inputLon, bool inputWest, double expectedLon, bool expectedWest)
        {
            var loc = MakeLocation(longitude: inputLon, west: inputWest);
            Assert.Equal(expectedLon, loc.Longitude);
            Assert.Equal(expectedWest, loc.West);
        }

        // Target's declination normalisation mirrors Location's latitude rule.
        [Theory]
        [InlineData( 41.27, true,   41.27, true)]
        [InlineData(-41.27, true,   41.27, false)]
        [InlineData( 41.27, false,  41.27, false)]
        [InlineData(-41.27, false,  41.27, true)]
        public void Target_DeclinationSign_NormalisesAgainstNorthFlag(
            double inputDec, bool inputNorth, double expectedDec, bool expectedNorth)
        {
            var t = new Target(
                name:           "test",
                rightAscension: 0.712306,
                declination:    inputDec, north: inputNorth,
                directory:      string.Empty,
                enabled:        true);
            Assert.Equal(expectedDec, t.Declination);
            Assert.Equal(expectedNorth, t.North);
        }

        private static Location MakeLocation(
            double latitude = 40.0, bool north = true,
            double longitude = 75.0, bool west = true)
        {
            return new Location(
                name:         "test",
                latitude:     latitude, north: north,
                longitude:    longitude, west:  west,
                timeZoneInfo: TimeZoneInfo.Utc,
                elevation:    0.0,
                bortleClass:  5,
                extinctionK:  0.28);
        }
    }
}
