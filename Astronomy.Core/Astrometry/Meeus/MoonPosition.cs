using System;

namespace Astronomy.Core.Astrometry.Meeus
{
    /// <summary>
    /// Apparent geocentric lunar position via Meeus AA chapter 47, using the truncated
    /// periodic-series tables 47.A (longitude / distance) and 47.B (latitude). Accuracy
    /// is ~10 arcsec in longitude / latitude and ~50 km in distance, which is ample for
    /// scheduler use (tens-of-arcsec moon-target separations, twilight-window decisions).
    /// </summary>
    /// <remarks>
    /// Reference: Jean Meeus, <em>Astronomical Algorithms</em> 2nd ed., chapter 47.
    /// The 60-term tables are reproduced here verbatim from pg. 339-341. Aberration of
    /// the moon (~&lt; 0.005 deg) is intentionally not modelled -- well below tolerance.
    /// </remarks>
    internal static class MoonPosition
    {
        // Earth's equatorial radius, km. IAU 1976 value used by Meeus pg. 391.
        private const double EarthEquatorialRadiusKm = 6378.14;

        // Table 47.A: longitude (sigma_l) and distance (sigma_r) periodic terms.
        // Columns per row: D, M, M', F, sigma_l (in 0.000001 deg), sigma_r (in 0.001 km).
        // |M| == 1 multiplies the coefficient by E; |M| == 2 by E^2.
        // Stored flat (row-major, stride = 6) instead of int[,] -- the JIT generates
        // tighter code for 1D arrays and bounds-check elision works better, shaving a
        // few percent off the hot 60-iteration loop in ApparentEcliptic.
        private const int LRStride = 6;
        private static readonly int[] mTermsLR = new int[]
        {
            0,  0,  1,  0,  6288774, -20905355,
            2,  0, -1,  0,  1274027,  -3699111,
            2,  0,  0,  0,   658314,  -2955968,
            0,  0,  2,  0,   213618,   -569925,
            0,  1,  0,  0,  -185116,     48888,
            0,  0,  0,  2,  -114332,     -3149,
            2,  0, -2,  0,    58793,    246158,
            2, -1, -1,  0,    57066,   -152138,
            2,  0,  1,  0,    53322,   -170733,
            2, -1,  0,  0,    45758,   -204586,
            0,  1, -1,  0,   -40923,   -129620,
            1,  0,  0,  0,   -34720,    108743,
            0,  1,  1,  0,   -30383,    104755,
            2,  0,  0, -2,    15327,     10321,
            0,  0,  1,  2,   -12528,         0,
            0,  0,  1, -2,    10980,     79661,
            4,  0, -1,  0,    10675,    -34782,
            0,  0,  3,  0,    10034,    -23210,
            4,  0, -2,  0,     8548,    -21636,
            2,  1, -1,  0,    -7888,     24208,
            2,  1,  0,  0,    -6766,     30824,
            1,  0, -1,  0,    -5163,     -8379,
            1,  1,  0,  0,     4987,    -16675,
            2, -1,  1,  0,     4036,    -12831,
            2,  0,  2,  0,     3994,    -10445,
            4,  0,  0,  0,     3861,    -11650,
            2,  0, -3,  0,     3665,     14403,
            0,  1, -2,  0,    -2689,     -7003,
            2,  0, -1,  2,    -2602,         0,
            2, -1, -2,  0,     2390,     10056,
            1,  0,  1,  0,    -2348,      6322,
            2, -2,  0,  0,     2236,     -9884,
            0,  1,  2,  0,    -2120,      5751,
            0,  2,  0,  0,    -2069,         0,
            2, -2, -1,  0,     2048,     -4950,
            2,  0,  1, -2,    -1773,      4130,
            2,  0,  0,  2,    -1595,         0,
            4, -1, -1,  0,     1215,     -3958,
            0,  0,  2,  2,    -1110,         0,
            3,  0, -1,  0,     -892,      3258,
            2,  1,  1,  0,     -810,      2616,
            4, -1, -2,  0,      759,     -1897,
            0,  2, -1,  0,     -713,     -2117,
            2,  2, -1,  0,     -700,      2354,
            2,  1, -2,  0,      691,         0,
            2, -1,  0, -2,      596,         0,
            4,  0,  1,  0,      549,     -1423,
            0,  0,  4,  0,      537,     -1117,
            4, -1,  0,  0,      520,     -1571,
            1,  0, -2,  0,     -487,     -1739,
            2,  1,  0, -2,     -399,         0,
            0,  0,  2, -2,     -381,     -4421,
            1,  1,  1,  0,      351,         0,
            3,  0, -2,  0,     -340,         0,
            4,  0, -3,  0,      330,         0,
            2, -1,  2,  0,      327,         0,
            0,  2,  1,  0,     -323,      1165,
            1,  1, -1,  0,      299,         0,
            2,  0,  3,  0,      294,         0,
            2,  0, -1, -2,        0,      8752,
        };

        // Table 47.B: latitude (sigma_b) periodic terms.
        // Columns per row: D, M, M', F, sigma_b (in 0.000001 deg).
        // |M| == 1 multiplies by E; |M| == 2 by E^2.
        // Stored flat (stride = 5) for the same reason as mTermsLR.
        private const int BStride = 5;
        private static readonly int[] mTermsB = new int[]
        {
            0,  0,  0,  1,  5128122,
            0,  0,  1,  1,   280602,
            0,  0,  1, -1,   277693,
            2,  0,  0, -1,   173237,
            2,  0, -1,  1,    55413,
            2,  0, -1, -1,    46271,
            2,  0,  0,  1,    32573,
            0,  0,  2,  1,    17198,
            2,  0,  1, -1,     9266,
            0,  0,  2, -1,     8822,
            2, -1,  0, -1,     8216,
            2,  0, -2, -1,     4324,
            2,  0,  1,  1,     4200,
            2,  1,  0, -1,    -3359,
            2, -1, -1,  1,     2463,
            2, -1,  0,  1,     2211,
            2, -1, -1, -1,     2065,
            0,  1, -1, -1,    -1870,
            4,  0, -1, -1,     1828,
            0,  1,  0,  1,    -1794,
            0,  0,  0,  3,    -1749,
            0,  1, -1,  1,    -1565,
            1,  0,  0,  1,    -1491,
            0,  1,  1,  1,    -1475,
            0,  1,  1, -1,    -1410,
            0,  1,  0, -1,    -1344,
            1,  0,  0, -1,    -1335,
            0,  0,  3,  1,     1107,
            4,  0,  0, -1,     1021,
            4,  0, -1,  1,      833,
            0,  0,  1, -3,      777,
            4,  0, -2,  1,      671,
            2,  0,  0, -3,      607,
            2,  0,  2, -1,      596,
            2, -1,  1, -1,      491,
            2,  0, -2,  1,     -451,
            0,  0,  3, -1,      439,
            2,  0,  2,  1,      422,
            2,  0, -3, -1,      421,
            2,  1, -1,  1,     -366,
            2,  1,  0,  1,     -351,
            4,  0,  0,  1,      331,
            2, -1,  1,  1,      315,
            2, -2,  0, -1,      302,
            0,  0,  1,  3,     -283,
            2,  1,  1, -1,     -229,
            1,  1,  0, -1,      223,
            1,  1,  0,  1,      223,
            0,  1, -2, -1,     -220,
            2,  1, -1, -1,     -220,
            1,  0,  1,  1,     -185,
            2, -1, -2, -1,      181,
            0,  1,  2,  1,     -177,
            4,  0, -2, -1,      176,
            4, -1, -1, -1,      166,
            1,  0,  1, -1,     -164,
            4,  0,  1, -1,      132,
            1,  0, -1, -1,     -119,
            4, -1,  0, -1,      115,
            2, -2,  0,  1,      107,
        };

        /// <summary>
        /// Apparent geocentric ecliptic coordinates of the Moon at <paramref name="jd"/>
        /// (TT). Returns <c>(lonDeg, latDeg, distanceKm)</c>; longitude already includes
        /// the nutation-in-longitude correction (apparent place).
        /// </summary>
        public static (double LonDeg, double LatDeg, double DistanceKm) ApparentEcliptic(double jd)
        {
            double T = MeeusUtility.T(jd);

            // Mean longitude of the Moon -- Meeus 47.1.
            double Lp = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                218.3164477, 481267.88123421, -0.0015786, 1.0 / 538841.0, -1.0 / 65194000.0));

            // Mean elongation of the Moon from the Sun -- Meeus 47.2.
            double D = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                297.8501921, 445267.1114034, -0.0018819, 1.0 / 545868.0, -1.0 / 113065000.0));

            // Sun's mean anomaly -- Meeus 47.3.
            double M = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                357.5291092, 35999.0502909, -0.0001536, 1.0 / 24490000.0));

            // Moon's mean anomaly -- Meeus 47.4.
            double Mp = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                134.9633964, 477198.8675055, 0.0087414, 1.0 / 69699.0, -1.0 / 14712000.0));

            // Moon's argument of latitude -- Meeus 47.5.
            double F = MeeusUtility.Norm360(MeeusUtility.Horner(T,
                93.2720950, 483202.0175233, -0.0036539, -1.0 / 3526000.0, 1.0 / 863310000.0));

            // Additional arguments (Meeus pg. 338).
            double A1 = MeeusUtility.Norm360(119.75 + 131.849 * T);
            double A2 = MeeusUtility.Norm360(53.09  + 479264.290 * T);
            double A3 = MeeusUtility.Norm360(313.45 + 481266.484 * T);

            // Eccentricity of Earth's orbit -- multiplies coefficients of M-bearing terms.
            double E  = MeeusUtility.Horner(T, 1.0, -0.002516, -0.0000074);
            double E2 = E * E;

            // Sum periodic series for longitude (sigma_l) and distance (sigma_r).
            double sigmaL = 0.0;
            double sigmaR = 0.0;
            int[] termsLR = mTermsLR; // local copy of the field reference helps the JIT hoist the bounds-check
            for (int idx = 0; idx < termsLR.Length; idx += LRStride)
            {
                int dC  = termsLR[idx];
                int mC  = termsLR[idx + 1];
                int mpC = termsLR[idx + 2];
                int fC  = termsLR[idx + 3];
                double lC = termsLR[idx + 4];
                double rC = termsLR[idx + 5];

                // Accumulate the linear-combo argument with FMA: dC*D + mC*M + mpC*Mp + fC*F.
                double arg = Math.FusedMultiplyAdd(dC,  D,
                             Math.FusedMultiplyAdd(mC,  M,
                             Math.FusedMultiplyAdd(mpC, Mp, fC * F))) * MeeusUtility.DegToRad;

                double mult = 1.0;
                int absM = mC < 0 ? -mC : mC;
                if      (absM == 1) mult = E;
                else if (absM == 2) mult = E2;

                // sigmaL += (lC*mult) * sin(arg) -> FMA collapses the += into one round.
                sigmaL = Math.FusedMultiplyAdd(lC * mult, Math.Sin(arg), sigmaL);
                sigmaR = Math.FusedMultiplyAdd(rC * mult, Math.Cos(arg), sigmaR);
            }

            // Sum periodic series for latitude (sigma_b).
            double sigmaB = 0.0;
            int[] termsB = mTermsB;
            for (int idx = 0; idx < termsB.Length; idx += BStride)
            {
                int dC  = termsB[idx];
                int mC  = termsB[idx + 1];
                int mpC = termsB[idx + 2];
                int fC  = termsB[idx + 3];
                double bC = termsB[idx + 4];

                double arg = Math.FusedMultiplyAdd(dC,  D,
                             Math.FusedMultiplyAdd(mC,  M,
                             Math.FusedMultiplyAdd(mpC, Mp, fC * F))) * MeeusUtility.DegToRad;

                double mult = 1.0;
                int absM = mC < 0 ? -mC : mC;
                if      (absM == 1) mult = E;
                else if (absM == 2) mult = E2;

                sigmaB = Math.FusedMultiplyAdd(bC * mult, Math.Sin(arg), sigmaB);
            }

            // Additive terms outside the table (Meeus pg. 342). Venus / Jupiter pulls and
            // the equation-of-the-figure contribution.
            double a1Rad = A1 * MeeusUtility.DegToRad;
            double a2Rad = A2 * MeeusUtility.DegToRad;
            double a3Rad = A3 * MeeusUtility.DegToRad;
            double lpRad = Lp * MeeusUtility.DegToRad;
            double mpRad = Mp * MeeusUtility.DegToRad;
            double fRad  = F  * MeeusUtility.DegToRad;

            sigmaL += 3958.0 * Math.Sin(a1Rad)
                    + 1962.0 * Math.Sin(lpRad - fRad)
                    +  318.0 * Math.Sin(a2Rad);

            sigmaB += -2235.0 * Math.Sin(lpRad)
                    +   382.0 * Math.Sin(a3Rad)
                    +   175.0 * Math.Sin(a1Rad - fRad)
                    +   175.0 * Math.Sin(a1Rad + fRad)
                    +   127.0 * Math.Sin(lpRad - mpRad)
                    -   115.0 * Math.Sin(lpRad + mpRad);

            // Geocentric longitude (deg), latitude (deg), distance (km).
            double lambda = Lp + sigmaL / 1000000.0;
            double beta   = sigmaB / 1000000.0;
            double delta  = 385000.56 + sigmaR / 1000.0;

            // Apparent place: add nutation in longitude.
            double dPsiDeg = MeeusUtility.NutationInLongitudeArcsec(T) / 3600.0;
            double appLon  = MeeusUtility.Norm360(lambda + dPsiDeg);

            return (appLon, beta, delta);
        }

        /// <summary>
        /// Apparent geocentric equatorial coordinates of the Moon at <paramref name="jd"/>
        /// (TT). Returns <c>(raDeg, decDeg, distanceKm)</c> with RA in <c>[0, 360)</c>,
        /// Dec in <c>[-90, +90]</c>, and distance from Earth's centre in km.
        /// </summary>
        public static (double RaDeg, double DecDeg, double DistanceKm) Apparent(double jd)
        {
            (double lonDeg, double latDeg, double distKm) = ApparentEcliptic(jd);

            double eps    = MeeusUtility.TrueObliquityDeg(MeeusUtility.T(jd));
            double lonRad = lonDeg * MeeusUtility.DegToRad;
            double latRad = latDeg * MeeusUtility.DegToRad;
            double epsRad = eps    * MeeusUtility.DegToRad;

            double sinLat = Math.Sin(latRad);
            double cosLat = Math.Cos(latRad);
            double sinLon = Math.Sin(lonRad);
            double cosLon = Math.Cos(lonRad);
            double sinEps = Math.Sin(epsRad);
            double cosEps = Math.Cos(epsRad);

            // Meeus 13.3 / 13.4. tan(beta) = sinLat/cosLat; cosLat may be zero only at the
            // ecliptic pole, which the Moon never reaches.
            double raRad  = Math.Atan2(sinLon * cosEps - (sinLat / cosLat) * sinEps, cosLon);
            double decRad = Math.Asin(sinLat * cosEps + cosLat * sinEps * sinLon);

            double raDeg  = MeeusUtility.Norm360(raRad * MeeusUtility.RadToDeg);
            double decDeg = decRad * MeeusUtility.RadToDeg;

            return (raDeg, decDeg, distKm);
        }

        /// <summary>
        /// Moon rise / set times on <paramref name="dateUtc"/>'s UTC calendar day, when the
        /// Moon's geocentric altitude crosses <paramref name="h0Deg"/>. The standard "moon
        /// rise/set" convention takes <paramref name="h0Deg"/> = 0.125&#176; -- approximately
        /// <c>0.7275 * pi_horizontal - 0.5667</c> for a typical moon distance, so the upper
        /// limb just clears the (refraction-adjusted) horizon when the centre is at the
        /// returned altitude.
        /// </summary>
        /// <remarks>
        /// Uses the same Meeus chapter 15 algorithm as <see cref="SunEphemeris.RiseSet"/> --
        /// three-day RA/Dec interpolation, iterative refinement -- but with the Moon's
        /// fast motion (~13&#176; / day in RA) means we run more iterations to converge.
        /// </remarks>
        public static (DateTime? Rise, DateTime? Set) RiseSet(
            DateTime dateUtc, double latDeg, double lonEastDeg, double h0Deg)
        {
            DateTime day0 = new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            double jd0 = Astronomy.Core.Time.JulianDate.FromUtc(day0);

            // GAST at 0h UT, in degrees. Same polynomial as SunEphemeris.RiseSet.
            double T0 = MeeusUtility.T(jd0);
            double theta0 = MeeusUtility.Norm360(
                100.46061837 + 36000.770053608 * T0 + 0.000387933 * T0 * T0 - T0 * T0 * T0 / 38710000.0);

            // Three days of moon RA/Dec for interpolation.
            (double ra0, double dec0, _) = Apparent(jd0 - 1.0);
            (double ra1, double dec1, _) = Apparent(jd0);
            (double ra2, double dec2, _) = Apparent(jd0 + 1.0);

            // Unwrap RA seam (moon moves ~13 deg/day so wraps are common).
            ra1 = RiseSetMath.Unwrap(ra0, ra1);
            ra2 = RiseSetMath.Unwrap(ra1, ra2);

            double L = -lonEastDeg;  // Meeus uses west-positive longitudes
            double phi = latDeg;

            double m0 = (ra1 + L - theta0) / 360.0;
            m0 = RiseSetMath.Frac(m0);

            double cosH0 = Math.FusedMultiplyAdd(
                              -Math.Sin(phi * MeeusUtility.DegToRad),
                               Math.Sin(dec1 * MeeusUtility.DegToRad),
                               Math.Sin(h0Deg * MeeusUtility.DegToRad))
                        / (Math.Cos(phi * MeeusUtility.DegToRad) * Math.Cos(dec1 * MeeusUtility.DegToRad));

            DateTime? rise = null;
            DateTime? set  = null;

            if (cosH0 >= -1.0 && cosH0 <= 1.0)
            {
                double H0deg = Math.Acos(cosH0) * MeeusUtility.RadToDeg;
                double m1 = RiseSetMath.Frac(m0 - H0deg / 360.0);
                double m2 = RiseSetMath.Frac(m0 + H0deg / 360.0);

                // 5 iterations -- the moon moves fast enough that 3 (the sun's count)
                // doesn't always converge to within 30 sec.
                for (int iter = 0; iter < 5; iter++)
                {
                    m1 = RefineMoonEvent(m1, theta0, L, phi, ra0, ra1, ra2, dec0, dec1, dec2, h0Deg);
                    m2 = RefineMoonEvent(m2, theta0, L, phi, ra0, ra1, ra2, dec0, dec1, dec2, h0Deg);
                }

                rise = day0.AddDays(m1);
                set  = day0.AddDays(m2);
            }

            return (rise, set);
        }

        // Mirror of SunEphemeris.RefineEvent for the Moon (drops isTransit; the
        // chapter-15 horizon refinement only runs for rise/set events).
        private static double RefineMoonEvent(
            double m, double theta0, double L, double phi,
            double ra0, double ra1, double ra2, double dec0, double dec1, double dec2,
            double h0Deg)
        {
            double thetaM = MeeusUtility.Norm360(theta0 + 360.985647 * m);

            double n = m;
            double raInterp  = RiseSetMath.Interp3(ra0,  ra1,  ra2,  n);
            double decInterp = RiseSetMath.Interp3(dec0, dec1, dec2, n);

            double H = MeeusUtility.NormPm180(thetaM - L - raInterp);

            double altRad = Math.Asin(Math.FusedMultiplyAdd(
                Math.Cos(phi * MeeusUtility.DegToRad) * Math.Cos(decInterp * MeeusUtility.DegToRad),
                Math.Cos(H * MeeusUtility.DegToRad),
                Math.Sin(phi * MeeusUtility.DegToRad) * Math.Sin(decInterp * MeeusUtility.DegToRad)));
            double altDeg = altRad * MeeusUtility.RadToDeg;

            double denom = 360.0 * Math.Cos(decInterp * MeeusUtility.DegToRad)
                                 * Math.Cos(phi      * MeeusUtility.DegToRad)
                                 * Math.Sin(H        * MeeusUtility.DegToRad);
            if (Math.Abs(denom) < 1e-12) return m;
            double dm = (altDeg - h0Deg) / denom;
            return RiseSetMath.Frac(m + dm);
        }


        /// <summary>
        /// Topocentric apparent equatorial coordinates of the Moon, applying parallax
        /// correction for an observer at geographic latitude / elevation. Distance is
        /// returned unchanged from geocentric (the parallax shift in distance is &lt; 1
        /// part in 10^4, well below tolerance).
        /// </summary>
        /// <param name="jd">Julian Date (TT).</param>
        /// <param name="lstDeg">Local apparent sidereal time at the observer, degrees.</param>
        /// <param name="latDeg">Geographic latitude (signed; positive north).</param>
        /// <param name="elevationM">Observer elevation above the geoid, meters.</param>
        public static (double RaDeg, double DecDeg, double DistanceKm) Topocentric(
            double jd, double lstDeg, double latDeg, double elevationM)
        {
            (double raDeg, double decDeg, double distKm) = Apparent(jd);

            // Geocentric latitude / radius factors -- Meeus 11.1 / 11.2 (pg. 82).
            double phiRad = latDeg * MeeusUtility.DegToRad;
            double u      = Math.Atan(0.99664719 * Math.Tan(phiRad));
            double elevFactor = elevationM / 6378140.0;
            double rhoSinPhi = Math.FusedMultiplyAdd(0.99664719, Math.Sin(u), elevFactor * Math.Sin(phiRad));
            double rhoCosPhi = Math.FusedMultiplyAdd(elevFactor, Math.Cos(phiRad), Math.Cos(u));

            // Equatorial horizontal parallax, sin(pi) = a / Delta -- Meeus 40.4 (pg. 279).
            double sinPi = EarthEquatorialRadiusKm / distKm;

            // Local hour angle of the Moon, deg.
            double Hdeg = MeeusUtility.NormPm180(lstDeg - raDeg);
            double Hrad = Hdeg * MeeusUtility.DegToRad;
            double decRad = decDeg * MeeusUtility.DegToRad;

            // Meeus 40.6-40.8: rotate from geocentric to topocentric via the (A, B, C)
            // direction-cosine triple.
            double cosDec = Math.Cos(decRad);
            double sinDec = Math.Sin(decRad);
            double sinH   = Math.Sin(Hrad);
            double cosH   = Math.Cos(Hrad);

            double A = cosDec * sinH;
            double B = Math.FusedMultiplyAdd(-rhoCosPhi, sinPi, cosDec * cosH);
            double C = Math.FusedMultiplyAdd(-rhoSinPhi, sinPi, sinDec);
            // q = sqrt(A^2 + B^2 + C^2) -- FMA the squared-sum chain.
            double q = Math.Sqrt(Math.FusedMultiplyAdd(A, A,
                                 Math.FusedMultiplyAdd(B, B, C * C)));

            double HpRad   = Math.Atan2(A, B);
            double decpRad = Math.Asin(C / q);

            // Recover topocentric RA from corrected hour angle: RA' = LST - H'.
            double raPrime  = MeeusUtility.Norm360(lstDeg - HpRad * MeeusUtility.RadToDeg);
            double decPrime = decpRad * MeeusUtility.RadToDeg;

            return (raPrime, decPrime, distKm);
        }
    }
}
