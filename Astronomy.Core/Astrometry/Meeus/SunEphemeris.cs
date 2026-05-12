using System;

namespace Astronomy.Core.Astrometry.Meeus
{
    /// <summary>
    /// Apparent geocentric solar position via Meeus AA chapter 25 (low-precision form,
    /// ~0.01 deg / ~0.01s accuracy) and rise/set/twilight events via Meeus chapter 15.
    /// </summary>
    /// <remarks>
    /// Reference: Jean Meeus, <em>Astronomical Algorithms</em> 2nd ed., chapters 15
    /// (rising / transit / setting) and 25 (solar coordinates -- low precision).
    /// Accuracy is sufficient for scheduler use: ~30s precision on twilight events
    /// at moderate latitudes; degrades near the polar circles where the iterative
    /// solver may not converge for thresholds near the sun's noon altitude.
    /// </remarks>
    internal static class SunEphemeris
    {
        /// <summary>
        /// Apparent geocentric equatorial coordinates of the Sun at <paramref name="jd"/>
        /// (TT). Returns <c>(raDeg, decDeg, R_au)</c> where RA is in [0, 360), Dec in
        /// [-90, +90], and R is the Sun-Earth distance in AU. Aberration and the
        /// nutation-in-longitude correction are included (apparent place).
        /// </summary>
        public static (double RaDeg, double DecDeg, double R) Apparent(double jd)
        {
            double T = MeeusUtility.T(jd);

            // Meeus 25.2: mean longitude (deg).
            double L0 = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                280.46646, 36000.76983, 0.0003032));

            // Meeus 25.3: mean anomaly (deg).
            double M = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                357.52911, 35999.05029, -0.0001537));

            // Meeus 25.4: eccentricity (dimensionless).
            double e = MeeusUtility.Horner(T, 0.016708634, -0.000042037, -0.0000001267);

            // Equation of centre, in degrees.
            double Mrad = M * MeeusUtility.DegToRad;
            // Each coefficient is a polynomial in T (FMA-evaluated), then weighted by sin(kM),
            // and the three terms accumulate via FMA into C.
            double c1 = Math.FusedMultiplyAdd(-0.000014, T * T,
                        Math.FusedMultiplyAdd(-0.004817, T, 1.914602));
            double c2 = Math.FusedMultiplyAdd(-0.000101, T, 0.019993);
            double C  = Math.FusedMultiplyAdd(c1, Math.Sin(Mrad),
                        Math.FusedMultiplyAdd(c2, Math.Sin(2 * Mrad),
                                       0.000289 * Math.Sin(3 * Mrad)));

            double trueLon = L0 + C;
            double trueAnom = M + C;

            // Radius vector (AU). Meeus 25.5.
            double R = 1.000001018 * (1.0 - e * e) / (1.0 + e * Math.Cos(trueAnom * MeeusUtility.DegToRad));

            // Apparent longitude: subtract aberration + nutation contribution. Meeus 25.8.
            double Omega = 125.04 - 1934.136 * T;
            double appLon = trueLon - 0.00569 - 0.00478 * Math.Sin(Omega * MeeusUtility.DegToRad);

            // Corrected obliquity for the apparent place (Meeus 25.8).
            double eps0 = MeeusUtility.MeanObliquityDeg(T);
            double eps  = eps0 + 0.00256 * Math.Cos(Omega * MeeusUtility.DegToRad);

            // To equatorial. Beta (latitude) is ~0 for the Sun; we ignore it (max ~1.2").
            double appLonRad = appLon * MeeusUtility.DegToRad;
            double epsRad    = eps    * MeeusUtility.DegToRad;
            double raRad  = Math.Atan2(Math.Cos(epsRad) * Math.Sin(appLonRad), Math.Cos(appLonRad));
            double decRad = Math.Asin(Math.Sin(epsRad) * Math.Sin(appLonRad));

            double raDeg = MeeusUtility.Norm360(raRad * MeeusUtility.RadToDeg);
            double decDeg = decRad * MeeusUtility.RadToDeg;

            return (raDeg, decDeg, R);
        }

        /// <summary>
        /// Rise / set times for the Sun at the given observer on <paramref name="dateUtc"/>'s
        /// calendar day in UTC, when the geometric altitude crosses
        /// <paramref name="targetAltitudeDeg"/>. Used for sun rise/set
        /// (target alt = -0.833 to account for refraction + solar disc semi-diameter),
        /// civil twilight (-6), nautical (-12), astronomical (-18).
        /// </summary>
        /// <param name="dateUtc">A UTC instant whose calendar date selects the day.
        /// Rise/set are computed for that UTC day (00:00 to 24:00 UT). Kind=Utc expected;
        /// any other Kind is treated as Utc.</param>
        /// <param name="latDeg">Observer latitude, signed (positive north).</param>
        /// <param name="lonDeg">Observer longitude, signed (positive east). Meeus 15
        /// uses west-positive longitudes; we negate inside.</param>
        /// <param name="targetAltitudeDeg">Geometric altitude of the sun's centre at
        /// the event in degrees. -0.833 for "official" rise/set; -18, -12, -6 for the
        /// three twilight thresholds.</param>
        /// <returns>
        /// (Rise, Set) as nullable UTC instants. Either or both may be null when the
        /// sun is circumpolar (never rises / never sets) above or below the threshold.
        /// </returns>
        public static (DateTime? Rise, DateTime? Set) RiseSet(
            DateTime dateUtc, double latDeg, double lonDeg, double targetAltitudeDeg)
        {
            // Snap to the calendar day's 0h UT.
            DateTime day0 = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            double jd0 = Astronomy.Core.Time.JulianDate.FromUtc(day0);

            // Apparent sidereal time at Greenwich at 0h UT, in degrees.
            // Theta0 = 100.46061837 + 36000.770053608 T + 0.000387933 T^2 - T^3/38710000  (Meeus 12.4 / 15.13)
            double T0 = MeeusUtility.T(jd0);
            double theta0 = MeeusUtility.Norm360(
                100.46061837 + 36000.770053608 * T0 + 0.000387933 * T0 * T0 - T0 * T0 * T0 / 38710000.0);

            // Sun position at 0h, 24h, 48h (we want -1h, 0h, +1h around the day in centuries
            // of T). Meeus uses three days (-1, 0, +1) for second-difference interpolation.
            (double ra0, double dec0, double _r0) = Apparent(jd0 - 1.0);
            (double ra1, double dec1, double _r1) = Apparent(jd0);
            (double ra2, double dec2, double _r2) = Apparent(jd0 + 1.0);

            // Resolve the +-180 wrap in RA between consecutive days (sun moves ~1 deg/day,
            // so the sole concern is the seam near 0h/360h). Force monotonic sequence.
            ra1 = Unwrap(ra0, ra1);
            ra2 = Unwrap(ra1, ra2);

            // Approximate transit time fraction (Meeus 15.2). lonDeg east-positive; Meeus
            // uses west-positive so negate.
            double L = -lonDeg;
            double phi = latDeg;

            double m0 = (ra1 + L - theta0) / 360.0;
            m0 = Frac(m0);

            // Hour angle at the threshold altitude (Meeus 15.1).
            double cosH0 = Math.FusedMultiplyAdd(
                              -Math.Sin(phi * MeeusUtility.DegToRad),
                               Math.Sin(dec1 * MeeusUtility.DegToRad),
                               Math.Sin(targetAltitudeDeg * MeeusUtility.DegToRad))
                        / (Math.Cos(phi * MeeusUtility.DegToRad) * Math.Cos(dec1 * MeeusUtility.DegToRad));

            DateTime? rise = null;
            DateTime? set  = null;

            bool circumpolarAbove = cosH0 < -1.0; // sun never goes below threshold
            bool circumpolarBelow = cosH0 >  1.0; // sun never reaches threshold

            if (!circumpolarAbove && !circumpolarBelow)
            {
                double H0deg = Math.Acos(cosH0) * MeeusUtility.RadToDeg;
                double m1 = Frac(m0 - H0deg / 360.0);
                double m2 = Frac(m0 + H0deg / 360.0);

                // Iterate two rounds; each correction is small (< 1 minute typical).
                for (int iter = 0; iter < 3; iter++)
                {
                    m1 = RefineEvent(m1, theta0, L, phi, ra0, ra1, ra2, dec0, dec1, dec2, targetAltitudeDeg, isTransit: false);
                    m2 = RefineEvent(m2, theta0, L, phi, ra0, ra1, ra2, dec0, dec1, dec2, targetAltitudeDeg, isTransit: false);
                }

                rise = day0.AddDays(m1);
                set  = day0.AddDays(m2);
            }
            // Circumpolar: leave both null. Caller treats null as "no event today" --
            // matches NINA's convention.

            return (rise, set);
        }

        // Meeus 15.7-15.9: refine a candidate fraction-of-day m for either a rise/set
        // (isTransit=false) or transit (isTransit=true) event by interpolating Sun
        // RA/Dec to that moment and solving the residual.
        private static double RefineEvent(
            double m, double theta0, double L, double phi,
            double ra0, double ra1, double ra2, double dec0, double dec1, double dec2,
            double targetAltDeg, bool isTransit)
        {
            // Greenwich sidereal time at moment m (Meeus 15.4).
            double thetaM = MeeusUtility.Norm360(theta0 + 360.985647 * m);

            // Interpolate RA and Dec to fraction n of the day (Meeus 15.5: n = m + DeltaT/86400),
            // where DeltaT is TT-UT in seconds. For our scheduler use, DeltaT ~ 70s; the
            // resulting position error is well below 30s timing precision so we ignore it.
            double n = m;
            double raInterp  = Interpolate3(ra0,  ra1,  ra2,  n);
            double decInterp = Interpolate3(dec0, dec1, dec2, n);

            // Local hour angle at the sun.
            double H = MeeusUtility.NormPm180(thetaM - L - raInterp);

            // Geometric altitude.
            double altRad = Math.Asin(Math.FusedMultiplyAdd(
                Math.Cos(phi * MeeusUtility.DegToRad) * Math.Cos(decInterp * MeeusUtility.DegToRad),
                Math.Cos(H * MeeusUtility.DegToRad),
                Math.Sin(phi * MeeusUtility.DegToRad) * Math.Sin(decInterp * MeeusUtility.DegToRad)));
            double altDeg = altRad * MeeusUtility.RadToDeg;

            double dm;
            if (isTransit)
            {
                // Meeus 15.6: correction = -H/360.
                dm = -H / 360.0;
            }
            else
            {
                // Meeus 15.7: correction = (alt - h0) / (360 * cos(dec) * cos(phi) * sin(H))
                double denom = 360.0 * Math.Cos(decInterp * MeeusUtility.DegToRad)
                                     * Math.Cos(phi      * MeeusUtility.DegToRad)
                                     * Math.Sin(H        * MeeusUtility.DegToRad);
                if (Math.Abs(denom) < 1e-12) return m; // pathological; bail out
                dm = (altDeg - targetAltDeg) / denom;
            }

            return Frac(m + dm);
        }

        // Three-point quadratic interpolation through (-1, y0), (0, y1), (+1, y2) at t = n.
        // Meeus 3.3.
        private static double Interpolate3(double y0, double y1, double y2, double n)
        {
            double a = y1 - y0;
            double b = y2 - y1;
            double c = b - a;
            return y1 + 0.5 * n * (a + b + n * c);
        }

        // Force x2 to be on the same side of the 360-deg seam as x1 (so a near-360 vs
        // near-0 pair becomes monotonic).
        private static double Unwrap(double prev, double cur)
        {
            if (cur - prev > 180.0) return cur - 360.0;
            if (prev - cur > 180.0) return cur + 360.0;
            return cur;
        }

        // Reduce a fraction-of-day to [0, 1).
        private static double Frac(double m)
        {
            double r = m - Math.Floor(m);
            return r;
        }
    }
}
