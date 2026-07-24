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
    // Tests for the parameter-iteration solvers in SessionSolvers. Fixtures match the
    // BestSessionTests pattern: M31 at Penns Park on 2026-11-15.
    public class SessionSolversTests
    {
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        private static Location MakeLocation() => TestLocations.PennsPark;
        private static DateTime MakeSeed(int year = 2026, int month = 11, int day = 15)
            => new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // ====================================================================
        // LongestDurationIn (pre-resolved flavor)
        // ====================================================================

        // One window of length L; longest D = L (uncapped). Session occupies the whole
        // window since min=max=L leaves no slack for wall-pushing.
        [Fact]
        public void LongestDurationIn_SingleWindow_ReturnsWindowLength()
        {
            var loc = MakeLocation();
            DateTime t0 = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);
            var window = (Start: t0, End: t0.AddHours(5));
            var windows = new[] { window };

            var result = SessionSolvers.LongestDurationIn(Target.Default, loc, windows);

            Assert.NotNull(result);
            Assert.Equal(TimeSpan.FromHours(5), result.Value.Duration);
            Assert.Equal(window.Start, result.Value.Start);
            Assert.Equal(window.End, result.Value.End);
        }

        // Two windows of different lengths; the longer wins.
        [Fact]
        public void LongestDurationIn_MultipleWindows_ReturnsLongestLength()
        {
            var loc = MakeLocation();
            DateTime t0 = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);
            var shortWin = (Start: t0,                           End: t0.AddHours(2));
            var longWin  = (Start: t0.AddHours(3),               End: t0.AddHours(8));
            var windows = new[] { shortWin, longWin };

            var result = SessionSolvers.LongestDurationIn(Target.Default, loc, windows);

            Assert.NotNull(result);
            Assert.Equal(TimeSpan.FromHours(5), result.Value.Duration);
            Assert.True(result.Value.Start >= longWin.Start && result.Value.End <= longWin.End);
        }

        // Cap shorter than longest window: returned duration matches cap, placement
        // chooses transit-centered or wall-pushed within the longest window.
        [Fact]
        public void LongestDurationIn_Capped_ReturnsCap()
        {
            var loc = MakeLocation();
            DateTime t0 = new DateTime(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);
            var window = (Start: t0, End: t0.AddHours(8));
            var windows = new[] { window };

            var cap = TimeSpan.FromHours(3);
            var result = SessionSolvers.LongestDurationIn(Target.Default, loc, windows, cap);

            Assert.NotNull(result);
            Assert.Equal(cap, result.Value.Duration);
            Assert.Equal(cap, result.Value.End - result.Value.Start);
        }

        [Fact]
        public void LongestDurationIn_EmptyCandidates_ReturnsNull()
        {
            var loc = MakeLocation();
            var windows = Array.Empty<(DateTime, DateTime)>();

            var result = SessionSolvers.LongestDurationIn(Target.Default, loc, windows);

            Assert.Null(result);
        }

        [Fact]
        public void LongestDurationIn_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var windows = new[] { (Start: DateTime.UtcNow, End: DateTime.UtcNow.AddHours(2)) };

            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationIn(null, loc, windows));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationIn(Target.Default, null, windows));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationIn(Target.Default, loc, null));
            // Non-positive cap is the user-reachable degenerate case (UI scrubs
            // duration to zero); contract is to return null, not throw.
            Assert.Null(SessionSolvers.LongestDurationIn(
                Target.Default, loc, windows, TimeSpan.Zero));
            Assert.Null(SessionSolvers.LongestDurationIn(
                Target.Default, loc, windows, TimeSpan.FromHours(-1)));
        }

        // ====================================================================
        // LongestDuration (auto-resolve flavor)
        // ====================================================================

        // Auto-resolve with null profile must agree with feeding visibility windows
        // directly into LongestDurationIn -- the moon-blind paths are byte-identical.
        [Fact]
        public void LongestDuration_NullProfile_MatchesVisibilityLongestArm()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var auto = SessionSolvers.LongestDuration(
                Target.Default, loc, night, horizon, profile: null);

            var visibility = VisibilityWindows.For(Target.Default, loc, night, horizon);
            var manual = SessionSolvers.LongestDurationIn(Target.Default, loc, visibility);

            Assert.Equal(auto.HasValue, manual.HasValue);
            if (auto.HasValue)
            {
                Assert.Equal(auto.Value.Duration, manual.Value.Duration);
                Assert.Equal(auto.Value.Start, manual.Value.Start);
                Assert.Equal(auto.Value.End, manual.Value.End);
            }
        }

        // M31 well-placed; even with the Narrowband profile some night should yield
        // a positive-length moon-clear session (mirrors the existing
        // For_EnabledProfile_ReturnsResultWhenTargetMoonClear precedent).
        [Fact]
        public void LongestDuration_EnabledProfile_ReturnsResultWhenMoonClear()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var result = SessionSolvers.LongestDuration(
                Target.Default, loc, night, horizon,
                profile: MoonLimitProfile.Narrowband);

            Assert.NotNull(result);
            Assert.True(result.Value.Duration > TimeSpan.Zero);
        }

        // Polar night: NightCalculator returns IsValid=false, VisibilityWindows.For
        // returns empty. LongestDuration must propagate as null without throwing.
        [Fact]
        public void LongestDuration_PolarNight_ReturnsNull()
        {
            // Northern polar location in summer where the sun never sets.
            var loc = TestLocations.PennsPark.With(
                latitude: 80.0, north: true,
                longitude: 0.0, west: false);
            var solsticeSeed = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            var night = NightCalculator.ComputeNight(loc, solsticeSeed);
            var horizon = new ScalarHorizonProfile(20.0);

            var result = SessionSolvers.LongestDuration(
                Target.Default, loc, night, horizon);

            Assert.Null(result);
        }

        // ====================================================================
        // LowestHorizon (bisection)
        // ====================================================================

        // M31 at Penns Park yields 2h easily; the largest H that still fits is well
        // below meridian (~89° at Penns Park since M31's dec ≈ lat) and well above the
        // test floor of 0°. Sub-bound the answer between 50° and 89° as a sanity check
        // that the bisection is converging into the expected near-meridian region.
        [Fact]
        public void LowestHorizon_TargetClearsHorizonComfortably_ReturnsLowAngle()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            var result = SessionSolvers.LowestHorizon(
                Target.Default, loc, night, TimeSpan.FromHours(2));

            Assert.NotNull(result);
            Assert.True(result.Value.HorizonDeg > 50.0,
                $"Expected HorizonDeg > 50° (M31 fits 2h easily at high H); got {result.Value.HorizonDeg:F2}°");
            Assert.True(result.Value.HorizonDeg < 89.0,
                $"Expected HorizonDeg < 89° (M31's meridian alt ~89°); got {result.Value.HorizonDeg:F2}°");
            Assert.True(result.Value.End > result.Value.Start);
        }

        // Asking for a session longer than the night is unsatisfiable at any horizon.
        [Fact]
        public void LowestHorizon_DurationExceedsAvailableTime_ReturnsNull()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            var result = SessionSolvers.LowestHorizon(
                Target.Default, loc, night, TimeSpan.FromHours(48));

            Assert.Null(result);
        }

        // 20 iterations of bisection across the meridian-vs-floor bracket gives sub-
        // arcminute precision. Sanity: the answer at H_lowest should fit, and at
        // H_lowest + 1° should not (or be very close to not fitting).
        [Fact]
        public void LowestHorizon_BisectionPrecision_AchievesSubDegreePrecision()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var dur = TimeSpan.FromHours(2);

            var result = SessionSolvers.LowestHorizon(Target.Default, loc, night, dur);

            Assert.NotNull(result);
            // At the returned horizon, a D-hour session must actually fit.
            var horizonAtAnswer = new ScalarHorizonProfile(result.Value.HorizonDeg);
            var atAnswer = SessionSolvers.LongestDuration(
                Target.Default, loc, night, horizonAtAnswer);
            Assert.NotNull(atAnswer);
            Assert.True(atAnswer.Value.Duration >= dur);

            // 1° above the returned horizon, the session must NOT fit (or barely fit
            // within bisection precision -- so "doesn't fit OR fits with < 0.01h slack").
            var horizonAbove = new ScalarHorizonProfile(result.Value.HorizonDeg + 1.0);
            var above = SessionSolvers.LongestDuration(
                Target.Default, loc, night, horizonAbove);
            bool justAtBoundary = above.HasValue
                && (above.Value.Duration - dur).TotalMinutes < 0.6;
            Assert.True(above == null || above.Value.Duration < dur || justAtBoundary,
                $"At H+1° the session should not fit (or be at-boundary): got {above?.Duration}");
        }

        [Fact]
        public void LowestHorizon_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var dur = TimeSpan.FromHours(2);

            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LowestHorizon(null, loc, night, dur));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LowestHorizon(Target.Default, null, night, dur));
            // Non-positive duration is user-reachable; returns null instead of throwing.
            Assert.Null(SessionSolvers.LowestHorizon(Target.Default, loc, night, TimeSpan.Zero));
            Assert.Null(SessionSolvers.LowestHorizon(Target.Default, loc, night, TimeSpan.FromHours(-1)));
            Assert.Throws<ArgumentException>(() =>
                SessionSolvers.LowestHorizon(Target.Default, loc, night, dur, minHorizonDeg: -91.0));
            Assert.Throws<ArgumentException>(() =>
                SessionSolvers.LowestHorizon(Target.Default, loc, night, dur, maxIterations: 0));
        }

        // ====================================================================
        // LongestDurationCenteredIn (pre-resolved, strict transit-centered)
        // ====================================================================

        // Window symmetric around transit: longest centered D ≈ full window length.
        // Session straddles transit. The implementation re-resolves transit from
        // window.Start (sidereal/solar conversion can drift by ~µs from a transit
        // computed at a different searchFromUtc), so the test computes expected
        // values via the same call path.
        [Fact]
        public void LongestDurationCenteredIn_WindowContainsTransit_ReturnsCenteredAroundTransit()
        {
            var loc = MakeLocation();
            DateTime transitSeed = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            var window = (Start: transitSeed - TimeSpan.FromHours(2),
                          End:   transitSeed + TimeSpan.FromHours(2));

            DateTime transitForWindow = TransitTime.UtcAtOrAfter(Target.Default, loc, window.Start);
            TimeSpan leftRoom = transitForWindow - window.Start;
            TimeSpan rightRoom = window.End - transitForWindow;
            TimeSpan expectedRoom = leftRoom < rightRoom ? leftRoom : rightRoom;
            TimeSpan expectedDur = TimeSpan.FromTicks(expectedRoom.Ticks * 2);

            var result = SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, new[] { window });

            Assert.NotNull(result);
            Assert.Equal(expectedDur, result.Value.Duration);
            Assert.Equal(transitForWindow - expectedRoom, result.Value.Start);
            Assert.Equal(transitForWindow + expectedRoom, result.Value.End);
            // Sanity: nominal symmetric 4h window should yield ~4h centered.
            Assert.True(Math.Abs((expectedDur - TimeSpan.FromHours(4)).TotalMilliseconds) < 1.0,
                $"Expected ~4h centered duration; got {expectedDur}");
        }

        // Window entirely before (or after) transit: cannot host a centered session.
        [Fact]
        public void LongestDurationCenteredIn_WindowDoesNotContainTransit_ReturnsNull()
        {
            var loc = MakeLocation();
            DateTime transit = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            // Window 2-6 hours BEFORE transit -- entirely pre-transit.
            var window = (Start: transit - TimeSpan.FromHours(6),
                          End:   transit - TimeSpan.FromHours(2));
            var windows = new[] { window };

            var result = SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, windows);

            Assert.Null(result);
        }

        // Off-center transit in window: D_max = 2 * min(leftRoom, rightRoom), bounded
        // by the closer wall. Validates the symmetric-expansion algorithm rather than
        // returning the full window length.
        [Fact]
        public void LongestDurationCenteredIn_TransitOffCenterInWindow_LimitsByCloserWall()
        {
            var loc = MakeLocation();
            DateTime transitSeed = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            // Window 1h before transit and 4h after -- closer wall is 1h (left),
            // so longest centered D = 2 * 1h ≈ 2h.
            var window = (Start: transitSeed - TimeSpan.FromHours(1),
                          End:   transitSeed + TimeSpan.FromHours(4));

            DateTime transitForWindow = TransitTime.UtcAtOrAfter(Target.Default, loc, window.Start);
            TimeSpan leftRoom = transitForWindow - window.Start;
            TimeSpan rightRoom = window.End - transitForWindow;
            TimeSpan expectedRoom = leftRoom < rightRoom ? leftRoom : rightRoom;
            TimeSpan expectedDur = TimeSpan.FromTicks(expectedRoom.Ticks * 2);

            var result = SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, new[] { window });

            Assert.NotNull(result);
            // The closer wall should be left (1h vs 4h) regardless of sub-µs drift.
            Assert.True(leftRoom < rightRoom,
                $"Test setup invariant: leftRoom should be smaller. leftRoom={leftRoom}, rightRoom={rightRoom}");
            Assert.Equal(expectedDur, result.Value.Duration);
            Assert.Equal(transitForWindow - expectedRoom, result.Value.Start);
            Assert.Equal(transitForWindow + expectedRoom, result.Value.End);
            // Sanity: nominal 1h-min closer wall yields ~2h centered.
            Assert.True(Math.Abs((expectedDur - TimeSpan.FromHours(2)).TotalMilliseconds) < 1.0,
                $"Expected ~2h centered duration; got {expectedDur}");
        }

        [Fact]
        public void LongestDurationCenteredIn_Capped_ReturnsCap()
        {
            var loc = MakeLocation();
            DateTime transitSeed = TransitTime.UtcAtOrAfter(
                Target.Default, loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            // Window allowing up to 6h centered; cap to 3h.
            var window = (Start: transitSeed - TimeSpan.FromHours(3),
                          End:   transitSeed + TimeSpan.FromHours(3));

            DateTime transitForWindow = TransitTime.UtcAtOrAfter(Target.Default, loc, window.Start);
            var cap = TimeSpan.FromHours(3);
            TimeSpan halfCap = TimeSpan.FromTicks(cap.Ticks / 2);

            var result = SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, new[] { window }, cap);

            Assert.NotNull(result);
            Assert.Equal(cap, result.Value.Duration);
            Assert.Equal(transitForWindow - halfCap, result.Value.Start);
            Assert.Equal(transitForWindow + halfCap, result.Value.End);
        }

        [Fact]
        public void LongestDurationCenteredIn_EmptyCandidates_ReturnsNull()
        {
            var loc = MakeLocation();
            var windows = Array.Empty<(DateTime, DateTime)>();

            var result = SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, windows);

            Assert.Null(result);
        }

        [Fact]
        public void LongestDurationCenteredIn_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var windows = new[] { (Start: DateTime.UtcNow, End: DateTime.UtcNow.AddHours(2)) };

            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationCenteredIn(null, loc, windows));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationCenteredIn(Target.Default, null, windows));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LongestDurationCenteredIn(Target.Default, loc, null));
            // Non-positive cap is user-reachable; returns null instead of throwing.
            Assert.Null(SessionSolvers.LongestDurationCenteredIn(
                Target.Default, loc, windows, TimeSpan.Zero));
            Assert.Null(SessionSolvers.LongestDurationCenteredIn(
                Target.Default, loc, windows, TimeSpan.FromHours(-1)));
        }

        // ====================================================================
        // LongestDurationCentered (auto-resolve, strict transit-centered)
        // ====================================================================

        // Auto-resolve happy path: M31 transits during a Penns Park November night,
        // so the visibility window contains transit and a centered session fits.
        [Fact]
        public void LongestDurationCentered_NullProfile_ReturnsResultMatchingTransitGeometry()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            var result = SessionSolvers.LongestDurationCentered(
                Target.Default, loc, night, horizon, profile: null);

            Assert.NotNull(result);
            Assert.True(result.Value.Duration > TimeSpan.Zero);
            // The session is exactly centered on transit -- midpoint should equal transit
            // (modulo Ticks/2 truncation when duration has odd Ticks).
            DateTime midpoint = result.Value.Start.AddTicks((result.Value.End - result.Value.Start).Ticks / 2);
            DateTime transit = TransitTime.UtcAtOrAfter(Target.Default, loc, result.Value.Start);
            // Allow up to 1 tick of slack from the integer-half-ticks computation.
            Assert.True(Math.Abs((midpoint - transit).Ticks) <= 2,
                $"Midpoint {midpoint:O} should equal transit {transit:O}");
        }

        // Polar night: VisibilityWindows.For returns empty, no candidates.
        [Fact]
        public void LongestDurationCentered_PolarNight_ReturnsNull()
        {
            var loc = TestLocations.PennsPark.With(
                latitude: 80.0, north: true,
                longitude: 0.0, west: false);
            var solsticeSeed = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            var night = NightCalculator.ComputeNight(loc, solsticeSeed);
            var horizon = new ScalarHorizonProfile(20.0);

            var result = SessionSolvers.LongestDurationCentered(
                Target.Default, loc, night, horizon);

            Assert.Null(result);
        }

        // ====================================================================
        // LowestHorizonCentered (bisection on strict-centered fit)
        // ====================================================================

        // M31 at Penns Park yields 2h centered easily; the largest H that still fits
        // is well below meridian (~89°) and well above the test floor of 0°. For the
        // transit-in-window case the centered-fit and wall-pushed-fit predicates
        // coincide, so the returned H should be close to LowestHorizon's answer.
        [Fact]
        public void LowestHorizonCentered_TargetClearsHorizonComfortably_ReturnsLowAngle()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            var result = SessionSolvers.LowestHorizonCentered(
                Target.Default, loc, night, TimeSpan.FromHours(2));

            Assert.NotNull(result);
            Assert.True(result.Value.HorizonDeg > 50.0,
                $"Expected HorizonDeg > 50° (M31 fits 2h centered easily); got {result.Value.HorizonDeg:F2}°");
            Assert.True(result.Value.HorizonDeg < 89.0,
                $"Expected HorizonDeg < 89° (M31's meridian alt ~89°); got {result.Value.HorizonDeg:F2}°");
            Assert.True(result.Value.End > result.Value.Start);
        }

        [Fact]
        public void LowestHorizonCentered_DurationExceedsAvailableTime_ReturnsNull()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            var result = SessionSolvers.LowestHorizonCentered(
                Target.Default, loc, night, TimeSpan.FromHours(48));

            Assert.Null(result);
        }

        [Fact]
        public void LowestHorizonCentered_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var dur = TimeSpan.FromHours(2);

            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LowestHorizonCentered(null, loc, night, dur));
            Assert.Throws<ArgumentNullException>(() =>
                SessionSolvers.LowestHorizonCentered(Target.Default, null, night, dur));
            // Non-positive duration is user-reachable; returns null instead of throwing.
            Assert.Null(SessionSolvers.LowestHorizonCentered(Target.Default, loc, night, TimeSpan.Zero));
            Assert.Null(SessionSolvers.LowestHorizonCentered(Target.Default, loc, night, TimeSpan.FromHours(-1)));
            Assert.Throws<ArgumentException>(() =>
                SessionSolvers.LowestHorizonCentered(Target.Default, loc, night, dur, minHorizonDeg: -91.0));
            Assert.Throws<ArgumentException>(() =>
                SessionSolvers.LowestHorizonCentered(Target.Default, loc, night, dur, maxIterations: 0));
        }
    }
}
