using System;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Time;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Public Moon surface, partially NINA-mirroring. Pure C#, thread-safe by construction
    /// (no static mutable state, no init dance), so callers can hammer this from many
    /// threads without coordination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NINA mirror status (audited against NINA.Astrometry/AstroUtil.cs on 2026-05-18):</b>
    /// <see cref="GetMoonAltitude"/>, <see cref="GetMoonIllumination"/>, and
    /// <see cref="GetMoonRiseAndSet"/> mirror NINA's current surface so a NINA-style
    /// consumer can drop the Library types in without changing call sites. NINA's
    /// preferred overloads take an <see cref="ObserverInfo"/> for topocentric corrections;
    /// we expose only the variants TP actually uses (geocentric
    /// <see cref="GetMoonIllumination"/>; <see cref="ObserverInfo"/>-taking
    /// <see cref="GetMoonAltitude"/>). The three convenience methods without direct NINA
    /// equivalents -- <see cref="GetMoonAzimuth"/>, <see cref="GetMoonAltAz"/>, and
    /// <see cref="GetMoonPhaseName"/> -- round out the public surface for downstream use;
    /// <see cref="GetMoonPhaseName"/> returns a string rather than NINA's
    /// <c>MoonPhase</c> enum because we don't reproduce that type, and the bucketing is
    /// synodic-age-based rather than NINA's Sun-Moon-angle-based -- the names line up but
    /// boundary instants can differ by hours near a quarter-phase transition.
    /// </para>
    /// <para>
    /// Internals are Meeus-based (chapters 47, 48); accuracy is ~10 arcsec on moon
    /// position -- ample for scheduler use, well below any UI-level decision threshold.
    /// The Sun-related members that previously lived here have moved to
    /// <see cref="Astronomy.Core.Sun.SunPosition"/> /
    /// <see cref="Astronomy.Core.Sun.SunEvents"/>.
    /// </para>
    /// <para>
    /// All <see cref="DateTime"/> inputs are expected as <see cref="DateTimeKind.Utc"/>;
    /// outputs (<see cref="RiseAndSetEvent.Rise"/>, <see cref="RiseAndSetEvent.Set"/>) are
    /// likewise UTC. Longitude convention is east-positive (so a western-hemisphere
    /// observer is negative).
    /// </para>
    /// </remarks>
    public static class AstroUtil
    {
        // -------------------- Moon position --------------------

        /// <summary>
        /// Topocentric altitude of the Moon (degrees) at <paramref name="utc"/> as seen
        /// from <paramref name="observer"/>. Includes parallax correction (Meeus 40)
        /// since the Moon's parallax is ~1&#176; -- non-negligible.
        /// </summary>
        public static double GetMoonAltitude(DateTime utc, ObserverInfo observer)
        {
            (double altDeg, _) = MoonAltAz(utc, observer);
            return altDeg;
        }

        /// <summary>
        /// Topocentric azimuth of the Moon (degrees, measured from North clockwise) at
        /// <paramref name="utc"/> as seen from <paramref name="observer"/>.
        /// </summary>
        public static double GetMoonAzimuth(DateTime utc, ObserverInfo observer)
        {
            (_, double azDeg) = MoonAltAz(utc, observer);
            return azDeg;
        }

        /// <summary>
        /// Topocentric altitude and azimuth of the Moon (degrees) at <paramref name="utc"/>
        /// in a single Meeus pass.
        /// </summary>
        /// <remarks>
        /// Prefer this over calling <see cref="GetMoonAltitude"/> +
        /// <see cref="GetMoonAzimuth"/> separately when the caller needs both. Each of the
        /// single-component getters runs the full <c>MoonPosition.Topocentric</c> periodic-
        /// term sum (~1.5 µs per call); calling them in sequence pays the cost twice for
        /// the same instant. <see cref="Astronomy.Core.Moon.MoonSeparation.ObserveAt"/>
        /// is the canonical caller.
        /// </remarks>
        public static (double AltDeg, double AzDeg) GetMoonAltAz(DateTime utc, ObserverInfo observer)
        {
            return MoonAltAz(utc, observer);
        }

        /// <summary>
        /// Geocentric illuminated fraction of the Moon's disc, range <c>[0, 1]</c>.
        /// Topocentric correction is &lt; 0.0001 and intentionally not modelled.
        /// </summary>
        public static double GetMoonIllumination(DateTime utc)
        {
            double jd = JulianDate.FromUtc(TimeKindGuard.AsUtc(utc));
            return MoonIllumination.Fraction(jd);
        }

        /// <summary>
        /// Moon rise / set on the UTC calendar day of <paramref name="dateUtc"/> for an
        /// observer at <paramref name="latDeg"/> / <paramref name="lonEastDeg"/> at
        /// <paramref name="elevationM"/> meters above sea level. Uses the standard
        /// <c>h0 = 0.125&#176;</c> threshold (upper limb at the refraction-adjusted
        /// sea-level horizon) lowered by the refracted horizon dip so an elevated
        /// observer's earlier rise / later set matches reality. Either or both may be
        /// null if the moon is circumpolar above / below the threshold for the whole day.
        /// </summary>
        public static RiseAndSetEvent GetMoonRiseAndSet(
            DateTime dateUtc, double latDeg, double lonEastDeg, double elevationM = 0.0)
        {
            double h0 = 0.125 - MeeusUtility.HorizonDipDeg(elevationM);
            (DateTime? rise, DateTime? set) = MoonPosition.RiseSet(dateUtc, latDeg, lonEastDeg, h0);
            return new RiseAndSetEvent(rise, set);
        }

        /// <summary>
        /// Moon rise / set events bracketing an astronomical night
        /// <c>[<paramref name="duskUtc"/>, <paramref name="dawnUtc"/>]</c>. Unlike
        /// <see cref="GetMoonRiseAndSet"/> which searches the single UTC calendar day of
        /// its input -- and which therefore mis-pairs events for non-UTC observers (a
        /// rise / set in the user's local "tonight" can straddle two UTC days) -- this
        /// scans three UTC days and selects:
        /// <list type="bullet">
        ///   <item><c>Rise</c> = latest moonrise &lt;= <paramref name="dawnUtc"/></item>
        ///   <item><c>Set</c> = earliest moonset &gt;= <paramref name="duskUtc"/></item>
        /// </list>
        /// This yields the rise that put the moon in the sky for this night (or, if the
        /// moon rises mid-night, that rise itself) and the set that takes it back down
        /// during or after the night. Convention mirrors
        /// <c>NightCalculator.BracketingPair</c>'s sun-event selection.
        /// </summary>
        /// <remarks>
        /// Either or both may be null when the moon stays above / below the threshold
        /// for the entire 3-day search window (polar latitudes), or when the inputs are
        /// <see cref="DateTime.MinValue"/> sentinels (no astronomical night at this
        /// location/date -- the call short-circuits and returns null events).
        /// </remarks>
        public static RiseAndSetEvent GetMoonRiseAndSetForNight(
            DateTime duskUtc, DateTime dawnUtc,
            double latDeg, double lonEastDeg, double elevationM = 0.0)
        {
            if (duskUtc == DateTime.MinValue || dawnUtc == DateTime.MinValue)
                return new RiseAndSetEvent(null, null);

            DateTime duskUtcGuarded = TimeKindGuard.AsUtc(duskUtc);
            DateTime dawnUtcGuarded = TimeKindGuard.AsUtc(dawnUtc);
            double h0 = 0.125 - MeeusUtility.HorizonDipDeg(elevationM);

            DateTime[] days =
            {
                duskUtcGuarded.AddDays(-1),
                duskUtcGuarded,
                duskUtcGuarded.AddDays(1),
            };

            DateTime? bestRise = null;
            DateTime? bestSet = null;

            for (int i = 0; i < days.Length; i++)
            {
                (DateTime? rise, DateTime? set) = MoonPosition.RiseSet(
                    days[i], latDeg, lonEastDeg, h0);

                if (rise.HasValue && rise.Value <= dawnUtcGuarded
                    && (!bestRise.HasValue || rise.Value > bestRise.Value))
                {
                    bestRise = rise;
                }

                if (set.HasValue && set.Value >= duskUtcGuarded
                    && (!bestSet.HasValue || set.Value < bestSet.Value))
                {
                    bestSet = set;
                }
            }

            return new RiseAndSetEvent(bestRise, bestSet);
        }

        /// <summary>
        /// Common lunar phase name at <paramref name="utc"/>. One of "New Moon",
        /// "Waxing Crescent", "First Quarter", "Waxing Gibbous", "Full Moon",
        /// "Waning Gibbous", "Last Quarter", "Waning Crescent". The boundaries split the
        /// 29.53-day synodic period into 8 equal-width buckets centred on the four
        /// cardinal phases; no allowance is made for the &#177;6 h synodic-period drift
        /// (it would shift bucket edges by &lt; 1.5%).
        /// </summary>
        public static string GetMoonPhaseName(DateTime utc)
        {
            double age = LunarAgeDays(TimeKindGuard.AsUtc(utc));
            double cycle = Astronomy.Core.Moon.LunarAge.SynodicMonthDays;
            double bucket = cycle / 8.0;       // ~3.691 days
            double half = bucket / 2.0;        // ~1.846 days

            if (age < half)                return "New Moon";
            if (age < half + bucket)       return "Waxing Crescent";
            if (age < half + 2 * bucket)   return "First Quarter";
            if (age < half + 3 * bucket)   return "Waxing Gibbous";
            if (age < half + 4 * bucket)   return "Full Moon";
            if (age < half + 5 * bucket)   return "Waning Gibbous";
            if (age < half + 6 * bucket)   return "Last Quarter";
            if (age < half + 7 * bucket)   return "Waning Crescent";
            return "New Moon";  // wrap-around at end of synodic month
        }

        private static double LunarAgeDays(DateTime utc)
            => Astronomy.Core.Moon.LunarAge.DaysAt(utc);

        // -------------------- Internals --------------------

        // Computes topocentric (alt, az) of the Moon at utc for the observer. Az is
        // measured from North clockwise (N=0, E=90, S=180, W=270) -- NINA convention.
        private static (double AltDeg, double AzDeg) MoonAltAz(DateTime utc, ObserverInfo observer)
        {
            DateTime utcOnly = TimeKindGuard.AsUtc(utc);
            double jd = JulianDate.FromUtc(utcOnly);

            // Local apparent sidereal time, in degrees. SiderealTime.Local returns hours;
            // convert to degrees with *15.
            double lstDeg = SiderealTime.Local(utcOnly, observer.Longitude) * 15.0;

            // Topocentric (RA, Dec) of the Moon -- parallax-corrected.
            (double raDeg, double decDeg, _) = MoonPosition.Topocentric(
                jd, lstDeg, observer.Latitude, observer.Elevation);

            return AltAzFromRaDec(lstDeg, raDeg, decDeg, observer.Latitude);
        }

        // Shared (alt, az) reduction from (LST, RA, Dec, lat). Delegates to the
        // public TargetGeometry helpers so the Meeus path consumes the same
        // signed-degree geometry primitives the Session layer uses -- no parallel
        // alt/az implementation. HA arrives in degrees from (LST - RA) * conversion;
        // TargetGeometry takes HA in sidereal hours, so divide by 15. Both
        // primitives wrap HA internally and return [0, 360) azimuth from North,
        // matching the NINA public-API convention.
        private static (double AltDeg, double AzDeg) AltAzFromRaDec(
            double lstDeg, double raDeg, double decDeg, double latDeg)
        {
            double haHours = MeeusUtility.NormPm180(lstDeg - raDeg) / 15.0;
            return (TargetGeometry.AltitudeAtHourAngle(haHours, latDeg, decDeg),
                    TargetGeometry.AzimuthAtHourAngle(haHours, latDeg, decDeg));
        }

    }
}
