using Astronomy.Core.Brightness;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for Bortle's two public lookup helpers. The private ClampIndex
    // is exercised indirectly via DefaultZenithMag / DefaultExtinctionK500
    // since its contract (silent clamp to [1, 9]) is part of the public
    // surface even though the method itself is private. Plus a spot-check
    // pin on the table values so a future "the IDA tweaked its standard
    // table" edit shows up here instead of as a silent K-S sky-brightness
    // drift.
    public class BortleTests
    {
        // Below-range inputs clamp to class 1 (sZenithMag[0] = 21.99).
        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(int.MinValue)]
        public void DefaultZenithMag_BelowOne_ClampsToClassOne(int bortleClass)
        {
            Assert.Equal(21.99, Bortle.DefaultZenithMag(bortleClass), precision: 4);
        }

        // Above-range inputs clamp to class 9 (sZenithMag[8] = 16.50).
        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        public void DefaultZenithMag_AboveNine_ClampsToClassNine(int bortleClass)
        {
            Assert.Equal(16.50, Bortle.DefaultZenithMag(bortleClass), precision: 4);
        }

        // Same clamp shape on the extinction coefficient.
        [Theory]
        [InlineData(0, 0.10)]
        [InlineData(10, 0.55)]
        public void DefaultExtinctionK500_OutOfRange_ClampsToBoundary(
            int bortleClass, double expectedK)
        {
            Assert.Equal(expectedK, Bortle.DefaultExtinctionK500(bortleClass), precision: 4);
        }

        // Spot-check the full in-range table. A future edit to the
        // standard table (or a typo in the array) shows up here as a
        // clean failure with the new vs expected value side-by-side.
        [Theory]
        [InlineData(1, 21.99, 0.10)]
        [InlineData(2, 21.93, 0.13)]
        [InlineData(3, 21.69, 0.18)]
        [InlineData(4, 20.97, 0.22)]
        [InlineData(5, 20.49, 0.28)]
        [InlineData(6, 19.50, 0.35)]
        [InlineData(7, 18.94, 0.42)]
        [InlineData(8, 17.80, 0.48)]
        [InlineData(9, 16.50, 0.55)]
        public void Defaults_MatchPublishedTable(int bortleClass, double expectedMag, double expectedK)
        {
            Assert.Equal(expectedMag, Bortle.DefaultZenithMag(bortleClass), precision: 4);
            Assert.Equal(expectedK, Bortle.DefaultExtinctionK500(bortleClass), precision: 4);
        }
    }
}
