using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Topocentric Sun-target angular separation and "sun-near" interval helpers.
    /// Mirrors <see cref="Astronomy.Core.Moon.MoonSeparation"/> with one important
    /// difference: the third method searches for intervals BELOW a threshold (sun
    /// approaching target -- eclipse / occultation prediction) rather than above
    /// (target clear of sun -- usually irrelevant since night already establishes
    /// sun-clear).
    /// </summary>
    public static class SunSeparation
    {
        /// <summary>
        /// Topocentric angular separation (degrees) between the target and the Sun at
        /// <paramref name="utc"/> as seen from <paramref name="location"/>. Range
        /// <c>[0, 180]</c>.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double DegreesAt(Target target, Location location, DateTime utc)
            => ObserveAt(target, location, utc).SeparationDeg;

        /// <summary>
        /// Topocentric Sun-target separation (degrees), Sun altitude (degrees), and Sun
        /// azimuth (degrees from North, clockwise) at <paramref name="utc"/>.
        /// </summary>
        /// <remarks>
        /// Single-pass: composes <see cref="AltAzCalculator.At"/> for the target with
        /// <see cref="SunPosition.AltAzAt"/> for the sun, then the spherical law of
        /// cosines for separation. Returning sun's alt/az too lets eclipse-probe callers
        /// reuse the same lookup that gave them the separation.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static (double SeparationDeg, double SunAltDeg, double SunAzDeg) ObserveAt(
            Target target, Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            AltAz tgt = AltAzCalculator.At(target, location, utc);
            AltAz sun = SunPosition.AltAzAt(location, utc);

            double t1 = tgt.Altitude * Math.PI / 180.0;
            double t2 = sun.Altitude * Math.PI / 180.0;
            double da = (tgt.Azimuth - sun.Azimuth) * Math.PI / 180.0;

            double cosSep = Math.Sin(t1) * Math.Sin(t2) + Math.Cos(t1) * Math.Cos(t2) * Math.Cos(da);
            if (cosSep >  1.0) cosSep =  1.0;
            if (cosSep < -1.0) cosSep = -1.0;
            double sepDeg = Math.Acos(cosSep) * 180.0 / Math.PI;

            return (sepDeg, sun.Altitude, sun.Azimuth);
        }

        /// <summary>
        /// Contiguous UTC intervals during <c>[startUtc, endUtc]</c> when the Sun-target
        /// separation is <em>below</em> <paramref name="maxSepDeg"/>. Useful for
        /// prospective eclipse / close-approach prediction.
        /// </summary>
        /// <remarks>
        /// Samples at 10-minute granularity then linearly interpolates threshold
        /// crossings between adjacent samples for ~1-minute boundary accuracy. Does not
        /// filter for horizon visibility -- callers wanting "below-threshold AND sun
        /// above horizon" should compose with <see cref="SunPosition.AltAzAt"/>
        /// themselves.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="startUtc">Search window start. Must be UTC.</param>
        /// <param name="endUtc">Search window end. Must be UTC and strictly after <paramref name="startUtc"/>.</param>
        /// <param name="maxSepDeg">Separation threshold (degrees). Returns intervals where separation &lt; this.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="endUtc"/> is not strictly after <paramref name="startUtc"/>.
        /// </exception>
        public static IReadOnlyList<UtcInterval> IntervalsBelowDeg(
            Target target, Location location, DateTime startUtc, DateTime endUtc, double maxSepDeg)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            DateTime s = TimeKindGuard.AsUtc(startUtc);
            DateTime e = TimeKindGuard.AsUtc(endUtc);
            if (e <= s)
                throw new ArgumentOutOfRangeException(nameof(endUtc), "endUtc must be strictly after startUtc");

            var result = new List<UtcInterval>();
            TimeSpan sampleSize = TimeSpan.FromMinutes(10);

            DateTime tPrev = s;
            double sepPrev = DegreesAt(target, location, tPrev);
            bool belowPrev = sepPrev < maxSepDeg;
            DateTime? currentStart = belowPrev ? (DateTime?)tPrev : null;

            DateTime tCur = s.Add(sampleSize);
            while (tCur <= e)
            {
                double sepCur = DegreesAt(target, location, tCur);
                bool belowCur = sepCur < maxSepDeg;

                if (belowPrev != belowCur)
                {
                    double frac = (maxSepDeg - sepPrev) / (sepCur - sepPrev);
                    DateTime crossing = tPrev.AddTicks((long)(frac * (tCur - tPrev).Ticks));
                    if (belowCur)
                        currentStart = crossing;
                    else if (currentStart.HasValue)
                    {
                        // Zero-length guard: an interpolated crossing exactly on the
                        // open start carries no below-threshold time.
                        if (crossing > currentStart.Value)
                            result.Add(new UtcInterval(currentStart.Value, crossing));
                        currentStart = null;
                    }
                }

                tPrev = tCur;
                sepPrev = sepCur;
                belowPrev = belowCur;
                tCur = tCur.Add(sampleSize);
            }

            if (currentStart.HasValue && e > currentStart.Value)
                result.Add(new UtcInterval(currentStart.Value, e));

            return result;
        }

    }
}
