using System;
using System.Collections.Generic;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Intersects a stellar target's above-horizon arcs with the night window, returning the
    /// contiguous UTC intervals where the target is both above the horizon profile and
    /// between astronomical dusk and dawn.
    /// </summary>
    public static class VisibilityWindows
    {
        // Outer-scan resolution for profile refinement. 1-minute sampling is fine enough to
        // catch dips below ridge / building features (azimuth changes < 1 deg / min for
        // any practical target) while keeping the sample count for a full night under 1000.
        private static readonly TimeSpan ProfileScanStep = TimeSpan.FromMinutes(1.0);

        /// <summary>
        /// Returns 0-2 contiguous UTC intervals where the target is visible during the given
        /// night. Zero windows means never above horizon during the night; one is the usual
        /// case; two arises when the target rises, sets, and rises again before dawn (shifted
        /// transits <c>k = -1</c> and <c>k = +1</c>) or when an azimuth-aware horizon profile
        /// (ridge / tree / building) cuts across the target's arc.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ScalarHorizonProfile"/> takes the closed-form analytic path. Other
        /// profiles use the scalar <see cref="IHorizonProfile.MinAltitude"/> arc as an outer
        /// envelope and scan it at 1-minute resolution, bisecting at each crossing of the
        /// (target altitude vs profile altitude at target azimuth) curve to sub-second
        /// precision. The scan is bounded by the scalar windows so polar-day / never-rises
        /// cases short-circuit before any per-sample work runs.
        /// </para>
        /// <para>
        /// Window-boundary semantics: both dusk and dawn boundaries are inclusive
        /// (<c>Max(lstDusk, riseHA)</c> / <c>Min(lstDawn, setHA)</c>).
        /// </para>
        /// </remarks>
        /// <returns>
        /// Intervals as <c>(Start, End)</c> tuples, both <see cref="DateTimeKind.Utc"/>.
        /// Empty list if the target never clears the horizon, never rises, or if the night
        /// window is invalid (polar day / polar night).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<(DateTime Start, DateTime End)> For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);

            if (!night.IsValid) return new List<(DateTime, DateTime)>();

            var scalarWindows = ForScalar(target, location, night, horizon.MinAltitude);
            if (horizon is ScalarHorizonProfile) return scalarWindows;
            if (scalarWindows.Count == 0)        return scalarWindows;

            var refined = new List<(DateTime Start, DateTime End)>();
            foreach (var outer in scalarWindows)
            {
                RefineAgainstProfile(target, location, horizon, outer.Start, outer.End, refined);
            }
            return refined;
        }

        // Closed-form analytic path: target above scalar horizonDeg ∩ [dusk, dawn].
        private static List<(DateTime Start, DateTime End)> ForScalar(
            Target target, Location location, NightWindow night, double horizonDeg)
        {
            var result = new List<(DateTime Start, DateTime End)>();

            var (latDeg, lonDegEast) = location.AsSignedDegrees();
            var (decDeg, raHours) = target.AsSignedRaDec();

            double haHorizon = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, horizonDeg);
            if (double.IsNaN(haHorizon)) return result; // never reaches horizon

            // NightWindow exposes AstronomicalDusk / AstronomicalDawn as Kind=Utc. No
            // conversion needed here -- see NightCalculator for the offset-recovery rationale.
            DateTime duskUtc = night.AstronomicalDusk;
            DateTime dawnUtc = night.AstronomicalDawn;

            double lstDusk = SiderealTime.Local(duskUtc, lonDegEast);
            double lstDawn = SiderealTime.Local(dawnUtc, lonDegEast);
            if (lstDawn < lstDusk) lstDawn += 24.0;

            if (double.IsPositiveInfinity(haHorizon))
            {
                // Circumpolar above horizon: full night is one visibility window.
                result.Add((duskUtc, dawnUtc));
                return result;
            }

            double solarPerSidereal = 24.0 / SiderealTime.SiderealHoursPerSolarDay;
            for (int k = -1; k <= 1; k++)
            {
                double center  = raHours + 24.0 * k;
                double ahStart = center - haHorizon;
                double ahEnd   = center + haHorizon;
                double s = Math.Max(lstDusk, ahStart);
                double e = Math.Min(lstDawn, ahEnd);
                if (s >= e) continue;

                DateTime startUtc = duskUtc.AddHours((s - lstDusk) * solarPerSidereal);
                DateTime endUtc   = duskUtc.AddHours((e - lstDusk) * solarPerSidereal);
                result.Add((startUtc, endUtc));
            }

            return result;
        }

        // Walk the (outerLo, outerHi) MinAltitude window in 1-minute steps, bisect each
        // (target alt vs profile alt at target az) crossing to sub-second precision, and
        // append every resulting "above profile" sub-interval to result.
        private static void RefineAgainstProfile(
            Target target, Location location, IHorizonProfile horizon,
            DateTime outerLo, DateTime outerHi,
            List<(DateTime Start, DateTime End)> result)
        {
            bool prevAbove = IsAboveProfile(target, location, horizon, outerLo);
            DateTime subStart = prevAbove ? outerLo : default;

            DateTime tPrev = outerLo;
            while (tPrev < outerHi)
            {
                DateTime tNext = tPrev + ProfileScanStep;
                if (tNext > outerHi) tNext = outerHi;

                bool nextAbove = IsAboveProfile(target, location, horizon, tNext);
                if (nextAbove != prevAbove)
                {
                    DateTime crossing = BisectCrossing(target, location, horizon, tPrev, tNext);
                    if (nextAbove) subStart = crossing;
                    else           result.Add((subStart, crossing));
                    prevAbove = nextAbove;
                }
                tPrev = tNext;
            }

            if (prevAbove) result.Add((subStart, outerHi));
        }

        private static bool IsAboveProfile(
            Target target, Location location, IHorizonProfile horizon, DateTime utc)
        {
            AltAz altaz = AltAzCalculator.At(target, location, utc);
            return altaz.Altitude > horizon.AltitudeAt(altaz.Azimuth);
        }

        // Bisection refines a bracket (lo, hi) where one endpoint is above-profile and the
        // other below to sub-second precision. 30 iterations halve a 1-minute span ~10^-9
        // times -- well below DateTime tick resolution.
        private static DateTime BisectCrossing(
            Target target, Location location, IHorizonProfile horizon,
            DateTime lo, DateTime hi)
        {
            bool loAbove = IsAboveProfile(target, location, horizon, lo);
            for (int i = 0; i < 30; i++)
            {
                DateTime mid = new DateTime((lo.Ticks + hi.Ticks) / 2, DateTimeKind.Utc);
                bool midAbove = IsAboveProfile(target, location, horizon, mid);
                if (midAbove == loAbove) lo = mid;
                else                     hi = mid;
            }
            return new DateTime((lo.Ticks + hi.Ticks) / 2, DateTimeKind.Utc);
        }
    }
}
