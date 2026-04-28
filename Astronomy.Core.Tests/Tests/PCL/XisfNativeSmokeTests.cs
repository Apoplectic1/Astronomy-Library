using Astronomy.PCL.Interop;
using Xunit;

namespace Astronomy.Core.Tests.Tests.PCL
{
    public class XisfNativeSmokeTests
    {
        [Fact]
        public void Add_ReturnsSum()
        {
            Assert.Equal(7, NativeMethods.AstronomyXisf_Add(3, 4));
        }
    }
}
