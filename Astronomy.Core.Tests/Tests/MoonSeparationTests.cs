using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Guard for MoonSeparation.ObserveAt: the bundle (Sep, MoonAlt) returned must
    // share the separation value with the legacy DegreesAt path (which is now a thin
    // wrapper around ObserveAt). MoonAlt must be in [-90, 90] for any sensible call.
    public class MoonSeparationObserveAtTests
    {
        private static readonly DateTime SampleUtc =
            new DateTime(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ObserveAt_SeparationMatchesDegreesAt()
        {
            var (sep, _, _) = MoonSeparation.ObserveAt(Target.Default, Location.Default, SampleUtc);
            double sepFromDegreesAt = MoonSeparation.DegreesAt(Target.Default, Location.Default, SampleUtc);
            Assert.Equal(sepFromDegreesAt, sep, 12);
        }

        [Fact]
        public void ObserveAt_SeparationInRange()
        {
            var (sep, _, _) = MoonSeparation.ObserveAt(Target.Default, Location.Default, SampleUtc);
            Assert.InRange(sep, 0.0, 180.0);
        }

        [Fact]
        public void ObserveAt_MoonAltInRange()
        {
            var (_, moonAlt, _) = MoonSeparation.ObserveAt(Target.Default, Location.Default, SampleUtc);
            Assert.InRange(moonAlt, -90.0, 90.0);
        }

        [Fact]
        public void ObserveAt_NullTarget_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => MoonSeparation.ObserveAt(null, Location.Default, SampleUtc));
        }

        [Fact]
        public void ObserveAt_NullLocation_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => MoonSeparation.ObserveAt(Target.Default, null, SampleUtc));
        }
    }
}
