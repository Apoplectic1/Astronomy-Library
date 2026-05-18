using System;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Time;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Apparent topocentric / geocentric position of the Sun, plus apparent angular
    /// diameter. Pure; no static mutable state; safe to call from concurrent background
    /// tasks. Replaces the Sun-related methods that previously lived scattered across
    /// <see cref="AstroUtil"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Composed over <see cref="SunEphemeris.Apparent"/> (Meeus AA chapter 25, low-precision
    /// form, ~0.01&#176; accuracy on RA/Dec). Sufficient for tracking, twilight, and
    /// solar-imaging pipelines. Solar parallax (~9&#8243;) is intentionally not modelled --
    /// well below the precision a solar tracker, twilight estimator, or solar imager can
    /// resolve.
    /// </para>
    /// <para>
    /// All <see cref="DateTime"/> inputs are expected as <see cref="DateTimeKind.Utc"/>;
    /// non-Utc kinds are coerced via <see cref="DateTime.SpecifyKind"/> (the value is
    /// treated as if it were UTC -- callers passing local time without converting first
    /// will get a wrong answer).
    /// </para>
    /// </remarks>
    public static class SunPosition
    {
        // IAU 2015 nominal solar radius (6.957e5 km) divided by 1 AU (149597870.7 km).
        // Used by ApparentDiameter*. Matches Meeus's quoted value to 4 sig figs.
        private const double SolarRadiusAu = 695700.0 / 149597870.7;

        /// <summary>
        /// Apparent geometric altitude and azimuth of the Sun (degrees) at
        /// <paramref name="utc"/> as seen from <paramref name="location"/>. Refraction is
        /// intentionally not applied; see <see cref="ApparentAltitudeAt"/> for the refracted
        /// variant.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static AltAz AltAzAt(Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(location);

            DateTime utcOnly = TimeKindGuard.AsUtc(utc);
            double jd = JulianDate.FromUtc(utcOnly);

            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double lonEast   = location.West  ? -location.Longitude :  location.Longitude;

            double lstDeg = SiderealTime.Local(utcOnly, lonEast) * 15.0;
            (double raDeg, double decDeg, _) = SunEphemeris.Apparent(jd);

            double haHours = MeeusUtility.NormPm180(lstDeg - raDeg) / 15.0;
            double altDeg  = TargetGeometry.AltitudeAtHourAngle(haHours, latSigned, decDeg);
            double azDeg   = TargetGeometry.AzimuthAtHourAngle(haHours, latSigned, decDeg);
            return new AltAz(altDeg, azDeg);
        }

        /// <summary>
        /// Apparent geocentric equatorial coordinates of the Sun at <paramref name="utc"/>.
        /// Returns <c>(RaDeg, DecDeg, DistanceAu)</c> with RA in <c>[0, 360)</c>, Dec in
        /// roughly <c>[-23.5, +23.5]</c>, and distance in <c>[0.983, 1.017]</c> AU across
        /// the year.
        /// </summary>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        public static (double RaDeg, double DecDeg, double DistanceAu) EquatorialAt(DateTime utc)
        {
            double jd = JulianDate.FromUtc(TimeKindGuard.AsUtc(utc));
            return SunEphemeris.Apparent(jd);
        }

        /// <summary>
        /// Apparent solar declination (degrees) at <paramref name="utc"/>. Convenience over
        /// <see cref="EquatorialAt"/>'s second component; varies between roughly
        /// -23.44&#176; (December solstice) and +23.44&#176; (June solstice).
        /// </summary>
        public static double DeclinationAt(DateTime utc)
            => EquatorialAt(utc).DecDeg;

        /// <summary>
        /// Local hour angle of the Sun in sidereal hours <c>[-12, +12)</c> at the given
        /// instant. 0 at upper transit (apparent solar noon), positive after, negative
        /// before.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double HourAngleAt(Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(location);

            DateTime utcOnly = TimeKindGuard.AsUtc(utc);
            double lonEast = location.West ? -location.Longitude : location.Longitude;
            double lstDeg  = SiderealTime.Local(utcOnly, lonEast) * 15.0;
            (double raDeg, _, _) = SunEphemeris.Apparent(JulianDate.FromUtc(utcOnly));
            return MeeusUtility.NormPm180(lstDeg - raDeg) / 15.0;
        }

        /// <summary>
        /// Apparent (refraction-corrected) altitude of the Sun in degrees. Adds Bennett's
        /// atmospheric refraction to the geometric altitude returned by
        /// <see cref="AltAzAt"/>.
        /// </summary>
        /// <remarks>
        /// Bennett's formula is defined for apparent altitude as input; we feed the
        /// geometric altitude as the standard approximation (Meeus AA p. 105). Error is
        /// below 0.01&#176; at all altitudes -- well below tracker precision.
        /// </remarks>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double ApparentAltitudeAt(Location location, DateTime utc)
        {
            double altGeom = AltAzAt(location, utc).Altitude;
            return altGeom + Refraction.SaemundssonDeg(altGeom);
        }

        /// <summary>
        /// Apparent angular diameter of the Sun in arcseconds at <paramref name="utc"/>.
        /// Varies from ~1882&#8243; at aphelion (~July 4) to ~1955&#8243; at perihelion
        /// (~January 4) due to the Earth-Sun distance changing across the year.
        /// </summary>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        public static double ApparentDiameterArcsecAt(DateTime utc)
        {
            double distAu = EquatorialAt(utc).DistanceAu;
            double diamRad = 2.0 * Math.Atan(SolarRadiusAu / distAu);
            return diamRad * MeeusUtility.RadToDeg * 3600.0;
        }

        /// <summary>
        /// Apparent angular diameter of the Sun in degrees. Equivalent to
        /// <see cref="ApparentDiameterArcsecAt"/> divided by 3600.
        /// </summary>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        public static double ApparentDiameterDegAt(DateTime utc)
            => ApparentDiameterArcsecAt(utc) / 3600.0;

    }
}
