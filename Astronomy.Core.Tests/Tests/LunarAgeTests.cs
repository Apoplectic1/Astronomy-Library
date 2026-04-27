using System;
using Astronomy.Core.Moon;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Sanity guard for LunarAge.DaysAt: at the reference epoch the age should be ~0;
    // half a synodic period later it should be ~14.77 (full moon territory); a full
    // period later it should wrap back to ~0. Argument validation rejects non-Utc Kind.
    public class LunarAgeTests
    {
        private static readonly DateTime ReferenceEpoch =
            new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

        [Fact]
        public void DaysAt_ReferenceEpoch_ReturnsApproximatelyZero()
        {
            // The reference JD constant matches the JD of the reference instant to
            // sub-second precision. Allow 15 minutes of slack to absorb any float
            // residual + the constant's 7-decimal truncation.
            double age = LunarAge.DaysAt(ReferenceEpoch);
            Assert.InRange(age, 0.0, 15.0 / (24.0 * 60.0));
        }

        [Fact]
        public void DaysAt_HalfCycleAfterReference_NearFullMoon()
        {
            DateTime halfCycle = ReferenceEpoch.AddDays(LunarAge.SynodicMonthDays / 2.0);
            double age = LunarAge.DaysAt(halfCycle);
            // Should be very close to half the synodic period (~14.77).
            Assert.InRange(age, 14.7, 14.8);
        }

        [Fact]
        public void DaysAt_FullCycleAfterReference_WrapsToZero()
        {
            DateTime nextCycle = ReferenceEpoch.AddDays(LunarAge.SynodicMonthDays);
            double age = LunarAge.DaysAt(nextCycle);
            // Wraps via modulo. Either ~0 or ~SynodicMonthDays (if float subtraction
            // gave a tiny negative that didn't trigger the +cycle rebound, the modulo
            // would give negative; we add SynodicMonthDays to get ~SynodicMonthDays).
            // Tolerance is 15 minutes either side of either pole.
            double slack = 15.0 / (24.0 * 60.0);
            bool nearZero = age < slack;
            bool nearCycle = age > LunarAge.SynodicMonthDays - slack;
            Assert.True(nearZero || nearCycle, $"expected ~0 or ~SynodicMonthDays, got {age}");
        }

        [Fact]
        public void DaysAt_BeforeReferenceEpoch_StillInRange()
        {
            DateTime beforeRef = ReferenceEpoch.AddDays(-LunarAge.SynodicMonthDays);
            double age = LunarAge.DaysAt(beforeRef);
            Assert.InRange(age, 0.0, LunarAge.SynodicMonthDays);
        }

        [Fact]
        public void DaysAt_NonUtcKind_Throws()
        {
            DateTime localKind = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
            Assert.Throws<ArgumentException>(() => LunarAge.DaysAt(localKind));
        }

        [Fact]
        public void DaysAt_UnspecifiedKind_Throws()
        {
            DateTime unspecified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            Assert.Throws<ArgumentException>(() => LunarAge.DaysAt(unspecified));
        }

        [Fact]
        public void NewMoonReferenceJd_MatchesJulianDateFromUtc()
        {
            // Sanity check: the hardcoded reference JD constant must match what
            // JulianDate.FromUtc returns for the reference instant. A drift here would
            // mean DaysAt at the reference epoch wraps to ~SynodicMonthDays via the
            // modulo, breaking every downstream consumer.
            double computedJd = JulianDate.FromUtc(ReferenceEpoch);
            Assert.Equal(LunarAge.NewMoonReferenceJd, computedJd, 4);
        }
    }
}
