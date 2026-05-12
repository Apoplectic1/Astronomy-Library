using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Time;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Computes the astronomical night window (sun at or below -18&#176;) bracketing
    /// <see cref="Location.DateTime"/>, plus the lunar illumination fraction. Pure;
    /// no static mutable state; safe to call from concurrent background tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backed by <see cref="SunEphemeris.RiseSet"/> (Meeus chapter 15) and
    /// <see cref="MoonIllumination.Fraction"/> (Meeus chapter 48). Call
    /// <see cref="TwilightCalculator.ComputeNight"/> for nautical (-12&#176;) or civil
    /// (-6&#176;) thresholds; the implementation is shared.
    /// </para>
    /// <para>
    /// The returned <see cref="NightWindow.AstronomicalDawn"/> /
    /// <see cref="NightWindow.AstronomicalDusk"/> are <see cref="DateTimeKind.Utc"/>.
    /// </para>
    /// </remarks>
    public static class NightCalculator
    {
        /// <summary>
        /// Returns the astronomical-twilight night window bracketing
        /// <paramref name="location"/>'s moment.
        /// </summary>
        /// <param name="location">Observer position and local moment. Non-null.</param>
        /// <returns>
        /// A <see cref="NightWindow"/> with <see cref="DateTimeKind.Utc"/> dusk/dawn
        /// instants. If the location is in polar day / polar night (no -18&#176;
        /// crossing within the bracketing 3-day window), the missing field is
        /// <see cref="DateTime.MinValue"/> and <see cref="NightWindow.IsValid"/>
        /// reports false.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static NightWindow ComputeNight(Location location)
        {
            ArgumentNullException.ThrowIfNull(location);
            return Compute(location, -18.0);
        }

        // Shared helper: bracket the night around location.DateTime where the sun
        // crosses sunAltBelowDeg. Used by TwilightCalculator for the parameterised
        // threshold variants. Operating in pure UTC (no local-frame offset trick)
        // avoids the DST-transition trap the old CoordinateSharp path had to dodge.
        internal static NightWindow Compute(Location location, double sunAltBelowDeg)
        {
            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double lonEast   = location.West  ? -location.Longitude :  location.Longitude;

            DateTime locUtc = AsUtc(location.DateTime);

            // The night that wraps locUtc could have its dusk on the prior UTC day and
            // its dawn on the next UTC day; sample three consecutive days to cover all
            // alignments without any offset gymnastics.
            (DateTime? endingDawn, DateTime? startingDusk) = BracketingPair(
                locUtc, latSigned, lonEast, sunAltBelowDeg);

            double illum = AstroUtil.GetMoonIllumination(locUtc);

            return new NightWindow
            {
                AstronomicalDawn          = endingDawn   ?? DateTime.MinValue,
                AstronomicalDusk          = startingDusk ?? DateTime.MinValue,
                LunarIlluminationFraction = illum,
            };
        }

        private static (DateTime? Dawn, DateTime? Dusk) BracketingPair(
            DateTime locUtc, double latSigned, double lonEast, double sunAltDeg)
        {
            // Build the candidate event lists across day-1 / day / day+1.
            DateTime[] days = { locUtc.AddDays(-1), locUtc, locUtc.AddDays(1) };

            DateTime? endingDawn = null;
            for (int i = 0; i < days.Length; i++)
            {
                (DateTime? rise, _) = SunEphemeris.RiseSet(days[i], latSigned, lonEast, sunAltDeg);
                if (rise.HasValue && rise.Value >= locUtc
                    && (!endingDawn.HasValue || rise.Value < endingDawn.Value))
                {
                    endingDawn = rise;
                }
            }

            DateTime? startingDusk = null;
            if (endingDawn.HasValue)
            {
                for (int i = 0; i < days.Length; i++)
                {
                    (_, DateTime? set) = SunEphemeris.RiseSet(days[i], latSigned, lonEast, sunAltDeg);
                    if (set.HasValue && set.Value < endingDawn.Value
                        && (!startingDusk.HasValue || set.Value > startingDusk.Value))
                    {
                        startingDusk = set;
                    }
                }
            }

            return (endingDawn, startingDusk);
        }

        // Resolve location.DateTime to a true UTC instant per the Location contract:
        // Kind=Utc passes through; Kind=Local converts; Kind=Unspecified is treated
        // as Local (per Location.DateTime XML doc).
        private static DateTime AsUtc(DateTime dt)
        {
            switch (dt.Kind)
            {
                case DateTimeKind.Utc:   return dt;
                case DateTimeKind.Local: return dt.ToUniversalTime();
                default:                 return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
            }
        }
    }
}
