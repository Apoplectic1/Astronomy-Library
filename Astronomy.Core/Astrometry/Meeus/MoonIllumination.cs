using System;

namespace Astronomy.Core.Astrometry.Meeus
{
    /// <summary>
    /// Lunar phase angle and illuminated fraction via Meeus AA chapter 48. Composes the
    /// outputs of <see cref="SunPosition.Apparent"/> and <see cref="MoonPosition.Apparent"/>
    /// -- no independent series of its own.
    /// </summary>
    /// <remarks>
    /// Reference: Jean Meeus, <em>Astronomical Algorithms</em> 2nd ed., chapter 48.
    /// We use the equatorial-form geocentric elongation (Meeus 48.2) computed from the
    /// apparent RA/Dec of the Sun and Moon, then the standard phase-angle / illuminated-
    /// fraction relations 48.3 / 48.1. Accuracy is well below 0.005 (the tolerance our
    /// parity tests check), governed by the Moon-position 10-arcsec series.
    /// </remarks>
    internal static class MoonIllumination
    {
        // 1 AU in km (IAU 2012 nominal).
        private const double AuKm = 149597870.7;

        /// <summary>
        /// Illuminated fraction of the Moon's disc as seen from Earth at <paramref name="jd"/>
        /// (TT). Range <c>[0, 1]</c> where 0 = new, 1 = full.
        /// </summary>
        public static double Fraction(double jd)
        {
            (double sunRa,  double sunDec,  double sunR_au)  = SunPosition.Apparent(jd);
            (double moonRa, double moonDec, double moonD_km) = MoonPosition.Apparent(jd);

            double sunRaRad   = sunRa   * MeeusUtility.DegToRad;
            double sunDecRad  = sunDec  * MeeusUtility.DegToRad;
            double moonRaRad  = moonRa  * MeeusUtility.DegToRad;
            double moonDecRad = moonDec * MeeusUtility.DegToRad;

            // Geocentric elongation Moon-from-Sun -- Meeus 48.2.
            double cosPsi = Math.Sin(sunDecRad) * Math.Sin(moonDecRad)
                          + Math.Cos(sunDecRad) * Math.Cos(moonDecRad)
                          * Math.Cos(sunRaRad - moonRaRad);
            if (cosPsi >  1.0) cosPsi =  1.0;
            if (cosPsi < -1.0) cosPsi = -1.0;
            double psi = Math.Acos(cosPsi);

            // Phase angle (Sun-Moon-Earth at the Moon) -- Meeus 48.3.
            double sunR_km = sunR_au * AuKm;
            double i = Math.Atan2(sunR_km * Math.Sin(psi), moonD_km - sunR_km * cosPsi);

            // Illuminated fraction -- Meeus 48.1.
            return (1.0 + Math.Cos(i)) / 2.0;
        }
    }
}
