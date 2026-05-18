using Astronomy.Core;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for the AltAz struct's two-property surface. AltAz is consumed
    // by name and by deconstruction across the suite; these tests pin both
    // the constructor's positional contract and the Deconstruct order
    // (altitude first, azimuth second) so a future field reorder can't
    // silently swap them at call sites that read by position.
    public class AltAzTests
    {
        [Fact]
        public void Ctor_StoresAltitudeAndAzimuth()
        {
            var aa = new AltAz(altitude: 45.5, azimuth: 180.0);
            Assert.Equal(45.5, aa.Altitude);
            Assert.Equal(180.0, aa.Azimuth);
        }

        // var (alt, az) = altAzCalculator.Of(...) is documented; pin it.
        [Fact]
        public void Deconstruct_ProducesAltitudeFirstAzimuthSecond()
        {
            var aa = new AltAz(altitude: 30.0, azimuth: 270.0);
            var (alt, az) = aa;
            Assert.Equal(30.0, alt);
            Assert.Equal(270.0, az);
        }
    }
}
