using System;
using System.Collections.Generic;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Time;

namespace Astronomy.Core.Sun
{
    /// <summary>
    /// Solar-tracking helpers: instantaneous angular rate, pre-computed Alt/Az schedule,
    /// and Kasten-Young air mass. Designed for gimbal-controller and PV-system callers
    /// that need feed-forward setpoints rather than just the current position.
    /// </summary>
    public static class SunTracking
    {
        /// <summary>
        /// Instantaneous angular rate of the Sun's apparent geometric position at
        /// <paramref name="utc"/>: <c>(altitude rate, azimuth rate)</c> in degrees per
        /// second. Suitable as a feed-forward term for a gimbal controller alongside the
        /// position setpoint from <see cref="SunPosition.AltAzAt"/>.
        /// </summary>
        /// <remarks>
        /// Implementation: central finite difference with dt = 1 second. Numeric error
        /// is ~10&#8315;&#8312; deg/sec, well below any plausible mechanical-tracker
        /// resolution. Azimuth is unwrapped across the 360-0 seam so a transit through
        /// north doesn't produce a spurious ~360/2 deg/sec spike.
        /// </remarks>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static (double AltDegPerSec, double AzDegPerSec) AngularRateAt(Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(location);

            DateTime t0 = TimeKindGuard.AsUtc(utc);
            AltAz pMinus = SunPosition.AltAzAt(location, t0.AddSeconds(-1));
            AltAz pPlus  = SunPosition.AltAzAt(location, t0.AddSeconds(+1));

            double dAlt = pPlus.Altitude - pMinus.Altitude;
            double dAz  = pPlus.Azimuth  - pMinus.Azimuth;

            // Unwrap az across the 360-0 seam. Sun shouldn't move more than ~0.01 deg in
            // 2 sec, so any |delta| > 180 is the seam-jump signature.
            if      (dAz >  180.0) dAz -= 360.0;
            else if (dAz < -180.0) dAz += 360.0;

            return (dAlt / 2.0, dAz / 2.0);
        }

        /// <summary>
        /// Sun Alt/Az schedule from <paramref name="startUtc"/> to <paramref name="endUtc"/>
        /// (inclusive of start, last sample at-or-before end) at uniform <paramref name="step"/>.
        /// Emits a list of <c>(Utc, Pos)</c> tuples suitable as a setpoint stream for a
        /// motion controller.
        /// </summary>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="startUtc">Schedule start instant. Must be UTC.</param>
        /// <param name="endUtc">Schedule end instant. Must be UTC and strictly after <paramref name="startUtc"/>.</param>
        /// <param name="step">Sample period. Must be at least 1 second.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="step"/> is less than 1 second, or
        /// <paramref name="endUtc"/> is not after <paramref name="startUtc"/>.
        /// </exception>
        public static IReadOnlyList<(DateTime Utc, AltAz Pos)> Schedule(
            Location location, DateTime startUtc, DateTime endUtc, TimeSpan step)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (step < TimeSpan.FromSeconds(1))
                throw new ArgumentOutOfRangeException(nameof(step), "step must be >= 1 second");

            DateTime s = TimeKindGuard.AsUtc(startUtc);
            DateTime e = TimeKindGuard.AsUtc(endUtc);
            if (e <= s)
                throw new ArgumentOutOfRangeException(nameof(endUtc), "endUtc must be strictly after startUtc");

            int capacity = (int)((e - s).TotalSeconds / step.TotalSeconds) + 1;
            var result = new List<(DateTime, AltAz)>(capacity);
            for (DateTime t = s; t <= e; t = t.Add(step))
                result.Add((t, SunPosition.AltAzAt(location, t)));
            return result;
        }

        /// <summary>
        /// Air mass at geometric altitude <paramref name="altitudeDeg"/> via the
        /// Kasten-Young 1989 formula. 1.0 at the zenith, ~38 at the horizon, and
        /// <see cref="double.PositiveInfinity"/> below the horizon. Used for clear-sky
        /// irradiance models and PV efficiency / extinction estimates where the Sun's
        /// altitude is the dominant input.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Formula: <c>1 / (sin(alt) + 0.50572 * (alt + 6.07995)^-1.6364)</c>, with alt in
        /// degrees. Domain (0, 90]; below the horizon returns
        /// <see cref="double.PositiveInfinity"/> (well-defined "no direct path" sentinel,
        /// distinct from <see cref="Astronomy.Core.Brightness.SkyBrightness"/>'s Pickering
        /// formula which clamps at a finite magic number).
        /// </para>
        /// </remarks>
        public static double AirMassKastenYoung(double altitudeDeg)
        {
            // Sentinel: well below horizon.
            if (altitudeDeg < 0.0) return double.PositiveInfinity;
            // Short-circuit to the textbook AM(zenith)=1 value. The Kasten-Young curve fit
            // has a small residual (~3e-4) at zenith; it's a curve-fit artifact, not a
            // meaningful air-mass deviation. Callers expect AM(90) == 1 exactly.
            if (altitudeDeg >= 90.0) return 1.0;
            double sinAlt = Math.Sin(altitudeDeg * MeeusUtility.DegToRad);
            return 1.0 / (sinAlt + 0.50572 * Math.Pow(altitudeDeg + 6.07995, -1.6364));
        }

    }
}
