using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Tests for BestSession.For, primarily the moon-aware overload. The
    // backwards-compat guarantee (profile == null behaves byte-identically to the
    // legacy moon-blind path) is exercised by checking that null and the Disabled
    // profile produce the same (Start, End, Quality) on the same inputs.
    public class BestSessionTests
    {
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        private static Location MakeLocation(int year = 2026, int month = 11, int day = 15)
            => Location.Default.With(
                dateTime: new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));

        // M31 is well-placed at Penns Park in November; tests use it as the canonical
        // "always visible for several hours" target.
        [Fact]
        public void For_LegacyPath_NullProfile_ReturnsResult()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var result = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality);

            Assert.NotNull(result);
        }

        [Fact]
        public void For_DisabledProfile_ProducesSameResultAsNullProfile()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var nullResult = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: null);

            var disabledResult = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonAvoidanceProfile.Disabled);

            Assert.Equal(nullResult.HasValue, disabledResult.HasValue);
            if (nullResult.HasValue)
            {
                Assert.Equal(nullResult.Value.Start, disabledResult.Value.Start);
                Assert.Equal(nullResult.Value.End, disabledResult.Value.End);
                Assert.Equal(nullResult.Value.Quality, disabledResult.Value.Quality, 12);
            }
        }

        [Fact]
        public void For_EnabledProfile_ReturnsResultWhenTargetMoonClear()
        {
            // M31 (RA 0.7h, Dec +41°) sits well off the ecliptic; on a typical mid-cycle
            // night the target-moon separation stays large enough that even Narrowband's
            // 60° threshold doesn't reject the entire visibility window. We don't compare
            // exact placement against the moon-blind result here because moon-clear
            // sub-interval boundaries can shift by up to 10 minutes via the sweep, so
            // exact-match would be brittle. We only assert "still got a session".
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var result = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonAvoidanceProfile.Narrowband);

            Assert.NotNull(result);
        }

        [Fact]
        public void For_NullTarget_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() => BestSession.For(
                null, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonAvoidanceProfile.Narrowband));
        }

        [Fact]
        public void For_NullLocation_Throws()
        {
            var night = NightCalculator.ComputeNight(MakeLocation());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() => BestSession.For(
                Target.Default, null, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonAvoidanceProfile.Narrowband));
        }

        [Fact]
        public void For_NonPositiveMinDuration_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentException>(() => BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.Zero, TimeSpan.FromHours(4),
                SinAltQuality, profile: null));
        }

        [Fact]
        public void For_MinExceedsMax_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentException>(() => BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(5), TimeSpan.FromHours(4),
                SinAltQuality, profile: null));
        }
    }
}
