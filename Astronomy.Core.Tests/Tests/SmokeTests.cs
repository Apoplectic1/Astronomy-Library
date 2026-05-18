using System;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Scaffolding smoke tests -- prove the xUnit runner, the ProjectReference to
    // Astronomy.Core, and the Core public surface are all wired. Not the Step-1 correctness
    // audit against Stellarium / NINA TS (ROADMAP.md Step 1); that's a larger follow-up. The
    // "AltitudeAtTransitMatchesMeridianAltitude" fact cross-checks three primitives
    // (TransitTime.UtcAtOrAfter, AltAzCalculator.At, TargetGeometry.MeridianAltitude)
    // against each other without needing an external reference value.
    public class SmokeTests
    {
        [Fact]
        public void TargetDefault_IsM31()
        {
            Target m31 = Target.Default;

            Assert.Equal("M31", m31.Name);
            Assert.True(m31.North);
            Assert.InRange(m31.RightAscension, 0.0, 24.0);
            Assert.True(m31.Declination > 0.0);
        }

        [Fact]
        public void LocationDefault_IsPublicSafe()
        {
            // Location.Default carries neutral, ship-safe placeholder coordinates so the
            // public Library source contains no author-specific values; consumer apps
            // (TargetPlanner, XisfManager, IS / ISP / ISS) override these via their own
            // configuration layers. Tests anchored to specific real-world coordinates use
            // the TestLocations fixtures instead of Location.Default.
            Location loc = Location.Default;

            Assert.Equal("Custom", loc.Name);
            Assert.True(loc.North);
            Assert.True(loc.West);
            Assert.InRange(loc.Latitude, 0.0, 90.0);
            Assert.InRange(loc.Longitude, 0.0, 180.0);
        }

        // At transit (hour angle = 0), altitude equals the meridian altitude
        // exactly per the closed-form identity -- holds at any latitude /
        // longitude, including where the meridian altitude is negative
        // (target never rises). Driven by TestLocations.All so the identity
        // is verified at both hemispheres, the equator, and the polar
        // fringe in one [Theory].
        [Theory]
        [MemberData(nameof(TestLocations.All), MemberType = typeof(TestLocations))]
        public void AltitudeAtTransit_MatchesMeridianAltitude(string locationName, Location location)
        {
            // Pick a stable UTC instant (no DST dance, unambiguous).
            DateTime searchFromUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            Target target = Target.Default;

            DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, searchFromUtc);
            AltAz altaz = AltAzCalculator.At(target, location, transitUtc);

            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double decSigned = target.North   ?  target.Declination : -target.Declination;
            double expectedMeridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);

            // Tolerance is generous to absorb floating-point noise from the
            // LST=RA inversion in TransitTime.
            Assert.True(
                Math.Abs(expectedMeridianAlt - altaz.Altitude) < 1e-6,
                $"[{locationName}] expected meridian altitude {expectedMeridianAlt}, got {altaz.Altitude}");
        }
    }
}
