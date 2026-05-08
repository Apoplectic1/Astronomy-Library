using System;
using System.Collections.Generic;
using System.Linq;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct tests for TargetOrdering.ByTransit / ByRise. The two methods drive
    // scheduler-UI sort order so the contract details (Circumpolar bubbles up,
    // NeverRises sinks, null entries skipped) need pinning.
    public class TargetOrderingTests
    {
        private static Location MakeLocation()
            => TestLocations.PennsPark.With(
                dateTime: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        // M31 (RA 0.7h), M42 (RA 5.6h), M81 (RA 9.9h), M51 (RA 13.5h) are spread
        // around the sky and provide a non-trivial ordering by transit / rise.
        private static readonly Target M31 = Target.Default;
        private static readonly Target M42 = Target.Default.With(
            name: "M42", rightAscension: 5.588139, declination: -5.391, north: false);
        private static readonly Target M81 = Target.Default.With(
            name: "M81", rightAscension: 9.925889, declination: 69.065, north: true);
        private static readonly Target M51 = Target.Default.With(
            name: "M51", rightAscension: 13.498, declination: 47.195, north: true);

        // ---- ByTransit ----

        [Fact]
        public void ByTransit_OrdersTargetsByAscendingNextTransit()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            // Input deliberately out of natural transit order.
            var input = new[] { M51, M31, M81, M42 };

            var ordered = TargetOrdering.ByTransit(input, loc, search);

            Assert.Equal(input.Length, ordered.Count);
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                DateTime a = TransitTime.UtcAtOrAfter(ordered[i],     loc, search);
                DateTime b = TransitTime.UtcAtOrAfter(ordered[i + 1], loc, search);
                Assert.True(a <= b,
                    $"transit order broken at index {i}: {ordered[i].Name}@{a:O} > {ordered[i+1].Name}@{b:O}");
            }
        }

        [Fact]
        public void ByTransit_NullEntriesAreDropped()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            IEnumerable<Target> input = new Target[] { M31, null, M42, null, M81 };

            var ordered = TargetOrdering.ByTransit(input, loc, search);

            Assert.Equal(3, ordered.Count);
            Assert.DoesNotContain(null, ordered);
        }

        [Fact]
        public void ByTransit_EmptyInput_ReturnsEmpty()
        {
            var loc = MakeLocation();
            var ordered = TargetOrdering.ByTransit(
                Array.Empty<Target>(), loc, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

            Assert.Empty(ordered);
        }

        [Fact]
        public void ByTransit_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            Assert.Throws<ArgumentNullException>(() =>
                TargetOrdering.ByTransit(null, loc, search));
            Assert.Throws<ArgumentNullException>(() =>
                TargetOrdering.ByTransit(new[] { M31 }, null, search));
        }

        // ---- ByRise ----

        [Fact]
        public void ByRise_CircumpolarBubblesToFront()
        {
            // Polaris-like target is circumpolar at Penns Park; ByRise maps that
            // to DateTime.MinValue and sorts it first.
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var polaris = Target.Default.With(
                name: "Polaris",
                rightAscension: 2.530194, declination: 89.264111, north: true);
            var input = new[] { M31, M42, polaris };

            var ordered = TargetOrdering.ByRise(input, loc, search, horizonDeg: 20.0);

            Assert.Equal(polaris.Name, ordered[0].Name);
        }

        [Fact]
        public void ByRise_NeverRisesSinksToEnd()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var southern = Target.Default.With(
                name: "deep south", declination: 80.0, north: false);
            var input = new[] { M31, southern, M42 };

            var ordered = TargetOrdering.ByRise(input, loc, search, horizonDeg: 20.0);

            Assert.Equal(southern.Name, ordered[ordered.Count - 1].Name);
        }

        [Fact]
        public void ByRise_FoundTargetsOrderedByAscendingRise()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            var input = new[] { M51, M31, M81, M42 };

            var ordered = TargetOrdering.ByRise(input, loc, search, horizonDeg: 20.0);

            // All four are Found at 20-deg horizon from Penns Park; check
            // monotonic non-decreasing rise UTC across the whole list.
            DateTime? prev = null;
            foreach (var t in ordered)
            {
                var (state, rise, _) = RiseSet.NextAtOrAfter(t, loc, search, 20.0);
                Assert.Equal(RiseSetState.Found, state);
                if (prev.HasValue)
                    Assert.True(rise.Value >= prev.Value,
                        $"rise order broken: {prev:O} > {rise:O}");
                prev = rise;
            }
        }

        [Fact]
        public void ByRise_NullEntriesAreDropped()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            IEnumerable<Target> input = new Target[] { M31, null, M42 };

            var ordered = TargetOrdering.ByRise(input, loc, search, horizonDeg: 20.0);

            Assert.Equal(2, ordered.Count);
            Assert.DoesNotContain(null, ordered);
        }

        [Fact]
        public void ByRise_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var search = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

            Assert.Throws<ArgumentNullException>(() =>
                TargetOrdering.ByRise(null, loc, search, 20.0));
            Assert.Throws<ArgumentNullException>(() =>
                TargetOrdering.ByRise(new[] { M31 }, null, search, 20.0));
        }
    }
}
