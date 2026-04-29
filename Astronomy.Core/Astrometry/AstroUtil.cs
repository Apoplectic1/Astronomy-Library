using System;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Time;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Public, NINA-API-compatible astronomy surface. Pure C#, thread-safe by construction
    /// (no static mutable state, no init dance), so callers can hammer this from many
    /// threads without coordination. Replaces the CoordinateSharp-backed paths that
    /// gated through a process-wide lock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Function names and signatures mirror <c>NINA.Astrometry.AstroUtil</c> so a future
    /// scheduler-plugin port can be drop-in interchangeable. Internals are Meeus-based
    /// (chapters 15, 25, 47, 48); accuracy is ~30s on twilight events at moderate
    /// latitudes and ~10 arcsec on moon position -- ample for scheduler use, well below
    /// the tolerance any UI-level decision can resolve.
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
        // -------------------- Sun rise/set / twilights --------------------

        /// <summary>
        /// Sun rise / set on the UTC calendar day of <paramref name="dateUtc"/>.
        /// Threshold is the geometric altitude -0.833&#176; (refraction + solar disc
        /// semi-diameter, the standard "official" sunrise/sunset definition).
        /// </summary>
        public static RiseAndSetEvent GetSunRiseAndSet(DateTime dateUtc, double latDeg, double lonEastDeg)
            => RiseSetAt(dateUtc, latDeg, lonEastDeg, -0.833);

        /// <summary>Civil twilight (sun centre at -6&#176;).</summary>
        public static RiseAndSetEvent GetCivilNightTimes(DateTime dateUtc, double latDeg, double lonEastDeg)
            => RiseSetAt(dateUtc, latDeg, lonEastDeg, -6.0);

        /// <summary>Nautical twilight (sun centre at -12&#176;).</summary>
        public static RiseAndSetEvent GetNauticalNightTimes(DateTime dateUtc, double latDeg, double lonEastDeg)
            => RiseSetAt(dateUtc, latDeg, lonEastDeg, -12.0);

        /// <summary>Astronomical twilight (sun centre at -18&#176;).</summary>
        public static RiseAndSetEvent GetNightTimes(DateTime dateUtc, double latDeg, double lonEastDeg)
            => RiseSetAt(dateUtc, latDeg, lonEastDeg, -18.0);

        private static RiseAndSetEvent RiseSetAt(DateTime dateUtc, double latDeg, double lonEastDeg, double altDeg)
        {
            (DateTime? rise, DateTime? set) = SunPosition.RiseSet(dateUtc, latDeg, lonEastDeg, altDeg);
            return new RiseAndSetEvent(rise, set);
        }

        // -------------------- Sun position --------------------

        /// <summary>
        /// Geometric altitude of the Sun (degrees) at <paramref name="utc"/> as seen from
        /// <paramref name="observer"/>. Solar parallax (~9&quot;) is below tolerance and
        /// not modelled; refraction is intentionally not applied (caller composes if
        /// they want apparent altitude).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public static double GetSunAltitude(DateTime utc, ObserverInfo observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            (double altDeg, _) = SunAltAz(utc, observer);
            return altDeg;
        }

        /// <summary>
        /// Azimuth of the Sun (degrees from North clockwise) at <paramref name="utc"/>
        /// as seen from <paramref name="observer"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public static double GetSunAzimuth(DateTime utc, ObserverInfo observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            (_, double azDeg) = SunAltAz(utc, observer);
            return azDeg;
        }

        // -------------------- Moon position --------------------

        /// <summary>
        /// Topocentric altitude of the Moon (degrees) at <paramref name="utc"/> as seen
        /// from <paramref name="observer"/>. Includes parallax correction (Meeus 40)
        /// since the Moon's parallax is ~1&#176; -- non-negligible.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public static double GetMoonAltitude(DateTime utc, ObserverInfo observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            (double altDeg, _) = MoonAltAz(utc, observer);
            return altDeg;
        }

        /// <summary>
        /// Topocentric azimuth of the Moon (degrees, measured from North clockwise) at
        /// <paramref name="utc"/> as seen from <paramref name="observer"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is null.</exception>
        public static double GetMoonAzimuth(DateTime utc, ObserverInfo observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            (_, double azDeg) = MoonAltAz(utc, observer);
            return azDeg;
        }

        /// <summary>
        /// Geocentric illuminated fraction of the Moon's disc, range <c>[0, 1]</c>.
        /// Topocentric correction is &lt; 0.0001 and intentionally not modelled.
        /// </summary>
        public static double GetMoonIllumination(DateTime utc)
        {
            double jd = JulianDate.FromUtc(EnsureUtc(utc));
            return MoonIllumination.Fraction(jd);
        }

        /// <summary>
        /// Moon rise / set on the UTC calendar day of <paramref name="dateUtc"/> for an
        /// observer at <paramref name="latDeg"/> / <paramref name="lonEastDeg"/>. Uses the
        /// standard <c>h0 = 0.125&#176;</c> threshold (upper limb at the refraction-
        /// adjusted horizon for a moon at typical distance). Either or both may be null
        /// if the moon is circumpolar above / below the threshold for the whole day.
        /// </summary>
        public static RiseAndSetEvent GetMoonRiseAndSet(DateTime dateUtc, double latDeg, double lonEastDeg)
        {
            (DateTime? rise, DateTime? set) = MoonPosition.RiseSet(dateUtc, latDeg, lonEastDeg, 0.125);
            return new RiseAndSetEvent(rise, set);
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
            double age = LunarAgeDays(EnsureUtc(utc));
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
            DateTime utcOnly = EnsureUtc(utc);
            double jd = JulianDate.FromUtc(utcOnly);

            // Local apparent sidereal time, in degrees. SiderealTime.Local returns hours;
            // convert to degrees with *15.
            double lstDeg = SiderealTime.Local(utcOnly, observer.Longitude) * 15.0;

            // Topocentric (RA, Dec) of the Moon -- parallax-corrected.
            (double raDeg, double decDeg, _) = MoonPosition.Topocentric(
                jd, lstDeg, observer.Latitude, observer.Elevation);

            return AltAzFromRaDec(lstDeg, raDeg, decDeg, observer.Latitude);
        }

        // Computes geocentric (alt, az) of the Sun at utc for the observer. Solar
        // parallax is negligible; we skip topocentric correction.
        private static (double AltDeg, double AzDeg) SunAltAz(DateTime utc, ObserverInfo observer)
        {
            DateTime utcOnly = EnsureUtc(utc);
            double jd = JulianDate.FromUtc(utcOnly);

            double lstDeg = SiderealTime.Local(utcOnly, observer.Longitude) * 15.0;

            (double raDeg, double decDeg, _) = SunPosition.Apparent(jd);

            return AltAzFromRaDec(lstDeg, raDeg, decDeg, observer.Latitude);
        }

        // Shared (alt, az) reduction from (LST, RA, Dec, lat). Az is from North,
        // clockwise (N=0, E=90, S=180, W=270). Meeus 13.5 / 13.6 compute az-from-south;
        // we convert to az-from-north so the public API matches NINA.
        private static (double AltDeg, double AzDeg) AltAzFromRaDec(
            double lstDeg, double raDeg, double decDeg, double latDeg)
        {
            double Hdeg = MeeusUtility.NormPm180(lstDeg - raDeg);
            double Hrad = Hdeg    * MeeusUtility.DegToRad;
            double phiRad = latDeg * MeeusUtility.DegToRad;
            double decRad = decDeg * MeeusUtility.DegToRad;

            double sinPhi = Math.Sin(phiRad);
            double cosPhi = Math.Cos(phiRad);
            double sinDec = Math.Sin(decRad);
            double cosDec = Math.Cos(decRad);
            double sinH   = Math.Sin(Hrad);
            double cosH   = Math.Cos(Hrad);

            double altRad = Math.Asin(sinPhi * sinDec + cosPhi * cosDec * cosH);
            double azFromSouth = Math.Atan2(sinH, cosH * sinPhi - (sinDec / cosDec) * cosPhi);
            double azDeg  = MeeusUtility.Norm360(azFromSouth * MeeusUtility.RadToDeg + 180.0);
            double altDeg = altRad * MeeusUtility.RadToDeg;
            return (altDeg, azDeg);
        }

        private static DateTime EnsureUtc(DateTime dt)
            => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
}
