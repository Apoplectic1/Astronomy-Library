using Astronomy.PCL.Interop;
using Xunit;

namespace Astronomy.Core.Tests.Tests.PCL
{
    public class XisfNativeSmokeTests
    {
        // First-line "is the native DLL loaded and speaking the right ABI"
        // check via AstronomyXisf_Ping. Sum semantics let the assertion be a
        // real correctness check on the int marshalling pipe (both directions)
        // instead of an opaque "did the call return" probe.
        [Fact]
        public void Smoke_NativeLoadIsHealthy()
        {
            Assert.Equal(7, NativeMethods.AstronomyXisf_Ping(3, 4));
        }
    }
}
