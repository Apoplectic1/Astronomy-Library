using System;
using Astronomy.Core.Locations;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for Location's D/M/S accessor contract and the With(...)
    // round-trip-preserves-fields rule. D/M/S accessors are computed on
    // read (no stored fields, no drift); pin a hand-picked fractional-
    // degree case to catch any future change that re-introduces the
    // hour-vs-degree confusion class.
    public class LocationTests
    {
        // Hand-picked fractional-degree latitude that hits whole-degrees,
        // arc-minutes, and arc-seconds with non-trivial values:
        //   42.295833 deg = 42 deg 17' 45".
        [Fact]
        public void LatDms_DecomposesDegreesMinutesSeconds()
        {
            var loc = MakeLocation(latitude: 42.295833);
            Assert.Equal(42.0, loc.LatDegrees);
            Assert.Equal(17.0, loc.LatMinutes);
            Assert.Equal(45.0, loc.LatSeconds, precision: 1);
        }

        //   74.997369 deg longitude = 74 deg 59' 50.53".
        [Fact]
        public void LonDms_DecomposesDegreesMinutesSeconds()
        {
            var loc = MakeLocation(longitude: 74.997369);
            Assert.Equal(74.0, loc.LonDegrees);
            Assert.Equal(59.0, loc.LonMinutes);
            Assert.Equal(50.53, loc.LonSeconds, precision: 1);
        }

        // D/M/S accessors are non-negative magnitudes -- the hemisphere
        // flag (West) carries the sign separately.
        [Fact]
        public void LatLonDms_AreUnsignedMagnitudes_RegardlessOfHemisphereFlag()
        {
            // Pass already-canonical southern + western hemisphere; the
            // magnitude derivation should ignore the flag entirely.
            var loc = MakeLocation(latitude: 42.295833, north: false,
                                   longitude: 74.997369, west: true);
            Assert.Equal(42.0, loc.LatDegrees);
            Assert.Equal(74.0, loc.LonDegrees);
        }

        // With() with no arguments must reproduce every field.
        [Fact]
        public void With_NoArgs_RoundTripsAllFields()
        {
            var original = MakeLocation(
                latitude: 42.295833, longitude: 74.997369,
                elevation: 80.67, bortleClass: 5, extinctionK: 0.28);
            var copy = original.With();

            Assert.Equal(original.Name,         copy.Name);
            Assert.Equal(original.Latitude,     copy.Latitude);
            Assert.Equal(original.North,        copy.North);
            Assert.Equal(original.Longitude,    copy.Longitude);
            Assert.Equal(original.West,         copy.West);
            Assert.Equal(original.TimeZoneInfo, copy.TimeZoneInfo);
            Assert.Same (original.LocalHorizon, copy.LocalHorizon);
            Assert.Equal(original.Elevation,    copy.Elevation);
            Assert.Equal(original.BortleClass,  copy.BortleClass);
            Assert.Equal(original.ExtinctionK,  copy.ExtinctionK);
        }

        // With() with one field changed preserves all others.
        [Fact]
        public void With_BortleClassOnly_PreservesOtherFields()
        {
            var original = MakeLocation();
            var copy = original.With(bortleClass: 9);

            Assert.Equal(9, copy.BortleClass);
            Assert.Equal(original.Latitude,    copy.Latitude);
            Assert.Equal(original.Longitude,   copy.Longitude);
            Assert.Equal(original.Elevation,   copy.Elevation);
            Assert.Equal(original.ExtinctionK, copy.ExtinctionK);
        }

        private static Location MakeLocation(
            double latitude = 40.282835, bool north = true,
            double longitude = 74.997369, bool west = true,
            double elevation = 80.67, int bortleClass = 5, double extinctionK = 0.28)
        {
            return new Location(
                name:         "test",
                latitude:     latitude, north: north,
                longitude:    longitude, west:  west,
                timeZoneInfo: TimeZoneInfo.Utc,
                elevation:    elevation,
                bortleClass:  bortleClass,
                extinctionK:  extinctionK);
        }
    }
}
