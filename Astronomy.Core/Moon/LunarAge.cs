using System;
using Astronomy.Core.Time;

namespace Astronomy.Core.Moon
{
    /// <summary>
    /// Lunar age (days since the most recent new moon) computed from Julian Date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closed-form synodic-cycle estimate against a known new-moon reference epoch. The
    /// synodic period itself drifts ±~6 hours per cycle and the Lorentzian formula
    /// (<see cref="MoonAvoidance.LorentzianRequiredSep"/>) is insensitive to age error of
    /// a few hours, so this approximation is well within tolerance for moon-avoidance
    /// scheduling.
    /// </para>
    /// <para>
    /// For a higher-accuracy lunar phase, call
    /// <see cref="Astronomy.Core.Astrometry.AstroUtil.GetMoonIllumination"/>; this helper
    /// exists for Lorentzian moon-avoidance scheduling, where lunar age (rather than
    /// illuminated fraction) is the natural axis of the avoidance formula.
    /// </para>
    /// </remarks>
    public static class LunarAge
    {
        /// <summary>One synodic month, in days. Matches <see cref="MoonAvoidance.DaysInLunarCycle"/>.</summary>
        public const double SynodicMonthDays = MoonAvoidance.DaysInLunarCycle;

        /// <summary>
        /// Reference Julian Date of the new moon on 2000-01-06 18:14 UT.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Source: NASA lunar phase tables. The numeric value matches
        /// <c>JulianDate.FromUtc(new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc))</c>
        /// to ~1 second; using the exact JD here means <see cref="DaysAt"/> at the
        /// reference instant returns essentially zero (tiny float-precision residual)
        /// rather than a value that wraps to the synodic-period boundary via the modulo.
        /// </para>
        /// <para>
        /// Sub-second accuracy at this epoch is sufficient; the synodic period itself
        /// drifts by ~6 hours per cycle, far above any precision concern in this constant.
        /// </para>
        /// </remarks>
        public const double NewMoonReferenceJd = 2451550.2597222;

        /// <summary>
        /// Lunar age in days at the given UTC instant, in <c>[0, SynodicMonthDays)</c>.
        /// 0 = new moon; ~14.77 = full moon; → <see cref="SynodicMonthDays"/> = next new
        /// moon.
        /// </summary>
        /// <param name="utc">Instant to evaluate. Kind must be Utc.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="utc"/> is not <see cref="DateTimeKind.Utc"/>.
        /// </exception>
        public static double DaysAt(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("utc must be Kind=Utc", nameof(utc));

            double jd = JulianDate.FromUtc(utc);
            double age = (jd - NewMoonReferenceJd) % SynodicMonthDays;
            if (age < 0) age += SynodicMonthDays;
            return age;
        }
    }
}
