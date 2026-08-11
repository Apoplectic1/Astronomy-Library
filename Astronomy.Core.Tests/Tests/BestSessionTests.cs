using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;
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

        private static Location MakeLocation() => TestLocations.PennsPark;
        private static DateTime MakeSeed(int year = 2026, int month = 11, int day = 15)
            => new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // M31 is well-placed at Penns Park in November; tests use it as the canonical
        // "always visible for several hours" target.
        [Fact]
        public void For_LegacyPath_NullProfile_ReturnsResult()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
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
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var nullResult = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: null);

            var disabledResult = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonLimitProfile.Disabled);

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
            // night the target-moon separation stays large enough that the K-S Δmag at
            // the target sits inside Narrowband's 1.0-mag tolerance for at least part of
            // the visibility window. We don't compare
            // exact placement against the moon-blind result here because moon-clear
            // sub-interval boundaries can shift by up to 10 minutes via the sweep, so
            // exact-match would be brittle. We only assert "still got a session".
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var result = BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonLimitProfile.Narrowband);

            Assert.NotNull(result);
        }

        [Fact]
        public void For_NullTarget_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() => BestSession.For(
                null, loc, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonLimitProfile.Narrowband));
        }

        [Fact]
        public void For_NullLocation_Throws()
        {
            var night = NightCalculator.ComputeNight(MakeLocation(), MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() => BestSession.For(
                Target.Default, null, night, horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonLimitProfile.Narrowband));
        }

        [Fact]
        public void For_NonPositiveMinDuration_ReturnsNull()
        {
            // Non-positive minDuration is the user-reachable degenerate case
            // (chart UI scrubs Duration spinner to zero). The contract treats it
            // as "no fit possible" rather than a caller bug -- consumers want a
            // uniform null return rather than translating an exception into the
            // same null themselves.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Null(BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.Zero, TimeSpan.FromHours(4),
                SinAltQuality, profile: null));
            Assert.Null(BestSession.For(
                Target.Default, loc, night, horizon,
                TimeSpan.FromHours(-1), TimeSpan.FromHours(4),
                SinAltQuality, profile: null));
        }

        [Fact]
        public void For_MinExceedsMax_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
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
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
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

        // Regression: PlaceBest must use endpoint-altitude comparison (NOT
        // TransitTime.UtcAtOrAfter sign comparison) to decide which window wall is
        // "transit-side". Failure mode: for a descending-arc window (post-PREVIOUS
        // transit), UtcAtOrAfter returns TOMORROW's transit, treating the window as
        // "transit AFTER" and pushing session against the LOW-altitude end (window.End).
        // The correct placement is against window.Start (high altitude, just past
        // yesterday's transit). Symptom: PlaceBest places the session at the lowest-
        // altitude end of the visibility arc instead of the highest, so any Floor /
        // Ceiling derived from its returned window is artificially low.
        [Fact]
        public void PlaceBest_DescendingArcWindow_PushesAgainstHighAltitudeStart()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(2);

            // Window 2-6 hours AFTER transit: post-transit descending arc. The "next
            // transit at-or-after window.Start" is tomorrow's, well beyond window.End,
            // so PlaceBest sees transit-AFTER. Without the altitude-comparison fix it
            // would push session against window.End (lowest altitude); the fix pushes
            // against window.Start (highest altitude).
            var window = new UtcInterval(transitUtc + TimeSpan.FromHours(2),
                          transitUtc + TimeSpan.FromHours(6));
            var windows = new[] { window };

            var result = BestSession.PlaceBest(
                Target.Default, loc, windows, dur, dur, SinAltQuality);

            Assert.NotNull(result);
            Assert.Equal(window.Start, result.Value.Start);
            Assert.Equal(window.Start + dur, result.Value.End);
        }

        // Symmetric companion: pre-transit RISING arc window. UtcAtOrAfter returns the
        // upcoming transit (after window.End), and the high-altitude end is window.End
        // (closer to transit). Both the buggy and fixed code happen to agree here -- this
        // test exists to lock in the rising-arc behavior so a future change to the
        // altitude-comparison logic doesn't accidentally break it.
        [Fact]
        public void PlaceBest_RisingArcWindow_PushesAgainstHighAltitudeEnd()
        {
            var loc = MakeLocation();
            DateTime transitUtc = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var dur = TimeSpan.FromHours(2);

            // Window 6-2 hours BEFORE transit: pre-transit rising arc.
            var window = new UtcInterval(transitUtc - TimeSpan.FromHours(6),
                          transitUtc - TimeSpan.FromHours(2));
            var windows = new[] { window };

            var result = BestSession.PlaceBest(
                Target.Default, loc, windows, dur, dur, SinAltQuality);

            Assert.NotNull(result);
            Assert.Equal(window.End - dur, result.Value.Start);
            Assert.Equal(window.End, result.Value.End);
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
            var highWindow = new UtcInterval(transitUtc - TimeSpan.FromHours(1),
                              transitUtc + TimeSpan.FromHours(1));
            // Low-altitude window: 2 hours, 6 hours before transit (target is much lower).
            var lowWindow  = new UtcInterval(transitUtc - TimeSpan.FromHours(7),
                              transitUtc - TimeSpan.FromHours(5));

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
            var window = new UtcInterval(transitSeed - TimeSpan.FromHours(2),
                          transitSeed + TimeSpan.FromHours(2));
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
            var window = new UtcInterval(transitUtc - TimeSpan.FromHours(3),
                          transitUtc - TimeSpan.FromHours(1));
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
            var window = new UtcInterval(transitUtc - TimeSpan.FromHours(1),
                          transitUtc + TimeSpan.FromHours(1));
            var windows = new[] { window };

            var result = BestSession.PlaceCentered(Target.Default, loc, windows, dur);

            Assert.Null(result);
        }

        [Fact]
        public void PlaceCentered_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var windows = new[] { new UtcInterval(DateTime.UtcNow, DateTime.UtcNow.AddHours(2)) };
            var dur = TimeSpan.FromHours(1);

            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(null, loc, windows, dur));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(Target.Default, null, windows, dur));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.PlaceCentered(Target.Default, loc, null, dur));
        }

        [Fact]
        public void PlaceCentered_NonPositiveDuration_ReturnsNull()
        {
            // Degenerate "no fit possible" -- non-positive duration returns null.
            var loc = MakeLocation();
            var windows = new[] { new UtcInterval(DateTime.UtcNow, DateTime.UtcNow.AddHours(2)) };

            Assert.Null(BestSession.PlaceCentered(Target.Default, loc, windows, TimeSpan.Zero));
            Assert.Null(BestSession.PlaceCentered(Target.Default, loc, windows, TimeSpan.FromHours(-1)));
        }

        // ResolveCandidates is the public surface for callers that need the same
        // visibility-or-moon-clear-intersected candidate set across multiple
        // placement strategies (e.g. PlaceBest + PlaceCentered on the same night
        // for a Sessions chart). Equivalence test: feeding the resolved candidates
        // to PlaceBest must produce the same result as For directly, since For
        // calls ResolveCandidates internally before placing.
        [Fact]
        public void ResolveCandidates_PlusPlaceBest_MatchesFor_NullProfile()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);
            var dur = TimeSpan.FromHours(2);

            var fromFor = BestSession.For(
                Target.Default, loc, night, horizon, dur, dur, SinAltQuality, profile: null);

            var candidates = BestSession.ResolveCandidates(
                Target.Default, loc, night, horizon, profile: null);
            var fromPlace = BestSession.PlaceBest(
                Target.Default, loc, candidates, dur, dur, SinAltQuality);

            Assert.NotNull(fromFor);
            Assert.NotNull(fromPlace);
            Assert.Equal(fromFor.Value.Start, fromPlace.Value.Start);
            Assert.Equal(fromFor.Value.End,   fromPlace.Value.End);
            Assert.Equal(fromFor.Value.Quality, fromPlace.Value.Quality, precision: 12);
        }

        [Fact]
        public void ResolveCandidates_PlusPlaceBest_MatchesFor_NarrowbandProfile()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);
            var dur = TimeSpan.FromHours(2);
            var profile = MoonLimitProfile.Narrowband;

            var fromFor = BestSession.For(
                Target.Default, loc, night, horizon, dur, dur, SinAltQuality, profile: profile);

            var candidates = BestSession.ResolveCandidates(
                Target.Default, loc, night, horizon, profile: profile);
            var fromPlace = candidates.Count == 0
                ? null
                : BestSession.PlaceBest(
                    Target.Default, loc, candidates, dur, dur, SinAltQuality);

            Assert.Equal(fromFor.HasValue, fromPlace.HasValue);
            if (fromFor.HasValue && fromPlace.HasValue)
            {
                Assert.Equal(fromFor.Value.Start, fromPlace.Value.Start);
                Assert.Equal(fromFor.Value.End,   fromPlace.Value.End);
                Assert.Equal(fromFor.Value.Quality, fromPlace.Value.Quality, precision: 12);
            }
        }

        [Fact]
        public void ResolveCandidates_NullProfile_EqualsVisibilityWindowsFor()
        {
            // With profile == null (or Disabled), ResolveCandidates returns visibility
            // unchanged -- byte-equal to VisibilityWindows.For's output.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var visibility = VisibilityWindows.For(Target.Default, loc, night, horizon);
            var resolved = BestSession.ResolveCandidates(
                Target.Default, loc, night, horizon, profile: null);

            Assert.Equal(visibility.Count, resolved.Count);
            for (int i = 0; i < visibility.Count; i++)
            {
                Assert.Equal(visibility[i].Start, resolved[i].Start);
                Assert.Equal(visibility[i].End,   resolved[i].End);
            }
        }

        [Fact]
        public void ResolveCandidates_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() =>
                BestSession.ResolveCandidates(null, loc, night, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.ResolveCandidates(Target.Default, null, night, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                BestSession.ResolveCandidates(Target.Default, loc, night, null));
        }
    }
}
