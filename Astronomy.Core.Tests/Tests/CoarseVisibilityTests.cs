using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct coverage for CoarseVisibility's three predicates. Coarse pre-filter
    // semantics (single-window minDuration, scalar-MinAltitude conservatism)
    // matter at the IS / scheduler call site -- exercise them here.
    public class CoarseVisibilityTests
    {
        private static Location MakeLocation() => TestLocations.PennsPark;
        private static DateTime MakeSeed(int year = 2026, int month = 11, int day = 15)
            => new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        // ---- IsEverVisible (no horizon profile, alt >= 0 threshold) ----

        [Fact]
        public void IsEverVisible_M31AtPennsParkInNovember_ReturnsTrue()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            Assert.True(CoarseVisibility.IsEverVisible(Target.Default, loc, night));
        }

        [Fact]
        public void IsEverVisible_NeverRisesTarget_ReturnsFalse()
        {
            // Far-southern declination at a northern latitude -- target's max altitude
            // is below 0 deg, the closed-form HourAngleAtAltitude returns NaN.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var southern = Target.Default.With(declination: 80.0, north: false);

            Assert.False(CoarseVisibility.IsEverVisible(southern, loc, night));
        }

        [Fact]
        public void IsEverVisible_CircumpolarTarget_ReturnsTrue()
        {
            // Polaris-like (Dec ~89 N) -- always above horizon at temperate latitudes,
            // HourAngleAtAltitude returns +Infinity.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var polaris = Target.Default.With(
                rightAscension: 2.530194, declination: 89.264111, north: true);

            Assert.True(CoarseVisibility.IsEverVisible(polaris, loc, night));
        }

        [Fact]
        public void IsEverVisible_PolarDay_ReturnsFalse()
        {
            // No astronomical night above the Arctic Circle in mid-summer -- the
            // method short-circuits on !night.IsValid.
            var loc = TestLocations.PennsPark.With(latitude: 80.0, north: true);
            var solsticeSeed = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            var night = NightCalculator.ComputeNight(loc, solsticeSeed);

            Assert.False(night.IsValid);
            Assert.False(CoarseVisibility.IsEverVisible(Target.Default, loc, night));
        }

        [Fact]
        public void IsEverVisible_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());

            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsEverVisible(null, loc, night));
            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsEverVisible(Target.Default, null, night));
        }

        // ---- IsEverAboveHorizon (with horizon profile) ----

        [Fact]
        public void IsEverAboveHorizon_M31AbovePennsParkAt20Deg_ReturnsTrue()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.True(CoarseVisibility.IsEverAboveHorizon(Target.Default, loc, night, horizon));
        }

        [Fact]
        public void IsEverAboveHorizon_NeverClearsHighHorizon_ReturnsFalse()
        {
            // M42 (RA 5.6h, Dec -5.4) peaks at ~44 deg from Penns Park (lat 40.3 N).
            // A 60-deg horizon is well above its meridian altitude, so it never clears.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var m42 = Target.Default.With(
                name: "M42", rightAscension: 5.588139, declination: 5.391, north: false);
            var horizon = new ScalarHorizonProfile(60.0);

            Assert.False(CoarseVisibility.IsEverAboveHorizon(m42, loc, night, horizon));
        }

        [Fact]
        public void IsEverAboveHorizon_PolarDay_ReturnsFalse()
        {
            var loc = TestLocations.PennsPark.With(latitude: 80.0, north: true);
            var solsticeSeed = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            var night = NightCalculator.ComputeNight(loc, solsticeSeed);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.False(CoarseVisibility.IsEverAboveHorizon(Target.Default, loc, night, horizon));
        }

        [Fact]
        public void IsEverAboveHorizon_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsEverAboveHorizon(null, loc, night, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsEverAboveHorizon(Target.Default, null, night, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsEverAboveHorizon(Target.Default, loc, night, null));
        }

        // ---- IsAboveHorizonForAtLeast (single-window contract) ----

        [Fact]
        public void IsAboveHorizonForAtLeast_M31TwoHours_ReturnsTrue()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.True(CoarseVisibility.IsAboveHorizonForAtLeast(
                Target.Default, loc, night, horizon, TimeSpan.FromHours(2)));
        }

        [Fact]
        public void IsAboveHorizonForAtLeast_M31TwentyHours_ReturnsFalse()
        {
            // No 20-hour single window exists at Penns Park in November (full night
            // is ~13 hours); so this must fail even though M31 is well-placed.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.False(CoarseVisibility.IsAboveHorizonForAtLeast(
                Target.Default, loc, night, horizon, TimeSpan.FromHours(20)));
        }

        [Fact]
        public void IsAboveHorizonForAtLeast_NeverClearsHighHorizon_ReturnsFalse()
        {
            // M42 peaks at ~44 deg from Penns Park; a 60-deg horizon excludes it
            // entirely, so the "at least 1 minute above" predicate must fail.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var m42 = Target.Default.With(
                name: "M42", rightAscension: 5.588139, declination: 5.391, north: false);
            var horizon = new ScalarHorizonProfile(60.0);

            Assert.False(CoarseVisibility.IsAboveHorizonForAtLeast(
                m42, loc, night, horizon, TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void IsAboveHorizonForAtLeast_PolarDay_ReturnsFalse()
        {
            var loc = TestLocations.PennsPark.With(latitude: 80.0, north: true);
            var solsticeSeed = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
            var night = NightCalculator.ComputeNight(loc, solsticeSeed);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.False(CoarseVisibility.IsAboveHorizonForAtLeast(
                Target.Default, loc, night, horizon, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void IsAboveHorizonForAtLeast_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc, MakeSeed());
            var horizon = new ScalarHorizonProfile(20.0);
            var dur = TimeSpan.FromHours(1);

            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsAboveHorizonForAtLeast(null, loc, night, horizon, dur));
            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsAboveHorizonForAtLeast(Target.Default, null, night, horizon, dur));
            Assert.Throws<ArgumentNullException>(() =>
                CoarseVisibility.IsAboveHorizonForAtLeast(Target.Default, loc, night, null, dur));
        }
    }
}
