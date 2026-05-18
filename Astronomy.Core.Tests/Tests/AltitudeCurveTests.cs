using System;
using Astronomy.Core;
using Astronomy.Core.Locations;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Correctness guard for AltitudeCurve.Sample: its output must match a per-minute
    // AltAzCalculator.At loop to well below chart pixel resolution at every sample. The two
    // paths share the underlying TargetGeometry.AltitudeAtHourAngle formula; the only place
    // they can diverge is the LST computation (per-sample SiderealTime.Local in the baseline
    // vs one-shot + linear advance in AltitudeCurve). GMST is linear in UT to far below
    // arcsecond precision over a night, so the expected agreement is ~nanodegrees. A loose
    // 1e-6 degree tolerance leaves margin for any future refactor of either side without
    // masking a real divergence.
    public class AltitudeCurveTests
    {
        [Theory]
        [InlineData(600)]   // typical Day-chart night
        [InlineData(1000)]  // long winter night
        [InlineData(6000)]  // stress
        public void Sample_MatchesPerMinuteAltAz(int count)
        {
            Target target = Target.Default;
            Location location = TestLocations.PennsPark;
            DateTime startUtc = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
            TimeSpan step = TimeSpan.FromMinutes(1);

            var batched = AltitudeCurve.Sample(target, location, startUtc, step, count);

            for (int i = 0; i < count; i++)
            {
                DateTime point = startUtc.Add(TimeSpan.FromTicks(step.Ticks * i));
                double expected = AltAzCalculator
                    .At(target, location, point).Altitude;
                double actual = batched[i];
                Assert.True(
                    Math.Abs(expected - actual) < 1e-6,
                    $"sample {i}: expected {expected}, got {actual}, delta {expected - actual}");
            }
        }

        // The same per-sample identity must hold at any latitude / longitude
        // (the kernel is the same TargetGeometry.AltitudeAtHourAngle either
        // way; only the LST seed and stride differ). One fixed count is
        // sufficient -- the count-dimensional stress lives in
        // Sample_MatchesPerMinuteAltAz above; this Theory adds a
        // latitude-dimensional stress instead.
        [Theory]
        [MemberData(nameof(TestLocations.All), MemberType = typeof(TestLocations))]
        public void Sample_MatchesPerMinuteAltAz_AcrossLocations(
            string locationName, Location location)
        {
            const int count = 1000;
            Target target = Target.Default;
            DateTime startUtc = new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc);
            TimeSpan step = TimeSpan.FromMinutes(1);

            var batched = AltitudeCurve.Sample(target, location, startUtc, step, count);

            for (int i = 0; i < count; i++)
            {
                DateTime point = startUtc.Add(TimeSpan.FromTicks(step.Ticks * i));
                double expected = AltAzCalculator
                    .At(target, location, point).Altitude;
                double actual = batched[i];
                Assert.True(
                    Math.Abs(expected - actual) < 1e-6,
                    $"[{locationName}] sample {i}: expected {expected}, got {actual}, delta {expected - actual}");
            }
        }

        [Fact]
        public void Sample_CountZero_ReturnsEmpty()
        {
            var result = AltitudeCurve.Sample(
                Target.Default, TestLocations.PennsPark,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), count: 0);

            Assert.Empty(result);
        }

        [Fact]
        public void Sample_NegativeCount_Throws()
        {
            Assert.Throws<ArgumentException>(() => AltitudeCurve.Sample(
                Target.Default, TestLocations.PennsPark,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.FromMinutes(1), count: -1));
        }

        [Fact]
        public void Sample_NonPositiveStep_Throws()
        {
            Assert.Throws<ArgumentException>(() => AltitudeCurve.Sample(
                Target.Default, TestLocations.PennsPark,
                new DateTime(2026, 11, 15, 22, 0, 0, DateTimeKind.Utc),
                TimeSpan.Zero, count: 10));
        }
    }
}
