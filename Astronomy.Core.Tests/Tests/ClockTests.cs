using System;
using Astronomy.Core.Time;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    public class ClockTests
    {
        private sealed class FixedClock : IClock
        {
            public DateTime UtcNow { get; init; }
        }

        [Fact]
        public void SystemClock_UtcNow_IsUtcKindAndTracksSystemClock()
        {
            DateTime before = DateTime.UtcNow;
            DateTime now = SystemClock.Instance.UtcNow;
            DateTime after = DateTime.UtcNow;

            Assert.Equal(DateTimeKind.Utc, now.Kind);
            Assert.InRange(now, before, after);
        }

        [Fact]
        public void ObservationMoment_Now_UsesInjectedClock()
        {
            var instant = new DateTime(2026, 9, 15, 3, 30, 0, DateTimeKind.Utc);
            var clock = new FixedClock { UtcNow = instant };

            var moment = ObservationMoment.Now(TimeZoneInfo.Utc, clock);

            Assert.Equal(instant, moment.Utc);
            Assert.Equal(TimeZoneInfo.Utc, moment.Zone);
        }

        [Fact]
        public void ObservationMoment_Now_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => ObservationMoment.Now(TimeZoneInfo.Utc, null!));
        }
    }
}
