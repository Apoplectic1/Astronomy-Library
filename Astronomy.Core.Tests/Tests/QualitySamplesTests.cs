using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct tests for QualitySamples.OverNight: slot grid construction, last-slot
    // truncation, Kind=Utc on returned timestamps, equivalence with
    // IntegratedQuality.OverSession on a per-slot basis, polar-day empty,
    // null/non-positive contracts.
    public class QualitySamplesTests
    {
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        private static Location MakeLocation(int year = 2026, int month = 11, int day = 15)
            => Location.Default.With(
                dateTime: new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));

        [Fact]
        public void OverNight_HourSlots_ProducesContiguousCoverageOfTheNight()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);

            var samples = QualitySamples.OverNight(
                Target.Default, loc, night, TimeSpan.FromHours(1), SinAltQuality);

            Assert.NotEmpty(samples);
            Assert.Equal(night.AstronomicalDusk, samples[0].Start);
            Assert.Equal(night.AstronomicalDawn, samples[samples.Count - 1].End);
            for (int i = 0; i < samples.Count - 1; i++)
            {
                Assert.Equal(samples[i].End, samples[i + 1].Start);
                Assert.Equal(DateTimeKind.Utc, samples[i].Start.Kind);
                Assert.Equal(DateTimeKind.Utc, samples[i].End.Kind);
            }
        }

        [Fact]
        public void OverNight_FullSlotsAreSlotSizeWide_LastSlotIsTruncated()
        {
            // Astronomical night at Penns Park in November is ~13.x hours -- not
            // an integer number of 1-hour slots. The last slot must be truncated
            // to fit inside [dusk, dawn]; all earlier slots must be exactly slotSize.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var slot = TimeSpan.FromHours(1);

            var samples = QualitySamples.OverNight(
                Target.Default, loc, night, slot, SinAltQuality);

            Assert.True(samples.Count >= 2);
            for (int i = 0; i < samples.Count - 1; i++)
            {
                Assert.Equal(slot, samples[i].End - samples[i].Start);
            }
            TimeSpan lastDuration = samples[samples.Count - 1].End - samples[samples.Count - 1].Start;
            Assert.True(lastDuration <= slot);
            Assert.True(lastDuration > TimeSpan.Zero);
        }

        [Fact]
        public void OverNight_PerHourQuality_MatchesIntegratedQualityDividedByHours()
        {
            // Equivalence check: each slot's QualityPerHour must equal
            // IntegratedQuality.OverSession(slot) / slot.TotalHours.
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);

            var samples = QualitySamples.OverNight(
                Target.Default, loc, night, TimeSpan.FromMinutes(30), SinAltQuality);

            foreach (var (start, end, perHour) in samples)
            {
                TimeSpan dur = end - start;
                double integrated = IntegratedQuality.OverSession(
                    Target.Default, loc, start, dur, SinAltQuality);
                Assert.Equal(integrated / dur.TotalHours, perHour, precision: 12);
            }
        }

        [Fact]
        public void OverNight_PolarDay_ReturnsEmptyList()
        {
            var loc = Location.Default.With(
                latitude: 80.0, north: true,
                dateTime: new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
            var night = NightCalculator.ComputeNight(loc);

            var samples = QualitySamples.OverNight(
                Target.Default, loc, night, TimeSpan.FromHours(1), SinAltQuality);

            Assert.False(night.IsValid);
            Assert.Empty(samples);
        }

        [Fact]
        public void OverNight_NonPositiveSlotSize_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);

            Assert.Throws<ArgumentException>(() => QualitySamples.OverNight(
                Target.Default, loc, night, TimeSpan.Zero, SinAltQuality));
            Assert.Throws<ArgumentException>(() => QualitySamples.OverNight(
                Target.Default, loc, night, TimeSpan.FromMinutes(-1), SinAltQuality));
        }

        [Fact]
        public void OverNight_NullArgs_Throws()
        {
            var loc = MakeLocation();
            var night = NightCalculator.ComputeNight(loc);
            var slot = TimeSpan.FromHours(1);

            Assert.Throws<ArgumentNullException>(() => QualitySamples.OverNight(
                null, loc, night, slot, SinAltQuality));
            Assert.Throws<ArgumentNullException>(() => QualitySamples.OverNight(
                Target.Default, null, night, slot, SinAltQuality));
            Assert.Throws<ArgumentNullException>(() => QualitySamples.OverNight(
                Target.Default, loc, night, slot, null));
        }
    }
}
