using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct edge-case coverage for VisibilityWindows.For. The Session and BestSession
    // tests exercise it transitively; these tests pin the public contract: Kind=Utc on
    // outputs, dusk/dawn clamping, the three "circumpolar above" / "never rises" /
    // "night invalid" early-out branches, and the null contract.
    public class VisibilityWindowsTests
    {
        private static Location MakeLocation(int year = 2026, int month = 11, int day = 15)
            => Location.Default.With(
                dateTime: new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));

        [Fact]
        public void For_M31AtPennsParkInNovember_ReturnsOneWindow()
        {
            // Canonical "well-placed for the night" case. M31 (RA 0.7h, Dec +41°) sits
            // near transit shortly after sunset in mid-November at Penns Park.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var windows = VisibilityWindows.For(Target.Default, loc, night, horizon);

            Assert.Single(windows);
            Assert.True(windows[0].End > windows[0].Start);
            Assert.Equal(DateTimeKind.Utc, windows[0].Start.Kind);
            Assert.Equal(DateTimeKind.Utc, windows[0].End.Kind);
        }

        [Fact]
        public void For_M31_StartAndEndAreClampedToDuskAndDawn()
        {
            // The Max(lstDusk, ahStart) / Min(lstDawn, ahEnd) idiom guarantees that
            // any returned interval is contained in [dusk, dawn]. This is the
            // observable consequence of the "inclusive boundary" semantics.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var windows = VisibilityWindows.For(Target.Default, loc, night, horizon);

            Assert.NotEmpty(windows);
            foreach (var w in windows)
            {
                Assert.True(w.Start >= night.AstronomicalDusk,
                    $"window.Start {w.Start:O} preceded dusk {night.AstronomicalDusk:O}");
                Assert.True(w.End <= night.AstronomicalDawn,
                    $"window.End {w.End:O} followed dawn {night.AstronomicalDawn:O}");
            }
        }

        [Fact]
        public void For_CircumpolarTarget_ReturnsFullNightAsOneWindow()
        {
            // High-declination target near Polaris: above the horizon all night at
            // mid-northern latitudes. VisibilityWindows.For takes the
            // PositiveInfinity branch and returns a single (dusk, dawn) interval.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);
            var polaris = Target.Default.With(
                name: "Polaris", rightAscension: 2.530194, declination: 89.264111, north: true);

            var windows = VisibilityWindows.For(polaris, loc, night, horizon);

            Assert.Single(windows);
            Assert.Equal(night.AstronomicalDusk, windows[0].Start);
            Assert.Equal(night.AstronomicalDawn, windows[0].End);
        }

        [Fact]
        public void For_NeverRisesTarget_ReturnsZeroWindows()
        {
            // Far-southern declination at a northern latitude: the target never
            // reaches the horizon altitude. HourAngleAtAltitude returns NaN and
            // VisibilityWindows.For takes the early-out branch.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);
            var southern = Target.Default.With(
                name: "deep south", rightAscension: 6.0, declination: 80.0, north: false);

            var windows = VisibilityWindows.For(southern, loc, night, horizon);

            Assert.Empty(windows);
        }

        [Fact]
        public void For_PolarDay_ReturnsZeroWindows()
        {
            // Above the Arctic Circle at June solstice -- no astronomical night.
            // NightWindow.IsValid is false, so VisibilityWindows.For early-outs
            // before doing any geometry work.
            var loc = Location.Default.With(
                latitude: 80.0, north: true,
                dateTime: new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.False(night.IsValid);
            Assert.Empty(VisibilityWindows.For(Target.Default, loc, night, horizon));
        }

        [Fact]
        public void For_HighHorizon_ShrinksOrEliminatesWindow()
        {
            // Raising the horizon profile to 70° forces M31 (max altitude ~71° at
            // Penns Park) into a vanishingly narrow window or no window at all.
            // Either outcome is acceptable; the contract is that the window can
            // only get smaller as the horizon rises.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var lowHorizon = new ScalarHorizonProfile(20.0);
            var highHorizon = new ScalarHorizonProfile(70.0);

            var lowWindows = VisibilityWindows.For(Target.Default, loc, night, lowHorizon);
            var highWindows = VisibilityWindows.For(Target.Default, loc, night, highHorizon);

            Assert.NotEmpty(lowWindows);
            TimeSpan lowDuration = lowWindows[0].End - lowWindows[0].Start;
            TimeSpan highDuration = highWindows.Count == 0
                ? TimeSpan.Zero
                : highWindows[0].End - highWindows[0].Start;
            Assert.True(highDuration < lowDuration);
        }

        [Fact]
        public void For_ScalarHorizonProfile_IsHonored()
        {
            // ScalarHorizonProfile.MinAltitude feeds straight into the
            // HourAngleAtAltitude solve. Two different scalar horizons must
            // produce two different windows for the same target / night.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var lowHorizon = new ScalarHorizonProfile(15.0);
            var midHorizon = new ScalarHorizonProfile(40.0);

            var lowWindows = VisibilityWindows.For(Target.Default, loc, night, lowHorizon);
            var midWindows = VisibilityWindows.For(Target.Default, loc, night, midHorizon);

            Assert.NotEmpty(lowWindows);
            Assert.NotEmpty(midWindows);
            Assert.True(midWindows[0].Start > lowWindows[0].Start || midWindows[0].End < lowWindows[0].End,
                "Higher horizon should narrow the visibility window from at least one side");
        }

        [Fact]
        public void For_ResultIsImmutableReadOnlyList()
        {
            // Public contract: returned collection is IReadOnlyList<T>. Callers may
            // hold the reference indefinitely; another VisibilityWindows.For call
            // must not mutate the previously-returned list.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            var first = VisibilityWindows.For(Target.Default, loc, night, horizon);
            var second = VisibilityWindows.For(Target.Default, loc, night, horizon);

            Assert.NotSame(first, second);
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Start, second[i].Start);
                Assert.Equal(first[i].End, second[i].End);
            }
        }

        [Fact]
        public void For_NullTarget_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() =>
                VisibilityWindows.For(null, loc, night, horizon));
        }

        [Fact]
        public void For_NullLocation_Throws()
        {
            var night = NightCalculator.ComputeNight(MakeLocation());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() =>
                VisibilityWindows.For(Target.Default, null, night, horizon));
        }

        [Fact]
        public void For_NullHorizon_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);

            Assert.Throws<ArgumentNullException>(() =>
                VisibilityWindows.For(Target.Default, loc, night, null));
        }
    }
}
