using System;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Coarse pre-filter answering the yes/no question "does this target ever clear the local
    /// horizon during this night?" -- intended as the first elimination pass ahead of more
    /// expensive per-target work like per-minute precompute, scoring, or interval scheduling.
    /// </summary>
    public static class CoarseVisibility
    {
        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> is above altitude
        /// 0&#176; at any moment during <paramref name="night"/>'s astronomical
        /// dusk-to-dawn window at <paramref name="location"/>. Takes no horizon profile:
        /// the question is "visible tonight?", not "clears an obstructed horizon?".
        /// </summary>
        /// <remarks>
        /// <para>
        /// Circumpolar targets (continuously above 0&#176; for the entire night) and
        /// transient-visibility targets (briefly above 0&#176; at any point between dusk and
        /// dawn) both return <see langword="true"/>. Targets that never rise at
        /// <paramref name="location"/> (declination too far south of the observer's
        /// latitude) return <see langword="false"/>, as do targets whose above-horizon arc
        /// falls entirely outside the night window.
        /// </para>
        /// <para>
        /// Closed-form; same underlying LST / hour-angle math as
        /// <see cref="IsEverAboveHorizon"/>, just with the altitude threshold fixed at
        /// 0&#176; so no <see cref="IHorizonProfile"/> is required at the call site.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsEverVisible(Target target, Location location, NightWindow night)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            if (!night.IsValid) return false;

            double latDeg    = location.North ?  location.Latitude  : -location.Latitude;
            double decDeg    = target.North   ?  target.Declination : -target.Declination;
            double lonDegE   = location.West  ? -location.Longitude :  location.Longitude;
            double raHours   = target.RightAscension;

            // Hour angle where the target sits on the 0 deg horizon. NaN => never rises
            // at this latitude; +Infinity => circumpolar above (always up).
            double haHorizon = TargetGeometry.HourAngleAtAltitude(latDeg, decDeg, 0.0);
            if (double.IsNaN(haHorizon)) return false;
            if (double.IsPositiveInfinity(haHorizon)) return true;

            // Convert the night window to sidereal hours (linearized so dawn > dusk).
            double lstDusk = SiderealTime.Local(night.AstronomicalDusk, lonDegE);
            double lstDawn = SiderealTime.Local(night.AstronomicalDawn, lonDegE);
            if (lstDawn < lstDusk) lstDawn += 24.0;

            // The target is above 0 deg when LST is within [RA - haHorizon, RA + haHorizon]
            // (mod 24). Check the three relevant wraps (k = -1, 0, +1) for intersection
            // with the night window.
            for (int k = -1; k <= 1; k++)
            {
                double center   = raHours + 24.0 * k;
                double arcStart = center - haHorizon;
                double arcEnd   = center + haHorizon;
                if (Math.Max(lstDusk, arcStart) < Math.Min(lstDawn, arcEnd)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> rises above
        /// <paramref name="horizon"/>'s <see cref="IHorizonProfile.MinAltitude"/> at any point
        /// between <paramref name="night"/>'s <see cref="NightWindow.AstronomicalDusk"/> and
        /// <see cref="NightWindow.AstronomicalDawn"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Conservative coarse pre-filter: tests against
        /// <see cref="IHorizonProfile.MinAltitude"/> (the lowest horizon altitude across all
        /// azimuths), so no target visible per a more precise per-azimuth horizon check is
        /// wrongly rejected. Any target that fails this test cannot pass a stricter one
        /// either.
        /// </para>
        /// <para>
        /// O(1) per call; closed-form via <see cref="VisibilityWindows.For"/>. Returns
        /// <see langword="false"/> for invalid (polar day / polar night) night windows.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsEverAboveHorizon(
            Target target, Location location, NightWindow night, IHorizonProfile horizon)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);

            if (!night.IsValid) return false;

            return VisibilityWindows.For(target, location, night, horizon).Count > 0;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> has a single contiguous
        /// window of at least <paramref name="minDuration"/> above <paramref name="horizon"/>'s
        /// <see cref="IHorizonProfile.MinAltitude"/> somewhere between
        /// <paramref name="night"/>'s <see cref="NightWindow.AstronomicalDusk"/> and
        /// <see cref="NightWindow.AstronomicalDawn"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Single-window semantics: a target whose total above-horizon time during the night
        /// is split across two shorter windows (rises, sets, rises again) is <b>not</b>
        /// considered visible even if the sum meets <paramref name="minDuration"/> -- a single
        /// imaging session can't span a horizon dip. Matches <see cref="BestSession"/>'s
        /// placement contract, which also filters by single-window length.
        /// </para>
        /// <para>
        /// Same cost class as <see cref="IsEverAboveHorizon"/>: one closed-form call to
        /// <see cref="VisibilityWindows.For"/> plus an O(windows) length scan (at most two
        /// windows). Returns <see langword="false"/> for invalid nights (polar day / polar
        /// night) and for targets that never clear the horizon during the night.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsAboveHorizonForAtLeast(
            Target target, Location location, NightWindow night,
            IHorizonProfile horizon, TimeSpan minDuration)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);

            if (!night.IsValid) return false;

            var windows = VisibilityWindows.For(target, location, night, horizon);
            foreach (var (start, end) in windows)
            {
                if (end - start >= minDuration) return true;
            }
            return false;
        }
    }
}
