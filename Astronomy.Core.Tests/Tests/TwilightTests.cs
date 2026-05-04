using Astronomy.Core.Brightness;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct tests for Twilight.ZenithBrightening. The class-doc calibration
    // points (-18, -12, -6, 0) anchor the quadratic-fit ramp; SkyBrightness
    // tests cover the composed K-S path but not the standalone ramp.
    public class TwilightTests
    {
        [Fact]
        public void ZenithBrightening_AstronomicalThreshold_IsZero()
        {
            // Sun_alt = -18 (astronomical twilight) is the dark-sky baseline:
            // no contribution.
            Assert.Equal(0.0, Twilight.ZenithBrightening(-18.0));
        }

        [Fact]
        public void ZenithBrightening_BelowAstronomical_IsZero()
        {
            Assert.Equal(0.0, Twilight.ZenithBrightening(-25.0));
            Assert.Equal(0.0, Twilight.ZenithBrightening(-90.0));
        }

        [Fact]
        public void ZenithBrightening_NauticalTwilight_NearOne()
        {
            // Sun_alt = -12 -> ~1.1 mag brighter per the class-doc calibration.
            // Quadratic fit: ((alt + 18)/18)^2 * 10 = (6/18)^2 * 10 = 1.111...
            Assert.Equal(10.0 / 9.0, Twilight.ZenithBrightening(-12.0), precision: 12);
        }

        [Fact]
        public void ZenithBrightening_CivilTwilight_NearFourPointFour()
        {
            // Sun_alt = -6 -> ~4.4 mag brighter per the class-doc calibration.
            // Quadratic fit: ((-6 + 18)/18)^2 * 10 = (12/18)^2 * 10 = 4.444...
            Assert.Equal(40.0 / 9.0, Twilight.ZenithBrightening(-6.0), precision: 12);
        }

        [Fact]
        public void ZenithBrightening_HorizonAndAbove_Saturates()
        {
            // sun_alt >= 0 saturates at 12 mag (full daylight).
            Assert.Equal(12.0, Twilight.ZenithBrightening(0.0));
            Assert.Equal(12.0, Twilight.ZenithBrightening(45.0));
            Assert.Equal(12.0, Twilight.ZenithBrightening(90.0));
        }

        [Fact]
        public void ZenithBrightening_IsMonotonicAcrossTheRamp()
        {
            // Across the (-18, 0) ramp, brightening must strictly increase as the
            // sun rises -- no bumps from the quadratic fit.
            double prev = -1.0;
            for (double alt = -18.0; alt <= 0.0; alt += 0.5)
            {
                double v = Twilight.ZenithBrightening(alt);
                Assert.True(v >= prev,
                    $"non-monotonic at sun_alt={alt}: prev={prev}, current={v}");
                prev = v;
            }
        }
    }
}
