using System;

namespace Astronomy.Core.Astrometry.Meeus
{
    /// <summary>
    /// Shared math used by the Meeus-based astronomy primitives. Polynomial Horner
    /// evaluation, angle normalisation, and the standard time-derivative T = (JD - J2000)/36525.
    /// All angles in degrees unless explicitly noted.
    /// </summary>
    /// <remarks>
    /// Reference: Jean Meeus, <em>Astronomical Algorithms</em>, 2nd ed. (1998),
    /// chapters 22 (nutation), 25 (sun), 47 (moon), 48 (illumination), 15 (rise/set).
    /// </remarks>
    internal static class MeeusUtility
    {
        public const double JD_J2000 = 2451545.0;
        public const double DaysPerJulianCentury = 36525.0;
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        /// <summary>Julian centuries since J2000.0 TT.</summary>
        public static double T(double julianDate) => (julianDate - JD_J2000) / DaysPerJulianCentury;

        /// <summary>
        /// Horner evaluation of a polynomial in <paramref name="x"/>. Coefficients are in
        /// ascending order: result = coeffs[0] + coeffs[1]*x + coeffs[2]*x^2 + ...
        /// </summary>
        public static double Horner(double x, params double[] coeffs)
        {
            double result = 0.0;
            for (int i = coeffs.Length - 1; i >= 0; i--)
            {
                result = result * x + coeffs[i];
            }
            return result;
        }

        /// <summary>Reduce an angle in degrees to [0, 360).</summary>
        public static double Norm360(double deg)
        {
            double r = deg % 360.0;
            if (r < 0) r += 360.0;
            return r;
        }

        /// <summary>Reduce an angle in degrees to [-180, 180).</summary>
        public static double NormPm180(double deg)
        {
            double r = Norm360(deg);
            if (r >= 180.0) r -= 360.0;
            return r;
        }

        /// <summary>Reduce an hour value to [0, 24).</summary>
        public static double Norm24(double hours)
        {
            double r = hours % 24.0;
            if (r < 0) r += 24.0;
            return r;
        }

        /// <summary>Mean obliquity of the ecliptic in degrees, IAU 1980 (Meeus 22.2).</summary>
        public static double MeanObliquityDeg(double T)
        {
            // 23 26 21.448 + secular terms; arc-seconds expanded.
            double arcsec = Horner(T,
                23.0 * 3600.0 + 26.0 * 60.0 + 21.448,
                -46.8150,
                -0.00059,
                +0.001813);
            return arcsec / 3600.0;
        }

        /// <summary>
        /// True obliquity (= mean obliquity + nutation in obliquity), degrees. For the
        /// truncation we use, sufficient for ~arcsecond-scale moon/sun position.
        /// </summary>
        public static double TrueObliquityDeg(double T)
        {
            double dEpsilonArcsec = NutationInObliquityArcsec(T);
            return MeanObliquityDeg(T) + dEpsilonArcsec / 3600.0;
        }

        /// <summary>
        /// Nutation in longitude (arcseconds), simplified Meeus 22 (period only of the
        /// Moon's ascending node). Sufficient for ~10 arcsec scheduler accuracy.
        /// </summary>
        public static double NutationInLongitudeArcsec(double T)
        {
            double Omega = 125.04452 - 1934.136261 * T;
            double L     = 280.4665  + 36000.7698 * T;     // Sun's mean longitude
            double Lp    = 218.3165  + 481267.8813 * T;    // Moon's mean longitude
            double dPsi = -17.20 * Math.Sin(Omega * DegToRad)
                          - 1.32 * Math.Sin(2.0 * L * DegToRad)
                          - 0.23 * Math.Sin(2.0 * Lp * DegToRad)
                          + 0.21 * Math.Sin(2.0 * Omega * DegToRad);
            return dPsi;
        }

        /// <summary>Nutation in obliquity (arcseconds), simplified Meeus 22.</summary>
        public static double NutationInObliquityArcsec(double T)
        {
            double Omega = 125.04452 - 1934.136261 * T;
            double L     = 280.4665  + 36000.7698 * T;
            double Lp    = 218.3165  + 481267.8813 * T;
            double dEps =  9.20 * Math.Cos(Omega * DegToRad)
                         + 0.57 * Math.Cos(2.0 * L * DegToRad)
                         + 0.10 * Math.Cos(2.0 * Lp * DegToRad)
                         - 0.09 * Math.Cos(2.0 * Omega * DegToRad);
            return dEps;
        }

        /// <summary>
        /// Atmospheric refraction (degrees) at apparent altitude <paramref name="apparentAltDeg"/>.
        /// Bennett's formula with Saemundsson correction; matches NINA's
        /// <c>AstroUtil.GetRefraction</c> within mas. Returns 0 for altitudes below the
        /// horizon (no upward bend modelled past nadir).
        /// </summary>
        public static double RefractionDeg(double apparentAltDeg)
        {
            if (apparentAltDeg < -1.0) return 0.0;
            // Bennett 1982: cot(alt + 7.31/(alt + 4.4)) in degrees, result in arcminutes.
            double h = apparentAltDeg + 7.31 / (apparentAltDeg + 4.4);
            double rArcmin = 1.0 / Math.Tan(h * DegToRad);
            return rArcmin / 60.0;
        }

        /// <summary>
        /// Refracted horizon dip (degrees) at observer elevation
        /// <paramref name="elevationM"/> meters above sea level. The standard nautical /
        /// astronomical formula <c>1.76 * sqrt(h_m)</c> arcminutes already incorporates
        /// mean atmospheric refraction along the line of sight to the horizon; this is
        /// what users mean by "I'm 80 m up so the horizon dips a bit". Returns 0 for
        /// non-positive elevations (no dip below sea level for our purposes).
        /// </summary>
        /// <remarks>
        /// At 80 m: ~0.26&#176; (sunrise/moonrise shift ~25 sec). At 1000 m: ~0.93&#176;
        /// (~3.5 min shift). At 10000 m: ~2.93&#176; (~11 min shift). Geocentric
        /// twilight thresholds (-18, -12, -6) are by convention NOT elevation-corrected
        /// -- they reference the celestial horizontal plane rather than the observer's
        /// apparent horizon -- so callers should only subtract this from the
        /// upper-limb-tangent thresholds (-0.833 for sun, +0.125 for moon).
        /// </remarks>
        public static double HorizonDipDeg(double elevationM)
        {
            if (elevationM <= 0.0) return 0.0;
            return 1.76 * Math.Sqrt(elevationM) / 60.0;
        }
    }
}
