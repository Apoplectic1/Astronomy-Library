using System;
using Astronomy.Core.Night;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for NightWindow.IsValid -- the sentinel short-circuit that
    // CoarseVisibility.IsEverVisible and other consumers rely on to handle
    // polar-day / polar-night cases without checking both endpoints
    // individually. Pin the four endpoint-combinations explicitly so a
    // future tweak to the sentinel rule cannot silently shift valid-window
    // recognition.
    public class NightWindowTests
    {
        [Fact]
        public void IsValid_BothRealEndpoints_True()
        {
            var nw = new NightWindow
            {
                AstronomicalDusk = new DateTime(2026, 5, 18, 21, 0, 0, DateTimeKind.Utc),
                AstronomicalDawn = new DateTime(2026, 5, 19,  6, 0, 0, DateTimeKind.Utc),
                LunarIlluminationFraction = 0.5
            };
            Assert.True(nw.IsValid);
        }

        // Any MinValue endpoint -- whether dusk-only, dawn-only, or both --
        // marks the night as invalid (polar day / polar night).
        [Theory]
        [InlineData(true,  false)] // dusk real, dawn MinValue
        [InlineData(false, true)]  // dusk MinValue, dawn real
        [InlineData(false, false)] // both MinValue
        public void IsValid_EitherEndpointMinValue_False(bool duskReal, bool dawnReal)
        {
            var nw = new NightWindow
            {
                AstronomicalDusk = duskReal
                    ? new DateTime(2026, 5, 18, 21, 0, 0, DateTimeKind.Utc)
                    : DateTime.MinValue,
                AstronomicalDawn = dawnReal
                    ? new DateTime(2026, 5, 19, 6, 0, 0, DateTimeKind.Utc)
                    : DateTime.MinValue,
                LunarIlluminationFraction = 0.5
            };
            Assert.False(nw.IsValid);
        }

        // Default-initialised NightWindow (all fields default(T)) has both
        // endpoints == DateTime.MinValue, so IsValid is false. This is the
        // shape the test scaffolding uses for the "no valid night" sentinel
        // return from CoarseVisibility / NightCalculator.
        [Fact]
        public void IsValid_DefaultInstance_False()
        {
            var nw = default(NightWindow);
            Assert.False(nw.IsValid);
        }
    }
}
