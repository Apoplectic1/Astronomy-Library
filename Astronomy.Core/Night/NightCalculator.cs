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

                // Caveat: SunEphemeris.RiseSet returns ONE set event per UTC calendar
                // day (Meeus's single-iteration convergence at m2 = Frac(m0 + H0/360)).
                // On dates where local evening astronomical dusk straddles 00:00 UTC,
                // TWO -18-deg down-crossings land on the same UTC day -- the prior
                // evening's dusk just past midnight UTC, and tonight's dusk just before
                // midnight UTC the next day. Meeus converges to whichever is closer to
                // its initial m_init and silently misses the other. The dropped event
                // is the dusk we actually need for the current night; the one we found
                // is the prior night's dusk, which combines with endingDawn into a
                // >18 h "night" that is really two separate nights with a sunlit day
                // between them.
                //
                // Detection: a real astronomical night at non-polar latitudes is at
                // most ~14 h, and at extreme polar twilight conditions the dusk/dawn
                // search already returns null (NightWindow.IsValid then reports false
                // at the caller). A computed (dawn - dusk) > 18 h is the signature of
                // the missing-late-dusk bug, not a real night.
                //
                // Recovery: brute-force-bisect backwards from endingDawn for the
                // latest -18-deg down-crossing. Triggers on ~6-10 nights per year per
                // location at mid-latitudes (autumn / spring equinox bands, shifted by
                // each DST transition); recovery cost is ~25 sun-altitude evaluations
                // plus a ~35-step bisect (a few microseconds), trivial vs the 365
                // night-builds in a year-cache.
                //
                // The "structural" alternative is to extend SunEphemeris.RiseSet to
                // return both events on collision days, but that changes the API for
                // every consumer; this localized fix has zero blast radius outside
                // this method.
                if (startingDusk.HasValue
                    && (endingDawn.Value - startingDusk.Value) > TimeSpan.FromHours(18))
                {
                    DateTime? recovered = FindLatestDuskBefore(
                        endingDawn.Value, latSigned, lonEast, sunAltDeg);
                    if (recovered.HasValue
                        && recovered.Value > startingDusk.Value
                        && (endingDawn.Value - recovered.Value) <= TimeSpan.FromHours(18))
                    {
                        startingDusk = recovered;
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

        // Walk backward from `before` in 30-minute steps to bracket the most recent
        // sun crossing from above sunAltBelowDeg to at-or-below it (= astronomical
        // dusk for -18 deg), then bisect to ~1-second precision. Used by
        // BracketingPair to recover the late same-UTC-day dusk that
        // SunEphemeris.RiseSet silently drops on collision days. Returns null if no
        // crossing is found within ~15 h (polar / anomalous condition -- caller
        // falls back to the original startingDusk).
        private static DateTime? FindLatestDuskBefore(
            DateTime before, double latSigned, double lonEast, double sunAltBelowDeg)
        {
            const int MaxStepsBack = 30;
            TimeSpan step = TimeSpan.FromMinutes(30);

            DateTime tHi = before;
            double altHi = SunAltitudeDeg(tHi, latSigned, lonEast);
            for (int i = 1; i <= MaxStepsBack; i++)
            {
                DateTime tLo = before.AddTicks(-i * step.Ticks);
                double altLo = SunAltitudeDeg(tLo, latSigned, lonEast);
                // Forward-in-time across [tLo, tHi]: altitude goes from above the
                // threshold (at tLo) to at-or-below (at tHi) -- this is the dusk
                // crossing we want.
                if (altLo > sunAltBelowDeg && altHi <= sunAltBelowDeg)
                    return BisectDuskCrossing(tLo, tHi, sunAltBelowDeg, latSigned, lonEast);
                tHi = tLo;
                altHi = altLo;
            }
            return null;
        }

        // Bisect the dusk crossing within [lo, hi] where altLo > threshold and
        // altHi <= threshold. Returns the first UTC instant (to ~1 s) where the
        // sun's altitude is at or below the threshold.
        private static DateTime BisectDuskCrossing(
            DateTime lo, DateTime hi, double threshold, double latSigned, double lonEast)
        {
            while ((hi - lo).Ticks > TimeSpan.TicksPerSecond)
            {
                DateTime mid = lo.AddTicks((hi - lo).Ticks / 2);
                double altMid = SunAltitudeDeg(mid, latSigned, lonEast);
                if (altMid > threshold) lo = mid;
                else hi = mid;
            }
            return hi;
        }

        // Sun altitude in degrees at a UTC instant using the standard equatorial-to-
        // horizontal conversion: sun RA/Dec from SunEphemeris.Apparent, LST at the
        // observer from SiderealTime.Local, then
        //   alt = asin(sin(lat) * sin(dec) + cos(lat) * cos(dec) * cos(HA)).
        // Internal to NightCalculator -- the dusk-recovery path is the only consumer.
        private static double SunAltitudeDeg(DateTime utc, double latSigned, double lonEast)
        {
            double jd = JulianDate.FromUtc(utc);
            (double raDeg, double decDeg, double _r) = SunEphemeris.Apparent(jd);
            double lstDeg = SiderealTime.Local(utc, lonEast) * 15.0;
            double haDeg = lstDeg - raDeg;
            if (haDeg > 180.0) haDeg -= 360.0;
            else if (haDeg < -180.0) haDeg += 360.0;

            const double degToRad = Math.PI / 180.0;
            double sinAlt = Math.Sin(latSigned * degToRad) * Math.Sin(decDeg * degToRad)
                          + Math.Cos(latSigned * degToRad) * Math.Cos(decDeg * degToRad)
                          * Math.Cos(haDeg * degToRad);
            return Math.Asin(sinAlt) * (180.0 / Math.PI);
        }
    }
}
