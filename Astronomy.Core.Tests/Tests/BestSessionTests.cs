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

        // PlaceBest is the placement primitive that For calls internally; the public
        // expose lets callers (e.g. a chart with pre-cached moon-clear sub-intervals)
        // skip For's internal moon sweep. Equivalence test: feeding the same visibility
        // windows that For computes internally must yield identical placement.
        [Fact]
        public void PlaceBest_PublicExposure_MatchesForOutput()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);
            var minDur = TimeSpan.FromHours(2);
            var maxDur = TimeSpan.FromHours(4);

            var visibility = VisibilityWindows.For(Target.Default, loc, night, horizon);
            var forResult = BestSession.For(
                Target.Default, loc, night, horizon, minDur, maxDur, SinAltQuality, profile: null);
            var placeBestResult = BestSession.PlaceBest(
                Target.Default, loc, visibility, minDur, maxDur, SinAltQuality);

            Assert.Equal(forResult.HasValue, placeBestResult.HasValue);
            if (forResult.HasValue)
            {
                Assert.Equal(forResult.Value.Start, placeBestResult.Value.Start);
                Assert.Equal(forResult.Value.End, placeBestResult.Value.End);
                Assert.Equal(forResult.Value.Quality, placeBestResult.Value.Quality, 12);
            }
        }

        // Two manually-constructed windows: one near transit (high altitude), one in
        // early evening (lower altitude). PlaceBest must pick the high one because
        // sin-altitude integrated over the higher window is greater.
        [Fact]
        public void PlaceBest_WithMoonClearSubintervals_PicksBestQualityWindow()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(2);

            // High-altitude window: 2 hours bracketing transit.
            var highWindow = (Start: transitUtc - TimeSpan.FromHours(1),
                              End:   transitUtc + TimeSpan.FromHours(1));
            // Low-altitude window: 2 hours, 6 hours before transit (target is much lower).
            var lowWindow  = (Start: transitUtc - TimeSpan.FromHours(7),
                              End:   transitUtc - TimeSpan.FromHours(5));

            var windows = new[] { lowWindow, highWindow };
            var result = BestSession.PlaceBest(Target.Default, loc, windows, dur, dur, SinAltQuality);

            Assert.NotNull(result);
            // The winning placement should sit inside (or coincide with) highWindow,
            // not lowWindow.
            Assert.True(result.Value.Start >= highWindow.Start && result.Value.End <= highWindow.End,
                $"Expected placement inside highWindow [{highWindow.Start:O}, {highWindow.End:O}], got [{result.Value.Start:O}, {result.Value.End:O}]");
        }

        // Strict transit-centered placement: the session is exactly [transit - dur/2,
        // transit + dur/2]. When the supplied window contains that interval, the
        // method must return it.
        [Fact]
        public void PlaceCentered_TransitInsideWindow_ReturnsCenteredSession()
        {
            var loc = MakeLocation();
            DateTime transitSeed = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(2);

            // Window wide enough to contain the centered session [transit - 1h, transit + 1h].
            var window = (Start: transitSeed - TimeSpan.FromHours(2),
                          End:   transitSeed + TimeSpan.FromHours(2));
            var windows = new[] { window };

            // PlaceCentered re-resolves transit from window.Start; floating-point in
            // TransitTime.UtcAtOrAfter means using the test's transitSeed directly would
            // diverge by ~µs. Compute the expected centered session via the same call
            // path the implementation uses.
            DateTime transitFromWinStart = TransitTime.UtcAtOrAfter(Target.Default, loc, window.Start);

            var result = BestSession.PlaceCentered(Target.Default, loc, windows, dur);

            Assert.NotNull(result);
            Assert.Equal(transitFromWinStart - TimeSpan.FromHours(1), result.Value.Start);
            Assert.Equal(transitFromWinStart + TimeSpan.FromHours(1), result.Value.End);
        }

        // When all candidate windows lie entirely before (or after) the transit,
        // PlaceCentered cannot fit a centered session and returns null.
        [Fact]
        public void PlaceCentered_TransitOutsideAllWindows_ReturnsNull()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(2);

            // Window entirely before transit: TransitTime.UtcAtOrAfter(window.Start)
            // returns the upcoming transit, which is past window.End.
            var window = (Start: transitUtc - TimeSpan.FromHours(3),
                          End:   transitUtc - TimeSpan.FromHours(1));
            var windows = new[] { window };

            var result = BestSession.PlaceCentered(Target.Default, loc, windows, dur);

            Assert.Null(result);
        }

        // The Symmetric "doesn't fit" case: transit is in the window, but the centered
        // session would spill past one of the window edges. PlaceCentered does NOT
        // wall-push (that's PlaceBest's job); it returns null instead.
        [Fact]
        public void PlaceCentered_CenteredSessionExceedsWindow_ReturnsNull()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(4);  // would require a 4-hour-wide window centered on transit

            // Window only 2 hours wide; centered session [transit - 2h, transit + 2h]
            // spills past both edges.
            var window = (Start: transitUtc - TimeSpan.FromHours(1),
                          End:   transitUtc + TimeSpan.FromHours(1));
            var windows = new[] { window };

            var result = BestSession.PlaceCentered(Target.Default, loc, windows, dur);

            Assert.Null(result);
        }

        [Fact]
        public void PlaceCentered_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var windows = new[] { (Start: DateTime.UtcNow, End: DateTime.UtcNow.AddHours(2)) };
            var dur = TimeSpan.FromHours(1);

            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(null, loc, windows, dur));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(Target.Default, null, windows, dur));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(Target.Default, loc, null, dur));
            Assert.Throws<ArgumentException>(() =>
                BestSession.PlaceCentered(Target.Default, loc, windows, TimeSpan.Zero));
        }
    }
}
