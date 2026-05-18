using System;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Pins the ObservationMoment record-struct contract: Utc-Kind invariant on factory
    // outputs, DST-aware local-to-UTC conversion through TimeZoneInfo, and the structural
    // equality semantics the `with` mutation idiom relies on.
    public class ObservationMomentTests
    {
        // US Eastern with DST rules -- exercises the autumn fall-back ambiguous window
        // (2026-11-01 01:00-02:00) and the spring-forward invalid window.
        private static TimeZoneInfo EasternTz =>
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

        [Fact]
        public void FromLocal_ConvertsWallClockThroughZoneRules()
        {
            // 2026-06-15 12:00 EDT (UTC-4) -> 16:00 UTC.
            DateTime local = new DateTime(2026, 6, 15, 12, 0, 0);
            ObservationMoment m = ObservationMoment.FromLocal(local, EasternTz);

            Assert.Equal(DateTimeKind.Utc, m.Utc.Kind);
            Assert.Equal(new DateTime(2026, 6, 15, 16, 0, 0, DateTimeKind.Utc), m.Utc);
            Assert.Same(EasternTz, m.Zone);
        }

        [Fact]
        public void FromLocal_AcrossDstFallBack_ResolvesAmbiguousLocalTime()
        {
            // 2026-11-01 01:30 in Eastern is ambiguous (occurs twice: once as 05:30 UTC under
            // EDT and again as 06:30 UTC under EST). TimeZoneInfo's documented behavior is to
            // resolve to the standard-time interpretation -- assert the round-trip lands on a
            // valid UTC instant within the two candidate windows rather than nailing the exact
            // platform choice.
            DateTime ambiguousLocal = new DateTime(2026, 11, 1, 1, 30, 0);
            ObservationMoment m = ObservationMoment.FromLocal(ambiguousLocal, EasternTz);

            Assert.Equal(DateTimeKind.Utc, m.Utc.Kind);
            Assert.InRange(
                m.Utc,
                new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc),    // EDT interpretation
                new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc));   // EST interpretation
        }

        [Fact]
        public void Now_ReturnsUtcKindAtCurrentInstant()
        {
            DateTime before = DateTime.UtcNow;
            ObservationMoment m = ObservationMoment.Now(TimeZoneInfo.Utc);
            DateTime after = DateTime.UtcNow;

            Assert.Equal(DateTimeKind.Utc, m.Utc.Kind);
            Assert.InRange(m.Utc, before, after);
            Assert.Same(TimeZoneInfo.Utc, m.Zone);
        }

        [Fact]
        public void Equality_IsStructuralOverUtcAndZoneIdentity()
        {
            DateTime utc = new DateTime(2026, 6, 15, 16, 0, 0, DateTimeKind.Utc);
            ObservationMoment a = new ObservationMoment(utc, TimeZoneInfo.Utc);
            ObservationMoment b = new ObservationMoment(utc, TimeZoneInfo.Utc);
            Assert.Equal(a, b);
            Assert.True(a == b);

            ObservationMoment c = a with { Utc = utc.AddSeconds(1) };
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void With_Syntax_MutatesIndividualFields()
        {
            DateTime utc = new DateTime(2026, 6, 15, 16, 0, 0, DateTimeKind.Utc);
            ObservationMoment m = new ObservationMoment(utc, TimeZoneInfo.Utc);

            ObservationMoment shifted = m with { Zone = EasternTz };
            Assert.Equal(utc, shifted.Utc);
            Assert.Same(EasternTz, shifted.Zone);
        }

        [Fact]
        public void FromLocal_NullZone_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ObservationMoment.FromLocal(new DateTime(2026, 6, 15, 12, 0, 0), null));
        }

        [Fact]
        public void Now_NullZone_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ObservationMoment.Now(null));
        }
    }
}
