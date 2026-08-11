using System;
using System.Linq;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class MeridianTests
    {
        private static readonly Location Loc = TestLocations.PennsPark;

        // TransitTime is the transit oracle -- Meridian composes it, so these tests pin
        // the composition semantics (sign convention, boundaries, shifted search), not
        // the LST inversion itself (TransitTimeTests owns that).
        //
        // The analytic inversion carries ~0.1 ms jitter across recomputations from
        // different seeds, so oracle-vs-computed instants compare within JitterTol
        // rather than tick-exact; boundary tests place windows >= 5 s clear of the
        // oracle transit so half-open comparisons are unambiguous.
        private static readonly TimeSpan JitterTol = TimeSpan.FromMilliseconds(10);

        private static DateTime Transit()
        {
            var seed = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
            return TransitTime.UtcAtOrAfter(Target.Default, Loc, seed);
        }

        private static void AssertClose(DateTime expected, DateTime actual)
            => Assert.InRange(Math.Abs((actual - expected).Ticks), 0, JitterTol.Ticks);

        // --- HourAngleAt / SideAt ---

        [Fact]
        public void HourAngleAt_SignConventionAroundTransit()
        {
            DateTime transit = Transit();
            // One solar hour ~= 1.0027 sidereal hours of hour angle.
            Assert.InRange(Meridian.HourAngleAt(Target.Default, Loc, transit.AddHours(-1)), -1.1, -0.9);
            Assert.InRange(Meridian.HourAngleAt(Target.Default, Loc, transit), -1e-6, 1e-6);
            Assert.InRange(Meridian.HourAngleAt(Target.Default, Loc, transit.AddHours(1)), 0.9, 1.1);
        }

        [Fact]
        public void HourAngleAt_AlwaysInSignedHalfOpenRange()
        {
            DateTime t0 = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
            for (int h = 0; h < 25; h++)
            {
                double ha = Meridian.HourAngleAt(Target.Default, Loc, t0.AddHours(h));
                Assert.InRange(ha, -12.0, 12.0);
                Assert.True(ha < 12.0, $"HA must be < +12 (half-open), got {ha}");
            }
        }

        [Fact]
        public void SideAt_EastBeforeTransit_WestAfter()
        {
            DateTime transit = Transit();
            Assert.Equal(MeridianSide.East, Meridian.SideAt(Target.Default, Loc, transit.AddMinutes(-10)));
            Assert.Equal(MeridianSide.West, Meridian.SideAt(Target.Default, Loc, transit.AddMinutes(10)));
        }

        // --- TransitsIn ---

        [Fact]
        public void TransitsIn_24hWindowJustBeforeTransit_HoldsTwo()
        {
            DateTime transit = Transit();
            var window = new UtcInterval(transit.AddMinutes(-1), transit.AddMinutes(-1).AddHours(24));

            var transits = Meridian.TransitsIn(Target.Default, Loc, window);

            Assert.Equal(2, transits.Count);
            AssertClose(transit, transits[0]);
            // Successive transits are one sidereal day apart (~23h56m04s).
            double gapHours = (transits[1] - transits[0]).TotalHours;
            Assert.InRange(gapHours, 23.92, 23.95);
        }

        [Fact]
        public void TransitsIn_WindowClearOfTransit_Empty()
        {
            DateTime transit = Transit();
            // Ends 5 s before the transit: excluded even under recomputation jitter.
            Assert.Empty(Meridian.TransitsIn(Target.Default, Loc,
                new UtcInterval(transit.AddHours(-2), transit.AddSeconds(-5))));
            // Starts 5 s after the transit: the next transit is ~a sidereal day away.
            Assert.Empty(Meridian.TransitsIn(Target.Default, Loc,
                new UtcInterval(transit.AddSeconds(5), transit.AddHours(2))));
        }

        [Fact]
        public void TransitsIn_WindowSpanningTransit_FindsIt()
        {
            DateTime transit = Transit();
            var transits = Meridian.TransitsIn(Target.Default, Loc,
                new UtcInterval(transit.AddSeconds(-5), transit.AddHours(1)));
            Assert.Single(transits);
            AssertClose(transit, transits[0]);
        }

        // --- FlipTimeIn ---

        [Fact]
        public void FlipTimeIn_TransitInsideSession_ReturnsShiftedTransit()
        {
            DateTime transit = Transit();
            var session = new UtcInterval(transit.AddHours(-2), transit.AddHours(2));
            DateTime? flip = Meridian.FlipTimeIn(Target.Default, Loc, session, TimeSpan.FromHours(1));
            Assert.NotNull(flip);
            AssertClose(transit.AddHours(1), flip.Value);
        }

        [Fact]
        public void FlipTimeIn_PreSessionTransit_InSessionFlip_Found()
        {
            // The case a naive "transit inside session" search misses: transit is 10
            // minutes BEFORE the session, but the flip (transit + 60 min) is inside it.
            DateTime transit = Transit();
            var session = new UtcInterval(transit.AddMinutes(10), transit.AddHours(2));
            DateTime? flip = Meridian.FlipTimeIn(Target.Default, Loc, session, TimeSpan.FromHours(1));
            Assert.NotNull(flip);
            AssertClose(transit.AddHours(1), flip.Value);
        }

        [Fact]
        public void FlipTimeIn_NoFlipInSession_Null()
        {
            DateTime transit = Transit();
            var session = new UtcInterval(transit.AddHours(2), transit.AddHours(4));
            Assert.Null(Meridian.FlipTimeIn(Target.Default, Loc, session, TimeSpan.FromHours(1)));
        }

        [Fact]
        public void FlipTimeIn_NegativeAllowance_FlipBeforeTransit()
        {
            DateTime transit = Transit();
            var session = new UtcInterval(transit.AddHours(-1), transit.AddSeconds(-5));
            DateTime? flip = Meridian.FlipTimeIn(Target.Default, Loc, session, TimeSpan.FromMinutes(-30));
            Assert.NotNull(flip);
            AssertClose(transit.AddMinutes(-30), flip.Value);
        }

        // --- SplitAtFlip ---

        [Fact]
        public void SplitAtFlip_WindowStraddlingFlip_SplitsInTwo()
        {
            DateTime transit = Transit();
            var windows = new[] { new UtcInterval(transit.AddHours(-2), transit.AddHours(2)) };

            var pieces = Meridian.SplitAtFlip(Target.Default, Loc, windows, TimeSpan.FromHours(1));

            Assert.Equal(2, pieces.Count);
            // Pieces meet exactly (internal consistency is tick-exact) at ~transit+1h.
            Assert.Equal(pieces[0].End, pieces[1].Start);
            AssertClose(transit.AddHours(1), pieces[0].End);
            Assert.Equal(windows[0].Start, pieces[0].Start);
            Assert.Equal(windows[0].End, pieces[1].End);
            // Total covered time preserved exactly.
            Assert.Equal(windows[0].Duration, TimeSpan.FromTicks(pieces.Sum(p => p.Duration.Ticks)));
        }

        [Fact]
        public void SplitAtFlip_FlipOnWindowBoundary_NoSplit()
        {
            // Window starting at ~the flip instant: the split tolerance absorbs the
            // recomputation jitter, so no sub-second sliver is emitted -- the
            // replanning re-split path.
            DateTime transit = Transit();
            var windows = new[] { new UtcInterval(transit.AddHours(1), transit.AddHours(3)) };
            var pieces = Meridian.SplitAtFlip(Target.Default, Loc, windows, TimeSpan.FromHours(1));
            Assert.Equal(windows, pieces);
        }

        [Fact]
        public void SplitAtFlip_Resplit_IsNoOp()
        {
            // Chained use: splitting the output of a previous split changes nothing.
            DateTime transit = Transit();
            var windows = new[] { new UtcInterval(transit.AddHours(-2), transit.AddHours(2)) };
            var once = Meridian.SplitAtFlip(Target.Default, Loc, windows, TimeSpan.FromHours(1));
            var twice = Meridian.SplitAtFlip(Target.Default, Loc, once, TimeSpan.FromHours(1));
            Assert.Equal(once, twice);
        }

        [Fact]
        public void SplitAtFlip_NoFlipNearWindow_Unchanged()
        {
            DateTime transit = Transit();
            var windows = new[] { new UtcInterval(transit.AddHours(2), transit.AddHours(4)) };
            var pieces = Meridian.SplitAtFlip(Target.Default, Loc, windows, TimeSpan.FromHours(1));
            Assert.Equal(windows, pieces);
        }

        [Fact]
        public void SplitAtFlip_NonCanonicalInput_Throws()
        {
            DateTime transit = Transit();
            var overlapping = new[]
            {
                new UtcInterval(transit.AddHours(-2), transit.AddHours(1)),
                new UtcInterval(transit, transit.AddHours(2)),
            };
            Assert.Throws<ArgumentException>(
                () => Meridian.SplitAtFlip(Target.Default, Loc, overlapping, TimeSpan.FromHours(1)));
        }
    }
}
