using System;
using System.Collections.Generic;
using System.Threading;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Pre-computed <see cref="NightWindow"/> set for a single <see cref="Location"/> spanning
    /// the day the location's DateTime falls in plus a contiguous forward range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="NightCalculator.ComputeNight"/> depends only on latitude, longitude, date, and
    /// UTC offset -- it does not depend on the target being observed. When multiple targets are
    /// graphed against the same <see cref="Location"/> (the multi-target Graph path), each
    /// target's year build otherwise re-derives the same 365-day NightWindow series
    /// independently. This cache amortizes the work across targets.
    /// </para>
    /// <para>
    /// This type amortizes that cost: build it once per Graph click, hand it to every target's
    /// <c>AltitudeSeries</c>, and per-target year work becomes pure math (AltAz) that parallelizes
    /// freely across threadpool cores.
    /// </para>
    /// <para>
    /// <b>Thread-safety:</b> the instance is immutable after construction; concurrent readers are
    /// safe. The <em>construction</em> itself runs sequentially across the year-day loop, but the
    /// underlying <see cref="NightCalculator"/> is now stateless / lock-free (Meeus-backed), so
    /// callers can build several caches in parallel for different locations if useful.
    /// </para>
    /// </remarks>
    public sealed class NightCache
    {
        /// <summary>
        /// NightWindow for the location's own <c>DateTime</c> -- the "tonight" window used
        /// by the Day and Moon series.
        /// </summary>
        public NightWindow Starting { get; }

        /// <summary>Zero-day of the year series. Entry <c>i</c> of <see cref="YearDays"/> is
        /// <c>YearStartDay.AddDays(i)</c>.</summary>
        public DateTime YearStartDay { get; }

        /// <summary>Per-day NightWindow array; indices align with
        /// <c>YearStartDay.AddDays(i)</c>.</summary>
        public IReadOnlyList<NightWindow> YearDays { get; }

        /// <summary>
        /// Builds the full cache: one NightCalculator call for <paramref name="location"/>'s
        /// current DateTime and one per day across <c>[yearStartDay, yearStartDay + yearDaysCount)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="location"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">yearDaysCount is negative.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/> fired mid-build.</exception>
        public NightCache(Location location, DateTime yearStartDay, int yearDaysCount,
                          CancellationToken ct = default)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (yearDaysCount < 0) throw new ArgumentOutOfRangeException(nameof(yearDaysCount));

            Starting = NightCalculator.ComputeNight(location);
            YearStartDay = yearStartDay;

            NightWindow[] year = new NightWindow[yearDaysCount];
            for (int i = 0; i < yearDaysCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                Location dayLoc = location.With(dateTime: yearStartDay.AddDays(i));
                year[i] = NightCalculator.ComputeNight(dayLoc);
            }
            YearDays = year;
        }

        /// <summary>
        /// Returns the day-of-year index aligned with <see cref="YearDays"/> for the same seed
        /// formula <c>AltitudeSeries.ComputeYearCache</c> uses internally, so AltitudeChart can
        /// build a cache whose range matches what each AltitudeSeries will look up.
        /// </summary>
        public static DateTime ComputeYearStartDay(DateTime seed)
        {
            return seed.AddDays(-seed.Day);
        }

        /// <summary>Companion of <see cref="ComputeYearStartDay"/>: total days in the year window.</summary>
        public static int ComputeYearDaysCount(DateTime seed)
        {
            DateTime start = ComputeYearStartDay(seed);
            DateTime end = start.AddYears(1);
            return (int)end.Subtract(start).TotalDays;
        }
    }
}
