using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Time;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Sun-events on a UTC date: rise/set, twilight crossings, transit (solar noon),
    /// noon altitude, day length, "next rise / next set from now", and equation of time.
    /// </summary>
    /// <remarks>
    /// Date-keyed methods take a <see cref="DateOnly"/>; instant-keyed methods take a
    /// <see cref="DateTime"/> in <see cref="DateTimeKind.Utc"/>. Outputs (rise/set/transit
    /// instants) are <see cref="DateTimeKind.Utc"/>. Polar day / polar night returns
    /// <see langword="null"/> for the missing event in <see cref="RiseAndSetEvent"/>.
    /// </remarks>
    public static class SunEvents
    {
        /// <summary>
        /// Sun rise / set on a UTC calendar date. Threshold is the geometric altitude
        /// -0.833&#176; (refraction + solar disc semi-diameter) lowered by the refracted
        /// horizon dip <c>1.76 * sqrt(elevationM)</c> arcmin so an elevated observer's
        /// earlier sunrise / later sunset matches reality.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utcDate">UTC calendar date.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static RiseAndSetEvent RiseSetOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            return CrossingsOnInternal(location, utcDate,
                -0.833 - MeeusUtility.HorizonDipDeg(location.Elevation));
        }

        /// <summary>
        /// Civil twilight start / end on a UTC date (sun centre at -6&#176;). NOT
        /// elevation-corrected -- twilight thresholds reference the celestial horizontal
        /// plane by convention.
        /// </summary>
        public static RiseAndSetEvent CivilTwilightOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            return CrossingsOnInternal(location, utcDate, -6.0);
        }

        /// <summary>Nautical twilight (sun centre at -12&#176;). NOT elevation-corrected.</summary>
        public static RiseAndSetEvent NauticalTwilightOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            return CrossingsOnInternal(location, utcDate, -12.0);
        }

        /// <summary>Astronomical twilight (sun centre at -18&#176;). NOT elevation-corrected.</summary>
        public static RiseAndSetEvent AstronomicalTwilightOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            return CrossingsOnInternal(location, utcDate, -18.0);
        }

        /// <summary>
        /// Sun rise / set on a UTC date for an arbitrary geometric altitude threshold.
        /// Useful for non-standard conditions (e.g. sun_alt = -4&#176; for "blue hour"
        /// boundaries) without calling out to the four named twilights individually.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utcDate">UTC calendar date.</param>
        /// <param name="altitudeDeg">Geometric altitude threshold in degrees. NOT
        /// elevation-corrected -- caller is responsible for adding any horizon-dip
        /// adjustment if applicable.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static RiseAndSetEvent CrossingsOn(
            Location location, DateOnly utcDate, double altitudeDeg)
        {
            ArgumentNullException.ThrowIfNull(location);
            return CrossingsOnInternal(location, utcDate, altitudeDeg);
        }

        /// <summary>
        /// UTC instant of solar transit (upper culmination) on a UTC calendar date. This
        /// is "apparent solar noon" -- the moment the Sun crosses the local meridian. The
        /// returned instant is within ~30 seconds of the actual transit at moderate
        /// latitudes.
        /// </summary>
        /// <remarks>
        /// Implementation: iterate <c>HourAngleAt -> 0</c> via fixed-point starting from
        /// UTC noon shifted by the longitude-driven local-noon offset. Converges in 2-3
        /// iterations because the sun's HA changes at a near-constant 15&#176;/hour.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime TransitOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);

            // Initial guess: UTC noon of utcDate, shifted by location longitude so we
            // start near local solar noon.
            DateTime t = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, 12, 0, 0, DateTimeKind.Utc);
            double lonEast = location.West ? -location.Longitude : location.Longitude;
            t = t.AddHours(-lonEast / 15.0);

            // Fixed-point: at transit HA=0, so each iteration corrects by -HA hours.
            for (int i = 0; i < 5; i++)
            {
                double ha = SunPosition.HourAngleAt(location, t);
                t = t.AddHours(-ha);
            }
            return t;
        }

        /// <summary>
        /// Maximum geometric altitude (degrees) of the Sun on a UTC date at this
        /// observer location -- the altitude at solar transit. Equivalent to
        /// <see cref="TargetGeometry.MeridianAltitude"/> evaluated at the sun's
        /// declination at transit instant.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double NoonAltitudeOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            DateTime transit = TransitOn(location, utcDate);
            double latSigned = location.North ? location.Latitude : -location.Latitude;
            double decDeg = SunPosition.DeclinationAt(transit);
            return TargetGeometry.MeridianAltitude(latSigned, decDeg);
        }

        /// <summary>
        /// Length of day on a UTC date (rise-to-set duration). Returns
        /// <see cref="TimeSpan.Zero"/> for polar night, ~24h for polar day.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static TimeSpan DayLengthOn(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);

            RiseAndSetEvent ev = RiseSetOn(location, utcDate);
            if (ev.Rise == null && ev.Set == null)
            {
                // Circumpolar -- determine day vs night by sun altitude at transit.
                DateTime transit = TransitOn(location, utcDate);
                return SunPosition.AltAzAt(location, transit).Altitude > 0.0
                    ? TimeSpan.FromDays(1)
                    : TimeSpan.Zero;
            }
            if (ev.Rise == null || ev.Set == null)
            {
                // Asymmetric polar (only one event in the day). Pragmatic fallback: the
                // sun is up either from set-of-yesterday to set-today or rise-today to
                // rise-tomorrow -- both ~24 hours minus a small piece. Return 12h as a
                // calibration-free midpoint; callers wanting precise behavior should
                // sweep the sun's altitude themselves.
                return TimeSpan.FromHours(12);
            }

            TimeSpan diff = ev.Set.Value - ev.Rise.Value;
            if (diff < TimeSpan.Zero) diff = diff.Add(TimeSpan.FromDays(1));
            return diff;
        }

        /// <summary>
        /// Next sun rise (apparent disc upper limb crossing the refraction-adjusted
        /// horizon) at or after <paramref name="fromUtc"/>. Walks forward up to 366 days
        /// to handle polar sites.
        /// </summary>
        /// <returns>The UTC rise instant, or <see langword="null"/> if no rise occurs
        /// within the search window.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime? NextRise(Location location, DateTime fromUtc)
        {
            ArgumentNullException.ThrowIfNull(location);

            DateTime cursor = TimeKindGuard.AsUtc(fromUtc);
            DateOnly start = DateOnly.FromDateTime(cursor);
            for (int i = 0; i < 366; i++)
            {
                RiseAndSetEvent ev = RiseSetOn(location, start.AddDays(i));
                if (ev.Rise.HasValue && ev.Rise.Value >= cursor) return ev.Rise;
            }
            return null;
        }

        /// <summary>
        /// Next sun set at or after <paramref name="fromUtc"/>. Walks forward up to 366
        /// days; <see langword="null"/> if no set occurs within that window.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime? NextSet(Location location, DateTime fromUtc)
        {
            ArgumentNullException.ThrowIfNull(location);

            DateTime cursor = TimeKindGuard.AsUtc(fromUtc);
            DateOnly start = DateOnly.FromDateTime(cursor);
            for (int i = 0; i < 366; i++)
            {
                RiseAndSetEvent ev = RiseSetOn(location, start.AddDays(i));
                if (ev.Set.HasValue && ev.Set.Value >= cursor) return ev.Set;
            }
            return null;
        }

        /// <summary>
        /// Equation of time in minutes at <paramref name="utc"/>: the difference between
        /// apparent solar time and mean solar time. Roughly +16.4 min near Nov 3,
        /// -14.2 min near Feb 11, ~0 at the four equinox/solstice crossings (~Apr 15,
        /// Jun 13, Sep 1, Dec 25). Used to bridge clock time and sundial time.
        /// </summary>
        /// <remarks>
        /// Implementation: Meeus AA equation 28.3 (compact form using y = tan&#178;(eps/2)).
        /// </remarks>
        public static double EquationOfTimeMinutes(DateTime utc)
        {
            double jd = JulianDate.FromUtc(TimeKindGuard.AsUtc(utc));
            double T = MeeusUtility.T(jd);

            double epsDeg = MeeusUtility.MeanObliquityDeg(T);
            double yTan = Math.Tan(0.5 * epsDeg * MeeusUtility.DegToRad);
            double y = yTan * yTan;

            double e = MeeusUtility.Horner(T,
                0.016708634, -0.000042037, -0.0000001267);
            double L0Deg = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                280.46646, 36000.76983, 0.0003032));
            double MDeg  = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                357.52911, 35999.05029, -0.0001537));

            double L0 = L0Deg * MeeusUtility.DegToRad;
            double M  = MDeg  * MeeusUtility.DegToRad;

            // Meeus 28.3 (radians).
            double E = y * Math.Sin(2.0 * L0)
                     - 2.0 * e * Math.Sin(M)
                     + 4.0 * e * y * Math.Sin(M) * Math.Cos(2.0 * L0)
                     - 0.5 * y * y * Math.Sin(4.0 * L0)
                     - 1.25 * e * e * Math.Sin(2.0 * M);

            // 1 radian = (180/pi) degrees = 4 * (180/pi) minutes (4 min per degree).
            return E * MeeusUtility.RadToDeg * 4.0;
        }

        // Shared implementation: the four named-twilight wrappers + CrossingsOn all reduce
        // to a single SunEphemeris.RiseSet call once the threshold is selected.
        private static RiseAndSetEvent CrossingsOnInternal(
            Location location, DateOnly utcDate, double altitudeDeg)
        {
            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double lonEast   = location.West  ? -location.Longitude :  location.Longitude;
            DateTime midnight = new DateTime(utcDate.Year, utcDate.Month, utcDate.Day, 0, 0, 0, DateTimeKind.Utc);

            (DateTime? rise, DateTime? set) = SunEphemeris.RiseSet(midnight, latSigned, lonEast, altitudeDeg);
            return new RiseAndSetEvent(rise, set);
        }

    }
}
