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
            => TestLocations.PennsPark.With(
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
            var loc = TestLocations.PennsPark.With(
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

        // ---- Profile-aware refinement (non-scalar IHorizonProfile) ----

        [Fact]
        public void For_PolylineConstantHorizon_MatchesScalarToWithinASecond()
        {
            // A polyline whose samples are all equal should produce the same windows
            // as ScalarHorizonProfile(samples[0]) -- the bisection refinement converges
            // to the closed-form crossing.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var scalar = new ScalarHorizonProfile(20.0);
            var flatPolyline = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0, 180.0, 270.0 },
                altitudesDeg: new[] { 20.0,  20.0,  20.0,  20.0 });

            var sw = VisibilityWindows.For(Target.Default, loc, night, scalar);
            var pw = VisibilityWindows.For(Target.Default, loc, night, flatPolyline);

            Assert.Equal(sw.Count, pw.Count);
            for (int i = 0; i < sw.Count; i++)
            {
                Assert.True(Math.Abs((sw[i].Start - pw[i].Start).TotalSeconds) < 1.0,
                    $"window {i} Start: scalar={sw[i].Start:O} polyline={pw[i].Start:O}");
                Assert.True(Math.Abs((sw[i].End - pw[i].End).TotalSeconds) < 1.0,
                    $"window {i} End: scalar={sw[i].End:O} polyline={pw[i].End:O}");
            }
        }

        [Fact]
        public void For_PolylineRidge_ShrinksWindowVsScalarBaseline()
        {
            // M42 (Dec -5.4) transits the south at ~44 deg from Penns Park. A polyline
            // with a 35-deg ridge spanning the southern azimuth band cuts into the
            // visible time vs the scalar 20-deg baseline.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var m42 = Target.Default.With(
                name: "M42", rightAscension: 5.588139, declination: 5.391, north: false);
            var scalar = new ScalarHorizonProfile(20.0);
            var ridge = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {   0.0,  90.0, 150.0, 180.0, 210.0, 270.0 },
                altitudesDeg: new[] {  20.0,  20.0,  35.0,  35.0,  35.0,  20.0 });

            var scalarWindows = VisibilityWindows.For(m42, loc, night, scalar);
            var ridgeWindows  = VisibilityWindows.For(m42, loc, night, ridge);

            Assert.NotEmpty(scalarWindows);
            Assert.NotEmpty(ridgeWindows);

            TimeSpan scalarTotal = TimeSpan.Zero;
            foreach (var w in scalarWindows) scalarTotal += w.End - w.Start;
            TimeSpan ridgeTotal = TimeSpan.Zero;
            foreach (var w in ridgeWindows)  ridgeTotal  += w.End - w.Start;

            Assert.True(ridgeTotal < scalarTotal,
                $"ridge profile should reduce visible time: scalar={scalarTotal.TotalMinutes:F1} min, ridge={ridgeTotal.TotalMinutes:F1} min");
        }

        [Fact]
        public void For_RidgeCutsTransit_SplitsIntoRisingAndSettingWindows()
        {
            // M42 transits south at ~44 deg from Penns Park. An obstruction-table profile
            // that places a 50-deg sector at azimuth 175-185 sits ABOVE the target's transit
            // altitude, splitting the single scalar window into a rising-side and a
            // setting-side sub-window.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var m42 = Target.Default.With(
                name: "M42", rightAscension: 5.588139, declination: 5.391, north: false);
            var profile = new ObstructionTableHorizonProfile(new[]
            {
                ( AzimuthDeg:   0.0, AltitudeDeg: 20.0 ),
                ( AzimuthDeg: 175.0, AltitudeDeg: 50.0 ),
                ( AzimuthDeg: 185.0, AltitudeDeg: 20.0 ),
            });

            var scalarWindows  = VisibilityWindows.For(m42, loc, night, new ScalarHorizonProfile(20.0));
            var profileWindows = VisibilityWindows.For(m42, loc, night, profile);

            Assert.Single(scalarWindows);                       // baseline: one window
            Assert.Equal(2, profileWindows.Count);              // ridge splits it
            Assert.True(profileWindows[0].End < profileWindows[1].Start);
            // Both sub-windows are inside the scalar baseline.
            Assert.True(profileWindows[0].Start >= scalarWindows[0].Start);
            Assert.True(profileWindows[1].End   <= scalarWindows[0].End);
        }

        [Fact]
        public void For_PolylineProfile_NeverRisesTarget_StaysEmpty()
        {
            // The profile path's outer-envelope short-circuit must survive: a target
            // that never reaches MinAltitude returns empty without invoking the scan.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var southern = Target.Default.With(declination: 80.0, north: false);
            var profile = new PolylineHorizonProfile(
                new[] {  0.0,  90.0, 180.0, 270.0 },
                new[] { 20.0,  20.0,  20.0,  20.0 });

            Assert.Empty(VisibilityWindows.For(southern, loc, night, profile));
        }
    }
}
