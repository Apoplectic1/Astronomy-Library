using System;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Solar engineering helpers: extraterrestrial irradiance, clear-sky direct-normal
    /// and global-horizontal irradiance, and optimal panel-tilt formulas. Targeted at
    /// PV-system feasibility studies and panel-orientation decisions; not at high-
    /// precision atmospheric radiative transfer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Irradiance helpers use the Kasten-Linke clear-sky model (1996 update of ESRA),
    /// parameterised by <em>Linke turbidity</em> -- a single dimensionless atmospheric
    /// transmittance number. Typical values: 1.5 (high-altitude / Antarctic), 3.0
    /// (clean continental, default), 4-5 (urban / hazy), 6+ (industrial / desert dust).
    /// Accuracy: ~5-10% under stated atmospheric conditions; intentionally not modelling
    /// aerosol composition, water vapour, ozone, or albedo.
    /// </para>
    /// <para>
    /// Optimal-tilt formulas are panel-orientation rules of thumb. The "annual" form
    /// uses the empirical Christofides 2002 fit; the "seasonal" form returns the daily
    /// noon-perpendicular tilt. For real installations the right answer also depends on
    /// shading, latitude clipping, mounting cost, and battery vs grid-tied regime --
    /// these helpers are starting points, not engineering specifications.
    /// </para>
    /// </remarks>
    public static class SunPower
    {
        // Solar constant in W/m². CODATA / IAU 2015 nominal total solar irradiance at 1 AU.
        private const double SolarConstantWm2 = 1361.0;

        /// <summary>
        /// Extraterrestrial direct-normal irradiance (W/m²) at the top of the atmosphere
        /// at <paramref name="utc"/>, accounting for Earth-Sun distance variation. Ranges
        /// roughly 1314 (aphelion, ~July 4) to 1408 (perihelion, ~Jan 4).
        /// </summary>
        public static double ExtraterrestrialIrradianceAt(DateTime utc)
        {
            double distAu = SunPosition.EquatorialAt(utc).DistanceAu;
            return SolarConstantWm2 / (distAu * distAu);
        }

        /// <summary>
        /// Clear-sky direct-normal irradiance (W/m², beam component) at the observer at
        /// <paramref name="utc"/>. Returns 0 if the Sun is at or below the horizon.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <param name="linkeTurbidity">Linke turbidity factor (dimensionless). 1 = ideal
        /// Rayleigh-only atmosphere; 3 = typical clean continental (default); 5+ = hazy.
        /// Caller-supplied minimum is clamped to 1 internally.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double ClearSkyDirectNormalAt(
            Location location, DateTime utc, double linkeTurbidity = 3.0)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (linkeTurbidity < 1.0) linkeTurbidity = 1.0;

            double altDeg = SunPosition.ApparentAltitudeAt(location, utc);
            if (altDeg <= 0.0) return 0.0;

            double am = SunTracking.AirMassKastenYoung(altDeg);
            double i0 = ExtraterrestrialIrradianceAt(utc);

            // Kasten 1996 / ESRA Rayleigh optical depth at given air mass.
            double am2 = am * am;
            double am3 = am2 * am;
            double am4 = am3 * am;
            double deltaR = 1.0 / (6.6296 + 1.7513 * am - 0.1202 * am2 + 0.0065 * am3 - 0.00013 * am4);

            return i0 * Math.Exp(-0.8662 * linkeTurbidity * am * deltaR);
        }

        /// <summary>
        /// Clear-sky global horizontal irradiance (W/m²) at the observer at
        /// <paramref name="utc"/> -- direct-beam projected onto a horizontal plane plus
        /// a simplified diffuse contribution scaled by Linke turbidity. Returns 0 if
        /// the Sun is at or below the horizon.
        /// </summary>
        /// <remarks>
        /// Diffuse contribution is a coarse linear-in-turbidity approximation
        /// (~10% * I_0 * (TL - 1) * sin(alt)); good to ~10% under typical clear-sky
        /// conditions. Callers needing higher precision should use a full radiative-
        /// transfer model (libRadtran, SMARTS).
        /// </remarks>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <param name="linkeTurbidity">Linke turbidity factor.</param>
        public static double ClearSkyGlobalHorizontalAt(
            Location location, DateTime utc, double linkeTurbidity = 3.0)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (linkeTurbidity < 1.0) linkeTurbidity = 1.0;

            double altDeg = SunPosition.ApparentAltitudeAt(location, utc);
            if (altDeg <= 0.0) return 0.0;

            double dni = ClearSkyDirectNormalAt(location, utc, linkeTurbidity);
            double sinAlt = Math.Sin(altDeg * MeeusUtility.DegToRad);
            double i0 = ExtraterrestrialIrradianceAt(utc);
            double diffuseHorizontal = i0 * 0.10 * (linkeTurbidity - 1.0) * sinAlt;
            if (diffuseHorizontal < 0.0) diffuseHorizontal = 0.0;

            return dni * sinAlt + diffuseHorizontal;
        }

        /// <summary>
        /// Optimal annual fixed-tilt angle (degrees from horizontal) for a sun-facing
        /// panel at <paramref name="location"/>, via Christofides 2002 empirical fit
        /// <c>0.76 * |latitude| + 3.1</c>. Result is hemisphere-agnostic: the panel is
        /// assumed to face the equator (south for North-hemisphere, north for South).
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double OptimalAnnualTiltDeg(Location location)
        {
            ArgumentNullException.ThrowIfNull(location);
            return 0.76 * location.Latitude + 3.1;
        }

        /// <summary>
        /// Optimal panel-tilt angle (degrees from horizontal) on a UTC date such that
        /// the panel faces the Sun perpendicularly at solar noon: <c>|lat - dec|</c>
        /// using signed latitude and the Sun's declination at transit. Useful for
        /// seasonally-adjustable mounts.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double OptimalSeasonalTiltDeg(Location location, DateOnly utcDate)
        {
            ArgumentNullException.ThrowIfNull(location);
            DateTime transit = SunEvents.TransitOn(location, utcDate);
            double decDeg = SunPosition.DeclinationAt(transit);
            double latSigned = location.North ? location.Latitude : -location.Latitude;
            return Math.Abs(latSigned - decDeg);
        }
    }
}
