using System;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class UtcIntervalTests
    {
        private static readonly DateTime Base = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

        private static DateTime T(double hours) => Base.AddHours(hours);

        // --- Construction: the contract gate ---

        [Fact]
        public void Ctor_LocalStart_Throws()
        {
            var local = DateTime.SpecifyKind(T(1), DateTimeKind.Local);
            var ex = Assert.Throws<ArgumentException>(() => new UtcInterval(local, T(2)));
            Assert.Equal("start", ex.ParamName);
        }

        [Fact]
        public void Ctor_UnspecifiedEnd_Throws()
        {
            var unspecified = DateTime.SpecifyKind(T(2), DateTimeKind.Unspecified);
            var ex = Assert.Throws<ArgumentException>(() => new UtcInterval(T(1), unspecified));
            Assert.Equal("end", ex.ParamName);
        }

        [Fact]
        public void Ctor_EndEqualsStart_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => new UtcInterval(T(1), T(1)));
            Assert.Equal("end", ex.ParamName);
        }

        [Fact]
        public void Ctor_EndBeforeStart_Throws()
        {
            Assert.Throws<ArgumentException>(() => new UtcInterval(T(2), T(1)));
        }

        // --- Members ---

        [Fact]
        public void Duration_IsEndMinusStart()
        {
            Assert.Equal(TimeSpan.FromHours(2.5), new UtcInterval(T(1), T(3.5)).Duration);
        }

        [Fact]
        public void Contains_HalfOpenSemantics()
        {
            var iv = new UtcInterval(T(1), T(3));
            Assert.True(iv.Contains(T(1)));    // Start inclusive
            Assert.True(iv.Contains(T(2)));
            Assert.False(iv.Contains(T(3)));   // End exclusive
            Assert.False(iv.Contains(T(0.5)));
            Assert.False(iv.Contains(T(4)));
        }

        [Fact]
        public void Contains_NonUtcInstant_Throws()
        {
            var iv = new UtcInterval(T(1), T(3));
            Assert.Throws<ArgumentException>(
                () => iv.Contains(DateTime.SpecifyKind(T(2), DateTimeKind.Local)));
        }

        [Fact]
        public void Overlaps_OverlappingAndContained_True()
        {
            var iv = new UtcInterval(T(1), T(3));
            Assert.True(iv.Overlaps(new UtcInterval(T(2), T(4))));
            Assert.True(iv.Overlaps(new UtcInterval(T(1.5), T(2.5))));  // contained
            Assert.True(new UtcInterval(T(0), T(5)).Overlaps(iv));      // containing
        }

        [Fact]
        public void Overlaps_TouchingAndDisjoint_False()
        {
            var iv = new UtcInterval(T(1), T(3));
            Assert.False(iv.Overlaps(new UtcInterval(T(3), T(4))));  // touching at End
            Assert.False(iv.Overlaps(new UtcInterval(T(0), T(1)))); // touching at Start
            Assert.False(iv.Overlaps(new UtcInterval(T(4), T(5))));
        }

        [Fact]
        public void Deconstruct_YieldsStartEnd()
        {
            var (start, end) = new UtcInterval(T(1), T(3));
            Assert.Equal(T(1), start);
            Assert.Equal(T(3), end);
        }

        [Fact]
        public void ValueEquality_SameEndpointsAreEqual()
        {
            Assert.Equal(new UtcInterval(T(1), T(3)), new UtcInterval(T(1), T(3)));
            Assert.NotEqual(new UtcInterval(T(1), T(3)), new UtcInterval(T(1), T(4)));
        }
    }
}
