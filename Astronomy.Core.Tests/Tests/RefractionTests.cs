using Astronomy.Core.Astrometry;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class RefractionTests
    {
        [Fact]
        public void BennettDeg_AtZenith_IsNearZero()
        {
            // Refraction at 90 deg altitude is essentially nil. Bennett's formula at exactly
            // 90 deg returns a tiny negative value (~-2e-5) due to cot(alt + 7.31/(alt+4.4))
            // crossing through 90 deg; both directions are valid "approximately zero".
            Assert.InRange(Refraction.BennettDeg(90.0), -0.001, 0.001);
        }

        [Fact]
        public void BennettDeg_AtHorizon_IsAroundThirtyFourArcmin()
        {
            // Standard atmosphere refraction at 0 deg apparent altitude is ~0.567 deg
            // (34 arcmin) -- the classic textbook number.
            double r = Refraction.BennettDeg(0.0);
            Assert.InRange(r, 0.55, 0.59);
        }

        [Fact]
        public void BennettDeg_BelowMinusOneDeg_IsZero()
        {
            // Sentinel: below -1 deg apparent altitude, formula breaks down; library
            // returns 0.
            Assert.Equal(0.0, Refraction.BennettDeg(-1.5));
            Assert.Equal(0.0, Refraction.BennettDeg(-30.0));
        }

        [Fact]
        public void BennettDeg_DecreasingWithAltitude()
        {
            // Strict monotone decrease across the (0, 90) range: refraction always less
            // at higher altitudes.
            double prev = double.PositiveInfinity;
            for (double alt = 0.0; alt <= 90.0; alt += 1.0)
            {
                double r = Refraction.BennettDeg(alt);
                Assert.True(r <= prev, $"non-monotonic at alt={alt}: prev={prev}, r={r}");
                prev = r;
            }
        }
    }
}
