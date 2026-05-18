using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct tests for RiseSet.NextAtOrAfter. The scalar overload is analytic; the
    // IHorizonProfile overload uses a scalar seed plus bisection refinement against
    // the profile -- both branches need direct coverage so a future bisection /
    // tri-state refactor doesn't slip past the visibility-window tests that consume
    // them.
    public class RiseSetTests
    {
        private static Location MakeLocation(int year = 2026, int month = 11, int day = 15)
            => TestLocations.PennsPark;

        // ---- Scalar overload ----

        [Fact]
        public void Scalar_M31AtPennsPark_ReturnsFoundWithRiseAndSet()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc);

            var (state, rise, set) = RiseSet.NextAtOrAfter(Target.Default, loc, search, 20.0);

            Assert.Equal(RiseSetState.Found, state);
            Assert.True(rise.HasValue);
            Assert.True(set.HasValue);
            Assert.Equal(DateTimeKind.Utc, rise.Value.Kind);
            Assert.Equal(DateTimeKind.Utc, set.Value.Kind);
            Assert.True(rise.Value >= search,
                $"rise {rise:O} must be at or after searchFromUtc {search:O}");
            Assert.True(set.Value > rise.Value,
                $"set {set:O} must follow rise {rise:O}");
        }

        [Fact]
        public void Scalar_NeverRisesTarget_ReturnsNeverRises()
        {
            // Far-southern target as seen from northern latitude.
            var loc = MakeLocation();
            var southern = Target.Default.With(declination: 80.0, north: false);
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            var (state, rise, set) = RiseSet.NextAtOrAfter(southern, loc, search, 20.0);

            Assert.Equal(RiseSetState.NeverRises, state);
            Assert.Null(rise);
            Assert.Null(set);
        }

        [Fact]
        public void Scalar_CircumpolarTarget_ReturnsCircumpolar()
        {
            var loc = MakeLocation();
            var polaris = Target.Default.With(
                rightAscension: 2.530194, declination: 89.264111, north: true);
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            // Use a low horizon so Polaris (~89.3 dec, ~40 lat) clearly clears it.
            var (state, rise, set) = RiseSet.NextAtOrAfter(polaris, loc, search, 20.0);

            Assert.Equal(RiseSetState.Circumpolar, state);
            Assert.Null(rise);
            Assert.Null(set);
        }

        [Fact]
        public void Scalar_RiseAlreadyPast_RollsToNextSiderealCycle()
        {
            // A search instant just past the target's rise. The rise belonging to
            // this transit cycle is in the past, so NextAtOrAfter must roll to the
            // next sidereal cycle (~23.93 solar hours later).
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var firstResult = RiseSet.NextAtOrAfter(Target.Default, loc, search, 20.0);
            Assert.Equal(RiseSetState.Found, firstResult.State);
            DateTime firstRise = firstResult.Rise.Value;

            // Search again from one minute after the first rise -- that rise is
            // now in the past, so the result must be next-cycle's rise.
            DateTime laterSearch = firstRise.AddMinutes(1);
            var second = RiseSet.NextAtOrAfter(Target.Default, loc, laterSearch, 20.0);

            Assert.Equal(RiseSetState.Found, second.State);
            Assert.True(second.Rise.Value >= laterSearch,
                $"second.Rise {second.Rise:O} must be at or after laterSearch {laterSearch:O}");
            // Roughly one sidereal day later (~23h 56m).
            TimeSpan delta = second.Rise.Value - firstRise;
            Assert.True(delta.TotalHours > 23.0 && delta.TotalHours < 24.5,
                $"expected ~23.9h gap between rises; got {delta.TotalHours:F3}h");
        }

        [Fact]
        public void Scalar_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            Assert.Throws<ArgumentNullException>(() =>
                RiseSet.NextAtOrAfter(null, loc, search, 20.0));
            Assert.Throws<ArgumentNullException>(() =>
                RiseSet.NextAtOrAfter(Target.Default, null, search, 20.0));
        }

        // ---- IHorizonProfile overload ----

        [Fact]
        public void Profile_FlatHorizon_MatchesScalarOverload()
        {
            // A flat ScalarHorizonProfile must produce a rise/set within sub-second
            // of the scalar overload at the same altitude (the bisection refinement
            // converges to the analytic answer).
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            var scalar = RiseSet.NextAtOrAfter(Target.Default, loc, search, 20.0);
            var profile = RiseSet.NextAtOrAfter(
                Target.Default, loc, search, new ScalarHorizonProfile(20.0));

            Assert.Equal(scalar.State, profile.State);
            Assert.True(Math.Abs((scalar.Rise.Value - profile.Rise.Value).TotalSeconds) < 1.0,
                "rise should match within a second");
            Assert.True(Math.Abs((scalar.Set.Value - profile.Set.Value).TotalSeconds) < 1.0,
                "set should match within a second");
        }

        [Fact]
        public void Profile_PolylineRidge_DiffersFromScalarSeed()
        {
            // Polyline profile with a 60-deg ridge at northeast (azimuth 45) and
            // 20-deg minimum elsewhere. M31 rises in the east-northeast at Penns
            // Park, so its profile-aware rise should be appreciably later than the
            // scalar 20-deg seed.
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var ridge = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0, 30.0, 45.0, 60.0,  90.0, 180.0, 270.0 },
                altitudesDeg: new[] { 20.0, 20.0, 60.0, 20.0,  20.0,  20.0,  20.0 });

            var scalar  = RiseSet.NextAtOrAfter(Target.Default, loc, search, 20.0);
            var profile = RiseSet.NextAtOrAfter(Target.Default, loc, search, ridge);

            Assert.Equal(RiseSetState.Found, profile.State);
            Assert.True(profile.Rise.Value > scalar.Rise.Value,
                $"ridge rise {profile.Rise:O} should be later than scalar {scalar.Rise:O}");
        }

        [Fact]
        public void Profile_NeverRises_ShortCircuitsBeforeBisection()
        {
            // Profile-aware overload short-circuits when the scalar seed reports
            // NeverRises (no rise to refine).
            var loc = MakeLocation();
            var southern = Target.Default.With(declination: 80.0, north: false);
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            var (state, rise, set) = RiseSet.NextAtOrAfter(
                southern, loc, search, new ScalarHorizonProfile(20.0));

            Assert.Equal(RiseSetState.NeverRises, state);
            Assert.Null(rise);
            Assert.Null(set);
        }

        [Fact]
        public void Profile_Circumpolar_ShortCircuitsBeforeBisection()
        {
            var loc = MakeLocation();
            var polaris = Target.Default.With(
                rightAscension: 2.530194, declination: 89.264111, north: true);
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            var (state, rise, set) = RiseSet.NextAtOrAfter(
                polaris, loc, search, new ScalarHorizonProfile(20.0));

            Assert.Equal(RiseSetState.Circumpolar, state);
            Assert.Null(rise);
            Assert.Null(set);
        }

        [Fact]
        public void Profile_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var horizon = new ScalarHorizonProfile(20.0);

            Assert.Throws<ArgumentNullException>(() =>
                RiseSet.NextAtOrAfter(null, loc, search, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                RiseSet.NextAtOrAfter(Target.Default, null, search, horizon));
            Assert.Throws<ArgumentNullException>(() =>
                RiseSet.NextAtOrAfter(Target.Default, loc, search, (IHorizonProfile)null));
        }
    }
}
