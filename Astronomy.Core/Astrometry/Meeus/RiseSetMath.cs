using System;

namespace Astronomy.Core.Astrometry.Meeus
{
    /// <summary>
    /// Shared helpers for the Meeus chapter-15 rise/set/transit refinement loop:
    /// three-point Lagrange interpolation through the per-day RA/Dec triple, an
    /// angle-seam unwrapper, and a fraction-of-day reducer. Used by both
    /// <see cref="SunEphemeris"/>'s RiseSet path and <see cref="MoonPosition"/>'s
    /// rise/set/transit refinement.
    /// </summary>
    /// <remarks>
    /// Reference: Jean Meeus, <em>Astronomical Algorithms</em>, 2nd ed. (1998),
    /// chapter 15 (rise/set/transit) and section 3.3 (three-point interpolation).
    /// </remarks>
    internal static class RiseSetMath
    {
        /// <summary>
        /// Three-point quadratic interpolation through <c>(-1, y0)</c>, <c>(0, y1)</c>,
        /// <c>(+1, y2)</c> at the abscissa <paramref name="n"/>. Meeus 3.3.
        /// </summary>
        public static double Interp3(double y0, double y1, double y2, double n)
        {
            double a = y1 - y0;
            double b = y2 - y1;
            double c = b - a;
            return y1 + 0.5 * n * (a + b + n * c);
        }

        /// <summary>
        /// Force <paramref name="cur"/> to be on the same side of the 360-degree seam
        /// as <paramref name="prev"/> (so a near-360 vs near-0 pair becomes monotonic
        /// for the three-point interpolation).
        /// </summary>
        public static double Unwrap(double prev, double cur)
        {
            if (cur - prev > 180.0) return cur - 360.0;
            if (prev - cur > 180.0) return cur + 360.0;
            return cur;
        }

        /// <summary>Reduce a fraction-of-day to <c>[0, 1)</c>.</summary>
        public static double Frac(double m) => m - Math.Floor(m);
    }
}
