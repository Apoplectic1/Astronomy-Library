using Astronomy.Core.Astrometry;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class RefractionTests
    {
        [Fact]
        public void SaemundssonDeg_AtZenith_IsNearZero()
        {
            // Refraction at 90 deg true altitude is essentially nil (Saemundsson
            // crosses through 90 deg with a tiny residual; both directions are
            // valid "approximately zero").
            Assert.InRange(Refraction.SaemundssonDeg(90.0), -0.001, 0.001);
        }

        [Fact]
        public void SaemundssonDeg_AtGeometricHorizon_IsAroundTwentyNineArcmin()
        {
            // Saemundsson takes geometric (true) altitude. At true h=0 the moon
            // appears ~29 arcmin above the horizon (apparent ~0.483 deg). The
            // textbook "34 arcmin at horizon" refers to the apparent horizon
            // (geometric ~-34 arcmin); the two values converge there.
            double r = Refraction.SaemundssonDeg(0.0);
            Assert.InRange(r, 0.47, 0.50);
        }

        [Fact]
        public void SaemundssonDeg_AtApparentHorizon_IsAroundThirtyFourArcmin()
        {
            // At geometric altitude ~-34 arcmin (-0.567 deg), the moon appears
            // exactly at the visual horizon (apparent ~0). Refraction there is
            // the classic textbook ~34 arcmin; the round-trip should land
            // apparent within sub-arcminute precision of 0.
            double rAtHorizon = Refraction.SaemundssonDeg(-0.567);
            Assert.InRange(rAtHorizon, 0.55, 0.59);
            double apparent = -0.567 + rAtHorizon;
            Assert.InRange(apparent, -0.01, 0.01);
        }

        [Fact]
        public void SaemundssonDeg_BelowMinusOneDeg_IsZero()
        {
            // Sentinel: below -1 deg true altitude, formula breaks down; library
            // returns 0.
            Assert.Equal(0.0, Refraction.SaemundssonDeg(-1.5));
            Assert.Equal(0.0, Refraction.SaemundssonDeg(-30.0));
        }

        [Fact]
        public void SaemundssonDeg_DecreasingWithAltitude()
        {
            // Strict monotone decrease across (0, 90): refraction always less at
            // higher altitudes.
            double prev = double.PositiveInfinity;
            for (double alt = 0.0; alt <= 90.0; alt += 1.0)
            {
                double r = Refraction.SaemundssonDeg(alt);
                Assert.True(r <= prev, $"non-monotonic at alt={alt}: prev={prev}, r={r}");
                prev = r;
            }
        }
    }
}
