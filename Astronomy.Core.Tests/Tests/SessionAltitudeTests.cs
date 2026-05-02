using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for SessionAltitude.Floor / Ceiling. These are pure-evaluation helpers --
    // no scheduling, no moon awareness -- so the assertions compare against
    // hand-computed AltAz / MeridianAltitude values directly.
    public class SessionAltitudeTests
    {
        private static Location MakeLocation()
            => Location.Default.With(
                dateTime: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        // Transit-centered session: [transit - 1h, transit + 1h]. Altitude is monotone
        // increasing on each half-arc of transit, so endpoints are equally distant
        // from transit and (modulo numerical noise) equally high. Floor = Min equals
        // either endpoint -- assert the helper's answer matches the manual Min.
        [Fact]
        public void Floor_TransitCenteredSession_ReturnsEndpointMin()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(1);
            DateTime sessionEnd   = transitUtc + TimeSpan.FromHours(1);

            double altStart = AltAzCalculator.At(Target.Default, loc, sessionStart).Altitude;
            double altEnd   = AltAzCalculator.At(Target.Default, loc, sessionEnd).Altitude;
            double expected = Math.Min(altStart, altEnd);

            double actual = SessionAltitude.Floor(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(expected, actual, 9);
        }

        // Wall-pushed session: [transit - 4h, transit - 2h]. Altitude is monotone
        // increasing as we approach transit, so altStart < altEnd. Floor = altStart.
        [Fact]
        public void Floor_WallPushedSession_ReturnsLowEndpoint()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(4);
            DateTime sessionEnd   = transitUtc - TimeSpan.FromHours(2);

            double altStart = AltAzCalculator.At(Target.Default, loc, sessionStart).Altitude;
            double altEnd   = AltAzCalculator.At(Target.Default, loc, sessionEnd).Altitude;

            // Sanity: confirm the test setup actually puts the lower altitude at start.
            Assert.True(altStart < altEnd,
                $"Test setup invariant: pre-transit session should have altStart < altEnd. Got {altStart}, {altEnd}");

            double actual = SessionAltitude.Floor(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(altStart, actual, 9);
        }

        // Session straddles transit: ceiling is the meridian altitude (the sky-geometry
        // maximum), not the higher endpoint. Endpoint altitudes are below meridian for
        // any session that doesn't sit exactly at transit, so the helper must detect
        // the transit-in-session case and return TargetGeometry.MeridianAltitude.
        [Fact]
        public void Ceiling_SessionContainsTransit_ReturnsMeridianAltitude()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(1);
            DateTime sessionEnd   = transitUtc + TimeSpan.FromHours(1);

            double latDeg = loc.North ? loc.Latitude : -loc.Latitude;
            double decDeg = Target.Default.North ? Target.Default.Declination : -Target.Default.Declination;
            double expected = TargetGeometry.MeridianAltitude(latDeg, decDeg);

            double actual = SessionAltitude.Ceiling(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(expected, actual, 9);
        }

        // Session entirely before transit: altitude monotone increasing toward transit,
        // so altEnd > altStart. Ceiling = altEnd (the higher endpoint), NOT the
        // meridian altitude (because transit isn't in the session).
        [Fact]
        public void Ceiling_SessionDoesNotContainTransit_ReturnsHigherEndpoint()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            DateTime sessionStart = transitUtc - TimeSpan.FromHours(4);
            DateTime sessionEnd   = transitUtc - TimeSpan.FromHours(2);

            double altStart = AltAzCalculator.At(Target.Default, loc, sessionStart).Altitude;
            double altEnd   = AltAzCalculator.At(Target.Default, loc, sessionEnd).Altitude;

            Assert.True(altEnd > altStart,
                $"Test setup invariant: pre-transit session should have altEnd > altStart. Got {altStart}, {altEnd}");

            double actual = SessionAltitude.Ceiling(Target.Default, loc, sessionStart, sessionEnd);

            Assert.Equal(altEnd, actual, 9);
        }

        [Fact]
        public void Floor_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var t0 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var t1 = t0.AddHours(2);

            Assert.Throws<ArgumentNullException>(() => SessionAltitude.Floor(null, loc, t0, t1));
            Assert.Throws<ArgumentNullException>(() => SessionAltitude.Floor(Target.Default, null, t0, t1));
        }

        [Fact]
        public void Ceiling_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var t0 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var t1 = t0.AddHours(2);

            Assert.Throws<ArgumentNullException>(() => SessionAltitude.Ceiling(null, loc, t0, t1));
            Assert.Throws<ArgumentNullException>(() => SessionAltitude.Ceiling(Target.Default, null, t0, t1));
        }
    }
}
