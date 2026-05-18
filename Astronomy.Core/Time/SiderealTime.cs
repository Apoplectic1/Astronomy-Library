using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Greenwich and Local Sidereal Time derivations from a UTC instant.
    /// </summary>
    public static class SiderealTime
    {
        /// <summary>
        /// Sidereal hours accumulated per solar day -- the slope of the USNO GMST
        /// polynomial (<c>360.985647 deg/day / 15 deg/hr</c>). One solar day of UT
        /// elapses <c>24.06570982441908</c> sidereal hours of LST.
        /// </summary>
        /// <remarks>
        /// Used by Session helpers to advance LST linearly over a sample grid without
        /// re-calling <see cref="Local"/> per sample. Centralised here so a future
        /// refinement (e.g. <c>julianCenturyT</c>-dependent slope) updates one constant
        /// instead of five.
        /// </remarks>
        public const double SiderealHoursPerSolarDay = 24.06570982441908;

        /// <summary>
        /// Duration of one sidereal day expressed in solar hours, approximately
        /// 23.9344696. Equal to <c>24.0 * 24.0 / </c><see cref="SiderealHoursPerSolarDay"/>.
        /// </summary>
        public const double SiderealDayInSolarHours = 24.0 * 24.0 / SiderealHoursPerSolarDay;

        /// <summary>
        /// Greenwich Mean Sidereal Time in hours <c>[0, 24)</c> at the given Julian Date.
        /// USNO one-liner form: <c>GMST(0h UT) + 1.00273790935 * (elapsed UT hours)</c>.
        /// </summary>
        public static double Greenwich(double julianDate)
        {
            double D = julianDate - 2451545.0;
            double gmst = 18.697374558 + SiderealHoursPerSolarDay * D;
            return gmst - 24.0 * Math.Floor(gmst / 24.0);
        }

        /// <summary>
        /// Local Sidereal Time in hours <c>[0, 24)</c> at the given UTC instant and
        /// east-positive longitude in degrees.
        /// </summary>
        /// <param name="utc">Instant to evaluate. Must be UTC -- callers holding a
        /// local-frame instant should route through <c>TimeKindGuard.AsUtc</c> first.</param>
        /// <param name="longitudeDegEast">Longitude in decimal degrees, east-positive (so a
        /// western-hemisphere longitude is negative).</param>
        public static double Local(DateTime utc, double longitudeDegEast)
        {
            double jd = JulianDate.FromUtc(utc);
            double lst = Greenwich(jd) + longitudeDegEast / 15.0;
            return lst - 24.0 * Math.Floor(lst / 24.0);
        }
    }
}
