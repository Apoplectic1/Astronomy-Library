using System;
using Astronomy.Core;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for TargetGeometry's three-sentinel contract on
    // HourAngleAtAltitude (NaN never-rises, +Infinity circumpolar-above,
    // finite value otherwise). Consumers like RiseSet, CoarseVisibility,
    // and VisibilityWindows.ForScalar branch on these sentinels; treating
    // NaN as a real value silently produces wrong windows. Pin the
    // contract directly here so a refactor can't shift a boundary
    // unnoticed.
    public class TargetGeometryTests
    {
        // (lat, dec, alt) -> sentinel-or-finite. See <returns> on HourAngleAtAltitude.
        [Theory]
        // Never-rises: at lat=40N, target at dec=-70 has meridian altitude
        // 90 - |40-(-70)| = -20 deg; can never reach alt=0.
        [InlineData( 40.0, -70.0, 0.0, double.NaN)]
        // Circumpolar above: at lat=70N, target at dec=80 has lower
        // culmination |70+80|-90 = 60 deg; always above alt=0.
        [InlineData( 70.0,  80.0, 0.0, double.PositiveInfinity)]
        // Same shape mirrored to southern hemisphere.
        [InlineData(-70.0, -80.0, 0.0, double.PositiveInfinity)]
        // Normal crossing: at lat=40N, target at dec=0 (celestial equator)
        // crosses alt=0 at HA = +/- 6 sidereal hours (12-hour arc).
        [InlineData( 40.0,   0.0, 0.0, 6.0)]
        public void HourAngleAtAltitude_HitsCorrectSentinel(
            double latDeg, double decDeg, double altDeg, double expected)
        {
            double ha = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, altDeg);

            if (double.IsNaN(expected))
                Assert.True(double.IsNaN(ha), $"expected NaN, got {ha}");
            else if (double.IsPositiveInfinity(expected))
                Assert.True(double.IsPositiveInfinity(ha), $"expected +Inf, got {ha}");
            else
                Assert.Equal(expected, ha, precision: 6);
        }

        // Closed-form sanity check on MeridianAltitude: at HA=0 the analytic
        // identity is alt = 90 - |lat - dec| (matches when both are in the
        // same hemisphere or close to it). Pin a few standard cases.
        [Theory]
        [InlineData( 40.0,   0.0, 50.0)]    // equator target from 40N
        [InlineData( 40.0,  41.27, 88.73)]  // M31 from 40N -- near zenith
        [InlineData(-40.0,   0.0, 50.0)]    // mirror to 40S
        [InlineData( 40.0,  40.0, 90.0)]    // dec == lat -- exact zenith
        public void MeridianAltitude_MatchesClosedForm(
            double latDeg, double decDeg, double expectedAltDeg)
        {
            double alt = TargetGeometry.MeridianAltitude(latDeg, decDeg);
            Assert.Equal(expectedAltDeg, alt, precision: 2);
        }

        // Cross-check: AltitudeAtHourAngle at HA=0 must equal MeridianAltitude
        // for the same (lat, dec). Tests the two primitives against each other
        // without needing an external reference.
        [Theory]
        [InlineData( 40.0,   0.0)]
        [InlineData( 40.0,  41.27)]
        [InlineData(-40.0, -70.0)]
        [InlineData( 70.0,  80.0)]
        public void AltitudeAtHourAngle_AtTransit_EqualsMeridianAltitude(
            double latDeg, double decDeg)
        {
            double altAtHa0 = TargetGeometry.AltitudeAtHourAngle(0.0, latDeg, decDeg);
            double meridian = TargetGeometry.MeridianAltitude(latDeg, decDeg);
            Assert.Equal(meridian, altAtHa0, precision: 9);
        }
    }
}
