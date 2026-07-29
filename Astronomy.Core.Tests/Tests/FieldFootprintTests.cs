using System;
using Astronomy.Core;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for FieldFootprint.OverlapFraction — the shared area of two equally-sized rectangular sky
    // fields differing in centre and rotation. Two of these guard failure modes that produce a
    // plausible-but-wrong number rather than an obvious error:
    //   * RA offsets must be scaled by cos(dec), or east-west displacement is overstated by 1/cos(dec)
    //     (nearly 3x at dec = +69). See RaOffset_IsScaledByCosDec* below.
    //   * A half-turn maps a rectangle onto itself, so 180 deg apart must read as complete overlap.
    // A wrong result in either case still lands in [0, 1] and still looks like a measurement.
    public class FieldFootprintTests
    {
        // The measured sensors this exists to serve: a 3:2 field and a square one, both on f=531.
        private const double WideW = 1.423;
        private const double WideH = 0.951;
        private const double SquareSide = 1.220;

        private static double Overlap(
            double measuredRaHours, double measuredDecDeg, double measuredRot,
            double referenceRaHours, double referenceDecDeg, double referenceRot,
            double widthDeg = WideW, double heightDeg = WideH) =>
            FieldFootprint.OverlapFraction(
                measuredRaHours, measuredDecDeg, measuredRot,
                referenceRaHours, referenceDecDeg, referenceRot,
                widthDeg, heightDeg);

        // ---- Degenerate and exact cases ----

        [Fact]
        public void Identical_IsExactlyOne()
        {
            Assert.Equal(1.0, Overlap(9.9589, 69.1303, 65.11, 9.9589, 69.1303, 65.11));
        }

        [Fact]
        public void FarApart_IsZero()
        {
            // 10 degrees of declination apart — many field widths.
            Assert.Equal(0.0, Overlap(9.9589, 59.1303, 65.11, 9.9589, 69.1303, 65.11));
        }

        [Fact]
        public void HalfTurn_IsCompleteOverlap_NoSpecialCasing()
        {
            // A rectangle rotated 180 deg about its own centre maps onto itself: a pier flip covers the
            // identical footprint. This must fall out of the geometry, not out of a fold applied beforehand.
            // Unlike the identical case, this one goes through the clip, so it lands a bit under exactly 1 —
            // asserting bit-equality on a polygon intersection would be testing the FPU, not the contract.
            Assert.Equal(1.0, Overlap(9.9589, 69.1303, 0.0, 9.9589, 69.1303, 180.0), 12);
            Assert.Equal(1.0, Overlap(9.9589, 69.1303, 245.11, 9.9589, 69.1303, 65.11), 12);
        }

        [Fact]
        public void SquareRotatedQuarterTurn_IsCompleteOverlap()
        {
            double f = Overlap(5.0, 20.0, 90.0, 5.0, 20.0, 0.0, SquareSide, SquareSide);
            Assert.Equal(1.0, f);
        }

        [Fact]
        public void WideFieldRotatedQuarterTurn_IsTheAspectRatio()
        {
            // A w x h rectangle crossed with the same rectangle turned 90 deg about one centre shares a
            // square of side min(w, h). Fraction = h^2 / (w*h) = h/w for w > h — closed form, no fitting.
            double f = Overlap(5.0, 20.0, 90.0, 5.0, 20.0, 0.0);
            Assert.Equal(WideH / WideW, f, 6);
        }

        // ---- Pure translation, closed form ----

        [Fact]
        public void ShiftedInDeclination_IsTheLinearFraction()
        {
            // Same rotation (0 = height along declination), displaced a quarter of the height north:
            // the shared strip is 3/4 of the height, so the fraction is 3/4 exactly.
            double shift = WideH / 4.0;
            double f = Overlap(5.0, 20.0 + shift, 0.0, 5.0, 20.0, 0.0);
            Assert.Equal(0.75, f, 6);
        }

        [Fact]
        public void ShiftedByAFullHeight_IsZero()
        {
            double f = Overlap(5.0, 20.0 + WideH, 0.0, 5.0, 20.0, 0.0);
            Assert.Equal(0.0, f, 9);
        }

        // ---- THE cos(dec) guard ----

        [Fact]
        public void RaOffset_IsScaledByCosDec()
        {
            // At dec = +69.13 (M81's), cos(dec) = 0.3554. An RA offset of 0.01 h = 0.15 deg becomes
            // 0.15 * 0.3554 = 0.0533 deg of sky. With rotation 0 the width runs along RA, so the shared
            // fraction is 1 - 0.0533/1.423 = 0.9625. Skipping cos(dec) would use the full 0.15 deg and
            // report 0.8946 — still a believable number, which is exactly the danger.
            double f = Overlap(9.9589 + 0.01, 69.1303, 0.0, 9.9589, 69.1303, 0.0);

            double cosDec = Math.Cos(69.1303 * Math.PI / 180.0);
            double expected = 1.0 - (0.15 * cosDec / WideW);
            Assert.Equal(expected, f, 4);

            double unscaled = 1.0 - (0.15 / WideW);
            Assert.NotEqual(unscaled, f, 3);
        }

        [Fact]
        public void SameRaOffset_OverlapsMoreAtHighDeclination()
        {
            // One RA offset, two declinations: the high-declination pair overlaps MORE, because the same
            // RA difference spans less sky there. If cos(dec) were dropped, both would read identically.
            double atEquator = Overlap(5.00 + 0.01, 0.0, 0.0, 5.00, 0.0, 0.0);
            double atHighDec = Overlap(9.9589 + 0.01, 69.1303, 0.0, 9.9589, 69.1303, 0.0);

            Assert.True(atHighDec > atEquator,
                $"expected more overlap at dec=69 than at the equator, got {atHighDec} vs {atEquator}");
        }

        [Fact]
        public void RaWrapAcrossZeroHours_MeasuresTheShortWay()
        {
            // 23.999 h and 0.001 h are 0.002 h apart, not 23.998 h. Without wrapping this reads as disjoint.
            double f = Overlap(23.999, 10.0, 0.0, 0.001, 10.0, 0.0);
            Assert.True(f > 0.9, $"expected a near-complete overlap across the 0h boundary, got {f}");
        }

        // ---- Rotation, and the combination ----

        [Fact]
        public void RotationAlone_ReducesOverlapMonotonically()
        {
            double at5 = Overlap(5.0, 20.0, 5.0, 5.0, 20.0, 0.0);
            double at20 = Overlap(5.0, 20.0, 20.0, 5.0, 20.0, 0.0);
            double at45 = Overlap(5.0, 20.0, 45.0, 5.0, 20.0, 0.0);

            Assert.True(at5 > at20 && at20 > at45, $"expected monotonic decrease, got {at5}, {at20}, {at45}");
            Assert.True(at5 < 1.0);
        }

        [Fact]
        public void TranslationAndRotationTogether_AreWorseThanEither()
        {
            double rotationOnly = Overlap(5.0, 20.0, 20.0, 5.0, 20.0, 0.0);
            double shiftOnly = Overlap(5.0, 20.0 + (WideH / 4.0), 0.0, 5.0, 20.0, 0.0);
            double both = Overlap(5.0, 20.0 + (WideH / 4.0), 20.0, 5.0, 20.0, 0.0);

            Assert.True(both < rotationOnly, $"{both} should be under {rotationOnly}");
            Assert.True(both < shiftOnly, $"{both} should be under {shiftOnly}");
        }

        [Fact]
        public void Result_IsSymmetricForEqualSizedFields_ToWithinTangentPlaneError()
        {
            // Equal areas mean the shared-area ratio is the same whichever field is the denominator — but only
            // to within the projection's own error. The tangent plane is built about the REFERENCE centre and
            // cos(dec) is taken there, so swapping the roles moves the origin (dec 20.0 vs 20.1) and shifts
            // the answer by a few parts in 100,000. That asymmetry is the approximation, not a defect; it sits
            // orders of magnitude below any reported precision. Pinned so a future change to the projection
            // origin shows up here as a size change rather than passing unnoticed.
            double forward = Overlap(5.01, 20.1, 30.0, 5.0, 20.0, 0.0);
            double reverse = Overlap(5.0, 20.0, 0.0, 5.01, 20.1, 30.0);

            // Bounded by magnitude, not decimal places: the measured gap is ~3.9e-5, which straddles a 4-dp
            // rounding boundary while being far tighter than any precision the number is reported at.
            Assert.True(Math.Abs(forward - reverse) < 1e-4,
                $"tangent-plane asymmetry grew beyond a few parts in 100,000: {forward} vs {reverse}");
        }

        [Fact]
        public void Result_IsAlwaysAFraction()
        {
            for (int i = 0; i < 360; i += 7)
            {
                double f = Overlap(5.0 + (i * 0.0005), 20.0 + (i * 0.005), i, 5.0, 20.0, 0.0);
                Assert.InRange(f, 0.0, 1.0);
            }
        }

        // ---- Guards ----

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(1.0, 0.0)]
        [InlineData(-1.0, 1.0)]
        public void NonPositiveSize_Throws(double widthDeg, double heightDeg)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FieldFootprint.OverlapFraction(5.0, 20.0, 0.0, 5.0, 20.0, 0.0, widthDeg, heightDeg));
        }
    }
}
