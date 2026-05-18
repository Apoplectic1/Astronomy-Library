using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for Target's D/M/S accessor contract, the With(...) round-trip
    // rule, and the Default-is-M31 anchor. The Dec D/M/S accessors had a
    // documented historical bug (routed through TimeSpan.FromHours(double),
    // producing hour-of-declination values instead of degree-of-declination
    // values); the current implementation uses a direct decimal-degree
    // decomposition. Pin both contracts here.
    public class TargetTests
    {
        // Target.Default is the M31 anchor used as a baseline by many other
        // tests. Verify the canonical RA/Dec are intact.
        [Fact]
        public void Default_IsM31()
        {
            var t = Target.Default;
            Assert.Equal("M31", t.Name);
            Assert.Equal(0.712306, t.RightAscension, precision: 6);
            Assert.Equal(41.269167, t.Declination, precision: 6);
            Assert.True(t.North);
            Assert.True(t.Enabled);
            Assert.Equal(string.Empty, t.Directory);
        }

        // DecDegrees / DecMinutes / DecSeconds must produce the degree-of-
        // declination decomposition. For M31 (Dec 41.269167 deg = 41 deg
        // 16' 9.00"), the components are (41, 16, ~9.0). The previous
        // (now-fixed) implementation produced (41, 16, ~9.00) only by
        // coincidence for the integer-degrees part; for fractional cases
        // the hour-vs-degree confusion would have surfaced as visibly
        // wrong seconds.
        [Fact]
        public void DecDms_ForM31_MatchesDegreeOfDeclinationDecomposition()
        {
            var t = Target.Default;
            Assert.Equal(41.0, t.DecDegrees);
            Assert.Equal(16.0, t.DecMinutes);
            Assert.Equal(9.0012, t.DecSeconds, precision: 1);
        }

        // RA DMS at M31's RA = 0.712306 hours = 0h 42m 44.3s.
        [Fact]
        public void RaDms_ForM31_MatchesHoursMinutesSeconds()
        {
            var t = Target.Default;
            Assert.Equal(0.0, t.RaHours);
            Assert.Equal(42.0, t.RaMinutes);
            Assert.Equal(44.30, t.RaSeconds, precision: 1);
        }

        // With() with no arguments must reproduce every field. A regression
        // where one constructor parameter is dropped from With's threading
        // would silently break round-trip equality.
        [Fact]
        public void With_NoArgs_RoundTripsAllFields()
        {
            var original = new Target(
                name:           "test target",
                rightAscension: 12.345,
                declination:    67.89, north: false,
                directory:      @"C:\some\path.json",
                enabled:        false);
            var copy = original.With();

            Assert.Equal(original.Name,           copy.Name);
            Assert.Equal(original.RightAscension, copy.RightAscension);
            Assert.Equal(original.Declination,    copy.Declination);
            Assert.Equal(original.North,          copy.North);
            Assert.Equal(original.Directory,      copy.Directory);
            Assert.Equal(original.Enabled,        copy.Enabled);
        }

        // With() with a single field changed preserves all others.
        [Fact]
        public void With_RaOnly_PreservesOtherFields()
        {
            var original = Target.Default;
            var copy = original.With(rightAscension: 5.0);
            Assert.Equal(5.0, copy.RightAscension);
            Assert.Equal(original.Name,        copy.Name);
            Assert.Equal(original.Declination, copy.Declination);
            Assert.Equal(original.North,       copy.North);
            Assert.Equal(original.Directory,   copy.Directory);
            Assert.Equal(original.Enabled,     copy.Enabled);
        }
    }
}
