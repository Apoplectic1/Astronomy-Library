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

        // AzimuthAtHourAngle documents [0, 360) -- half-open. Due north sits on
        // that seam: the cosAz clamp forces exactly 1.0, Acos(1.0) is exactly 0.0,
        // and the eastern-half flip then computes 360.0 - 0.0. Before the fold-back
        // these returned exactly 360.0, out of contract, and nothing downstream
        // renormalised (AltAz's ctor stores it verbatim). Not a pole-only oddity:
        // any target north of the zenith at upper transit hits it.
        [Theory]
        [InlineData(  0.0,  6.0, 90.0)]   // celestial pole from 40N, eastern half
        [InlineData( 40.3,  0.0, 69.07)]  // M81 at upper transit from Penns Park
        [InlineData( 40.3,  0.0, 89.26)]  // Polaris at upper transit
        [InlineData(  0.0,  0.0, 45.0)]   // equator observer, north-of-zenith target
        [InlineData( 89.9,  3.0, 90.0)]   // near-pole observer, pole target
        public void AzimuthAtHourAngle_DueNorth_StaysBelow360(
            double latDeg, double haHours, double decDeg)
        {
            double az = TargetGeometry.AzimuthAtHourAngle(haHours, latDeg, decDeg);

            Assert.InRange(az, 0.0, Math.BitDecrement(360.0));
            Assert.NotEqual(360.0, az);
        }

        // The invariant itself, swept rather than spot-checked: no (HA, lat, dec)
        // may leave [0, 360). The eastern half (HA < 12h) is where the flip runs,
        // so weight the sweep there; include the poles, the equator and the
        // dec == lat zenith case, each of which drives cosAz to the clamp.
        [Fact]
        public void AzimuthAtHourAngle_NeverLeavesHalfOpenRange()
        {
            double[] lats = { -90.0, -40.0, 0.0, 40.0, 40.3, 51.5, 89.9, 90.0 };
            double[] decs = { -90.0, -45.0, 0.0, 40.3, 45.0, 69.07, 89.26, 90.0 };

            foreach (double lat in lats)
            foreach (double dec in decs)
            {
                for (int i = 0; i < 480; i++)
                {
                    double ha = i * 24.0 / 480.0;
                    double az = TargetGeometry.AzimuthAtHourAngle(ha, lat, dec);

                    Assert.False(double.IsNaN(az),
                        $"NaN azimuth at lat={lat}, dec={dec}, ha={ha}");
                    Assert.InRange(az, 0.0, Math.BitDecrement(360.0));
                }
            }
        }
    }
}
