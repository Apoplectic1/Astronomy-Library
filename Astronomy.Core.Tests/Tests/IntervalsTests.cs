using System;
using System.Collections.Generic;
using System.Linq;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class IntervalsTests
    {
        private static readonly DateTime Base = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

        private static DateTime T(double hours) => Base.AddHours(hours);

        private static UtcInterval I(double startH, double endH) => new(T(startH), T(endH));

        // ================================================================
        // Subtract(window, span): the six MaximumAltitudeClipper relative
        // positions (TS NINA.Plugin.TargetScheduler/Astrometry/
        // MaximumAltitudeClipper.cs), covered generically by half-open
        // subtraction instead of case enumeration.
        // ================================================================

        [Fact]
        public void Subtract_TsCase1_WindowEntirelyBeforeSpan_Unchanged()
        {
            var result = Intervals.Subtract(I(1, 3), I(4, 6));
            Assert.Equal(new[] { I(1, 3) }, result);
        }

        [Fact]
        public void Subtract_TsCase2_SpanClipsTail()
        {
            // Window head before the span, tail inside it -> keep the head.
            var result = Intervals.Subtract(I(1, 5), I(4, 6));
            Assert.Equal(new[] { I(1, 4) }, result);
        }

        [Fact]
        public void Subtract_TsCase3_WindowInsideSpan_Empty()
        {
            Assert.Empty(Intervals.Subtract(I(4, 5), I(3, 6)));
        }

        [Fact]
        public void Subtract_TsCase4_SpanClipsHead()
        {
            // Window head inside the span, tail after it -> keep the tail.
            var result = Intervals.Subtract(I(4, 8), I(3, 6));
            Assert.Equal(new[] { I(6, 8) }, result);
        }

        [Fact]
        public void Subtract_TsCase5_WindowEntirelyAfterSpan_Unchanged()
        {
            var result = Intervals.Subtract(I(7, 9), I(4, 6));
            Assert.Equal(new[] { I(7, 9) }, result);
        }

        [Fact]
        public void Subtract_TsCase6_WindowSurroundsSpan_SplitsInTwo()
        {
            // TS's Clip returned only the pre-span segment here; generic
            // subtraction returns both survivors.
            var result = Intervals.Subtract(I(1, 9), I(4, 6));
            Assert.Equal(new[] { I(1, 4), I(6, 9) }, result);
        }

        [Fact]
        public void Subtract_TouchingSpan_Unchanged()
        {
            // Half-open: a span touching the window end-to-start removes nothing.
            Assert.Equal(new[] { I(1, 3) }, Intervals.Subtract(I(1, 3), I(3, 5)));
            Assert.Equal(new[] { I(3, 5) }, Intervals.Subtract(I(3, 5), I(1, 3)));
        }

        // ================================================================
        // Intersect
        // ================================================================

        [Fact]
        public void Intersect_Overlapping_ReturnsOverlap()
        {
            // Spec scenario: [20:00, 23:00) ∩ [22:00, 02:00) = [22:00, 23:00).
            var result = Intervals.Intersect(
                new[] { I(20, 23) }, new[] { I(22, 26) });
            Assert.Equal(new[] { I(22, 23) }, result);
        }

        [Fact]
        public void Intersect_Touching_Empty()
        {
            // Spec scenario: [20:00, 22:00) ∩ [22:00, 23:00) = empty.
            Assert.Empty(Intervals.Intersect(new[] { I(20, 22) }, new[] { I(22, 23) }));
        }

        [Fact]
        public void Intersect_MultiWindow_PairwiseOverlaps()
        {
            var a = new[] { I(1, 4), I(6, 9) };
            var b = new[] { I(3, 7), I(8, 10) };
            Assert.Equal(new[] { I(3, 4), I(6, 7), I(8, 9) }, Intervals.Intersect(a, b));
        }

        [Fact]
        public void Intersect_EmptyOperand_Empty()
        {
            Assert.Empty(Intervals.Intersect(Array.Empty<UtcInterval>(), new[] { I(1, 2) }));
            Assert.Empty(Intervals.Intersect(new[] { I(1, 2) }, Array.Empty<UtcInterval>()));
        }

        // ================================================================
        // Union
        // ================================================================

        [Fact]
        public void Union_OverlappingAndTouching_Coalesce()
        {
            // Spec scenario: overlapping and exactly-touching intervals merge.
            var result = Intervals.Union(
                new[] { I(1, 3), I(5, 7) }, new[] { I(2, 5), I(8, 9) });
            Assert.Equal(new[] { I(1, 7), I(8, 9) }, result);
        }

        [Fact]
        public void Union_EmptyOperand_ReturnsOther()
        {
            var a = new[] { I(1, 2), I(4, 5) };
            Assert.Equal(a, Intervals.Union(a, Array.Empty<UtcInterval>()));
            Assert.Equal(a, Intervals.Union(Array.Empty<UtcInterval>(), a));
        }

        // ================================================================
        // Subtract (list form)
        // ================================================================

        [Fact]
        public void Subtract_ListForm_MultipleSpansAcrossWindows()
        {
            var windows = new[] { I(1, 6), I(8, 12) };
            var forbidden = new[] { I(2, 3), I(5, 9), I(11, 14) };
            Assert.Equal(
                new[] { I(1, 2), I(3, 5), I(9, 11) },
                Intervals.Subtract(windows, forbidden));
        }

        [Fact]
        public void Subtract_EmptySubtrahend_Unchanged()
        {
            var a = new[] { I(1, 3) };
            Assert.Equal(a, Intervals.Subtract(a, Array.Empty<UtcInterval>()));
        }

        // ================================================================
        // Clip
        // ================================================================

        [Fact]
        public void Clip_TrimsPartialOverlapsToBound()
        {
            // Spec scenario: windows clipped to a dusk-dawn bound.
            var result = Intervals.Clip(
                new[] { I(1, 5), I(7, 9), I(11, 13) }, bound: I(4, 12));
            Assert.Equal(new[] { I(4, 5), I(7, 9), I(11, 12) }, result);
        }

        [Fact]
        public void Clip_EquivalentToIntersectWithSingleBound()
        {
            var list = new[] { I(1, 5), I(7, 9), I(11, 13) };
            var bound = I(4, 12);
            Assert.Equal(
                Intervals.Intersect(list, new[] { bound }),
                Intervals.Clip(list, bound));
        }

        // ================================================================
        // Canonical-list contract: fail fast on violation
        // ================================================================

        [Fact]
        public void Ops_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Intervals.Intersect(null!, new[] { I(1, 2) }));
            Assert.Throws<ArgumentNullException>(() => Intervals.Union(new[] { I(1, 2) }, null!));
            Assert.Throws<ArgumentNullException>(() => Intervals.Subtract(null!, Array.Empty<UtcInterval>()));
            Assert.Throws<ArgumentNullException>(() => Intervals.Clip(null!, I(1, 2)));
        }

        [Fact]
        public void Ops_UnorderedInput_Throws()
        {
            var unordered = new[] { I(5, 6), I(1, 2) };
            Assert.Throws<ArgumentException>(() => Intervals.Intersect(unordered, Array.Empty<UtcInterval>()));
        }

        [Fact]
        public void Ops_OverlappingInput_Throws()
        {
            var overlapping = new[] { I(1, 4), I(3, 6) };
            Assert.Throws<ArgumentException>(() => Intervals.Union(overlapping, Array.Empty<UtcInterval>()));
        }

        [Fact]
        public void Ops_TouchingInput_Accepted()
        {
            // Touching elements are distinct intervals (e.g. same-side flip-split
            // pieces) -- legal input, preserved by non-union ops, coalesced by Union.
            var touching = new[] { I(1, 3), I(3, 5) };
            Assert.Equal(touching, Intervals.Subtract(touching, Array.Empty<UtcInterval>()));
            // The touching boundary survives intersection -- the elements stay
            // distinct (a flip-split solver depends on exactly this).
            Assert.Equal(new[] { I(2, 3), I(3, 4) }, Intervals.Intersect(touching, new[] { I(2, 4) }));
            Assert.Equal(new[] { I(1, 5) }, Intervals.Union(touching, Array.Empty<UtcInterval>()));
        }

        [Fact]
        public void Ops_DefaultUtcInterval_Throws()
        {
            var withDefault = new[] { default(UtcInterval) };
            Assert.Throws<ArgumentException>(() => Intervals.Clip(withDefault, I(1, 2)));
        }

        // ================================================================
        // Properties
        // ================================================================

        [Fact]
        public void Property_IntersectPlusSubtractReassembleOperandA()
        {
            // Union(a ∩ b, a − b) == a for canonical inputs.
            var a = new[] { I(1, 5), I(7, 10), I(12, 13) };
            var b = new[] { I(2, 8), I(9, 12.5) };
            var reassembled = Intervals.Union(
                Intervals.Intersect(a, b), Intervals.Subtract(a, b));
            Assert.Equal(a, reassembled);
        }

        [Fact]
        public void Property_OutputsSatisfyCanonicalInvariant()
        {
            // Every op result must itself be ordered, disjoint, merged -- feed
            // each back into another op as the cheapest invariant probe.
            var a = new[] { I(1, 5), I(7, 10) };
            var b = new[] { I(4, 8), I(9, 14) };
            foreach (var result in new[]
            {
                Intervals.Intersect(a, b),
                Intervals.Union(a, b),
                Intervals.Subtract(a, b),
                Intervals.Clip(a, I(2, 9)),
            })
            {
                // Throws if the result violates the canonical contract.
                Intervals.Intersect(result, Array.Empty<UtcInterval>());
                Assert.True(result.All(iv => iv.End > iv.Start));
            }
        }

        [Fact]
        public void Property_IntersectAndUnionAreCommutative()
        {
            var a = new[] { I(1, 5), I(7, 10) };
            var b = new[] { I(4, 8), I(9, 14) };
            Assert.Equal(Intervals.Intersect(a, b), Intervals.Intersect(b, a));
            Assert.Equal(Intervals.Union(a, b), Intervals.Union(b, a));
        }
    }
}
