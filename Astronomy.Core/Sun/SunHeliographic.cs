using System;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Time;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Heliographic coordinates of the Earth-pointing point on the Sun's disc, plus the
    /// Carrington rotation number. Used by solar imagers to derotate / co-register
    /// frames and to label features by Carrington longitude across days or years.
    /// </summary>
    /// <remarks>
    /// All math from Meeus AA chapter 29. Accuracy is well below 0.05&#176; on (P, B0, L0)
    /// over historical and near-future dates -- below the resolution of typical amateur
    /// solar telescopes.
    /// </remarks>
    public static class SunHeliographic
    {
        /// <summary>
        /// Carrington epoch (rotation 1 start): JD 2398167.5 = 1853 November 9.5 UT, per
        /// the NOAA SWPC reference formula <c>N = 1 + (JD - 2398167.5) / 27.2753</c>.
        /// Note: pre-1899-12-30 dates incur a ~1-2 day error in this function because
        /// .NET's <see cref="DateTime.ToOADate"/> -- and thus
        /// <see cref="Astronomy.Core.Time.JulianDate.FromUtc"/> -- uses an OLE-Automation-
        /// Date convention that handles negative offsets idiosyncratically. The constants
        /// are correct; only old-date inputs are unreliable.
        /// </summary>
        private const double CarringtonEpochJd = 2398167.5;

        /// <summary>
        /// Mean synodic period of solar rotation as seen from Earth (Carrington's adopted
        /// mean rotation period). Used to convert JD difference into rotation count.
        /// </summary>
        private const double CarringtonRotationDays = 27.2753;

        /// <summary>
        /// Heliographic disk-centre triple <c>(P, B0, L0)</c> in degrees at
        /// <paramref name="utc"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>P</b>: position angle (eastward from celestial north) of the solar rotation
        /// axis, ranging roughly &#177;26.3&#176;. Used for image derotation.
        /// </para>
        /// <para>
        /// <b>B0</b>: heliographic latitude of the disc centre (the sub-Earth point),
        /// ranging roughly &#177;7.25&#176;. Tells you which hemisphere is tipped toward
        /// Earth.
        /// </para>
        /// <para>
        /// <b>L0</b>: heliographic longitude of the disc centre in the Carrington system,
        /// in <c>[0, 360)</c>. Decreases with time at ~13.2&#176;/day -- the Sun
        /// rotates eastward (in heliographic longitude) faster than Earth orbits.
        /// </para>
        /// </remarks>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        public static (double PDeg, double B0Deg, double L0Deg) DiskCenterAt(DateTime utc)
        {
            double jd = JulianDate.FromUtc(TimeKindGuard.AsUtc(utc));
            double T = MeeusUtility.T(jd);

            // Apparent geocentric (RA, Dec) of the Sun -- already includes aberration +
            // nutation in longitude. We re-derive ecliptic longitude from that, then run
            // Meeus 29 against it.
            (double raDeg, double decDeg, _) = SunEphemeris.Apparent(jd);

            // Apparent obliquity (must match the value SunEphemeris.Apparent used).
            double Omega = 125.04 - 1934.136 * T;
            double eps0 = MeeusUtility.MeanObliquityDeg(T);
            double epsDeg = eps0 + 0.00256 * Math.Cos(Omega * MeeusUtility.DegToRad);
            double epsRad = epsDeg * MeeusUtility.DegToRad;

            // Equatorial -> ecliptic longitude (sun's beta is ~0, max ~1.2", ignored).
            double aRad = raDeg * MeeusUtility.DegToRad;
            double dRad = decDeg * MeeusUtility.DegToRad;
            double lambdaRad = Math.Atan2(
                Math.Sin(aRad) * Math.Cos(epsRad) + Math.Tan(dRad) * Math.Sin(epsRad),
                Math.Cos(aRad));

            // Meeus 29.2 / 29.4: theta is the rotation phase referenced to the Carrington
            // epoch; I and K are the inclination and ascending-node longitude of the
            // solar equator.
            double theta = (jd - 2398220.0) * 360.0 / 25.38;
            theta = MeeusUtility.Norm360(theta);

            const double IDeg = 7.25;
            double KDeg = 73.6667 + 1.3958333 * (jd - 2396758.0) / 36525.0;

            double IRad = IDeg * MeeusUtility.DegToRad;
            double lambdaMinusKRad = (lambdaRad * MeeusUtility.RadToDeg - KDeg) * MeeusUtility.DegToRad;

            // P (position angle of rotation axis): Meeus 29.5.
            double xRad = Math.Atan(-Math.Cos(lambdaRad) * Math.Tan(epsRad));
            double yRad = Math.Atan(-Math.Cos(lambdaMinusKRad) * Math.Tan(IRad));
            double PDeg = (xRad + yRad) * MeeusUtility.RadToDeg;

            // B0 (heliographic latitude of disc centre): Meeus 29.6.
            double B0Rad = Math.Asin(Math.Sin(lambdaMinusKRad) * Math.Sin(IRad));
            double B0Deg = B0Rad * MeeusUtility.RadToDeg;

            // L0 (heliographic longitude of disc centre): Meeus 29.7-29.8.
            double etaRad = Math.Atan2(
                -Math.Sin(lambdaMinusKRad) * Math.Cos(IRad),
                -Math.Cos(lambdaMinusKRad));
            double etaDeg = etaRad * MeeusUtility.RadToDeg;
            double L0Deg = MeeusUtility.Norm360(etaDeg - theta);

            return (PDeg, B0Deg, L0Deg);
        }

        /// <summary>
        /// Carrington rotation number (fractional) at <paramref name="utc"/>. Integer
        /// part is the rotation count starting from 1 at the Carrington epoch
        /// (1853-11-09 ~21:36 UT, JD 2398167.4); fractional part interpolates linearly
        /// over the ~27.275-day mean synodic rotation. Use <see cref="Math.Floor(double)"/>
        /// for the canonical integer rotation index.
        /// </summary>
        public static double CarringtonRotationNumberAt(DateTime utc)
        {
            double jd = JulianDate.FromUtc(TimeKindGuard.AsUtc(utc));
            return 1.0 + (jd - CarringtonEpochJd) / CarringtonRotationDays;
        }

    }
}
