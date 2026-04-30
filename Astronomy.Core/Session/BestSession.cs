using System;
using System.Collections.Generic;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Finds the single D-hour session that maximizes an integrated-quality objective across
    /// the night's visibility windows.
    /// </summary>
    public static class BestSession
    {
        /// <summary>
        /// Returns the best D-hour session inside the night, or <see langword="null"/> if no
        /// visibility window can accommodate even <paramref name="minDuration"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Placement heuristic per window: if the transit occurs inside the window, prefer a
        /// transit-centered session; otherwise push the session against the wall of the
        /// window closer to transit. Session length is the lesser of
        /// <paramref name="maxDuration"/> and the window length. Quality is computed via
        /// <see cref="IntegratedQuality.OverSession"/> using the caller-supplied
        /// <paramref name="altitudeQuality"/> function.
        /// </para>
        /// <para>
        /// Currently uses the scalar-horizon <see cref="VisibilityWindows.For"/> fast-path;
        /// will pick up the azimuth-aware horizon-profile refinement automatically once
        /// <see cref="VisibilityWindows"/> gains it.
        /// </para>
        /// <para>
        /// When <paramref name="profile"/> is non-<see langword="null"/> and enabled, the
        /// candidate windows are intersected with moon-clear sub-intervals (per the
        /// ACP/TS Lorentzian, sampled at 10-minute resolution) before placement. When
        /// <paramref name="profile"/> is <see langword="null"/> or
        /// <see cref="MoonAvoidanceProfile.Disabled"/>, the moon-aware path short-circuits
        /// and the result is byte-identical to the legacy moon-blind output.
        /// </para>
        /// </remarks>
        /// <returns>
        /// A <c>(Start, End, Quality)</c> tuple (times are <see cref="DateTimeKind.Utc"/>)
        /// or <see langword="null"/> if no window fits.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>,
        /// <paramref name="horizon"/>, or <paramref name="altitudeQuality"/> is
        /// <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="minDuration"/> is non-positive, or
        /// <paramref name="minDuration"/> &gt; <paramref name="maxDuration"/>.
        /// </exception>
        public static (DateTime Start, DateTime End, double Quality)? For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double> altitudeQuality,
            MoonAvoidanceProfile profile = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));
            if (altitudeQuality == null) throw new ArgumentNullException(nameof(altitudeQuality));
            if (minDuration <= TimeSpan.Zero)
                throw new ArgumentException("minDuration must be positive", nameof(minDuration));
            if (minDuration > maxDuration)
                throw new ArgumentException("minDuration must be <= maxDuration");

            var visibility = VisibilityWindows.For(target, location, night, horizon);
            if (visibility.Count == 0) return null;

            // Moon-aware path: intersect each visibility window with moon-clear sub-
            // intervals. Profile-null and profile-Disabled short-circuit to the legacy
            // path -- the byte-identical guarantee for the v1 default.
            IReadOnlyList<(DateTime Start, DateTime End)> candidates = visibility;
            if (profile != null && profile.Enabled)
            {
                candidates = MoonClearIntersect(target, location, visibility, profile);
                if (candidates.Count == 0) return null;
            }

            return PlaceBest(target, location, candidates, minDuration, maxDuration, altitudeQuality);
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        // Existing transit-centered-or-wall-pushed placement, factored out so the legacy
        // and moon-aware code paths share it. Behavior is preserved exactly relative to
        // the pre-refactor body.
        private static (DateTime Start, DateTime End, double Quality)? PlaceBest(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> windows,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double> altitudeQuality)
        {
            double minHrs = minDuration.TotalHours;
            double maxHrs = maxDuration.TotalHours;

            (DateTime Start, DateTime End, double Quality)? best = null;

            foreach (var win in windows)
            {
                double winHrs = (win.End - win.Start).TotalHours;
                if (winHrs < minHrs) continue;

                double sessionHrs = Math.Min(winHrs, maxHrs);
                TimeSpan sessionDuration = TimeSpan.FromHours(sessionHrs);

                DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, win.Start);
                bool transitInWindow = transitUtc >= win.Start && transitUtc <= win.End;

                DateTime sessionStart;
                if (transitInWindow)
                {
                    // Try transit-centered, clamp to window.
                    sessionStart = transitUtc.AddHours(-sessionHrs / 2.0);
                    if (sessionStart < win.Start) sessionStart = win.Start;
                    if (sessionStart.AddHours(sessionHrs) > win.End)
                        sessionStart = win.End.AddHours(-sessionHrs);
                }
                else
                {
                    // Push against the edge closer to transit (alt is monotone inside the
                    // window when transit is outside, so the extreme end is the low-alt end).
                    sessionStart = transitUtc < win.Start
                        ? win.Start
                        : win.End.AddHours(-sessionHrs);
                }

                DateTime sessionEnd = sessionStart.AddHours(sessionHrs);
                double quality = IntegratedQuality.OverSession(
                    target, location, sessionStart, sessionDuration, altitudeQuality);

                if (best == null || quality > best.Value.Quality)
                    best = (sessionStart, sessionEnd, quality);
            }

            return best;
        }

        // Walks each visibility window at 10-minute resolution, samples (separation,
        // moonAlt, age), evaluates MoonAvoidance.IsRejected, and emits contiguous
        // (Start, End) sub-intervals where avoidance accepts. Boundary crossings are
        // located by linear interpolation on (actualSep - requiredSep), so the result is
        // accurate to about 1 minute regardless of how the threshold itself ramps with
        // moon altitude inside the relaxation zone.
        private static IReadOnlyList<(DateTime Start, DateTime End)> MoonClearIntersect(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> visibility,
            MoonAvoidanceProfile profile)
        {
            var result = new List<(DateTime Start, DateTime End)>();
            TimeSpan sampleSize = TimeSpan.FromMinutes(10);

            foreach (var win in visibility)
            {
                DateTime tPrev = win.Start;
                var (sepPrev, moonAltPrev, _) = MoonSeparation.ObserveAt(target, location, tPrev);
                double agePrev = LunarAge.DaysAt(tPrev);
                double reqPrev = MoonAvoidance.RequiredSepWithRelax(agePrev, moonAltPrev, profile);
                double deltaPrev = sepPrev - reqPrev;       // > 0 => clear, < 0 => rejected
                bool clearPrev = !(reqPrev > 0.0 && deltaPrev < 0.0);
                DateTime? clearStart = clearPrev ? (DateTime?)tPrev : null;

                DateTime tCur = win.Start.Add(sampleSize);
                while (tCur <= win.End)
                {
                    var (sepCur, moonAltCur, _) = MoonSeparation.ObserveAt(target, location, tCur);
                    double ageCur = LunarAge.DaysAt(tCur);
                    double reqCur = MoonAvoidance.RequiredSepWithRelax(ageCur, moonAltCur, profile);
                    double deltaCur = sepCur - reqCur;
                    bool clearCur = !(reqCur > 0.0 && deltaCur < 0.0);

                    if (clearPrev != clearCur)
                    {
                        // Linear interpolation on delta to locate the boundary. Falls back
                        // to the half-step midpoint if the deltas don't straddle zero
                        // (can happen when one side has reqPrev/Cur == 0 -- avoidance was
                        // off there -- and the other side flipped on with reqCur > sepCur).
                        DateTime crossing;
                        double denom = deltaPrev - deltaCur;
                        if (denom != 0.0 && Math.Sign(deltaPrev) != Math.Sign(deltaCur))
                        {
                            double frac = deltaPrev / denom;
                            crossing = tPrev.AddTicks((long)(frac * (tCur - tPrev).Ticks));
                        }
                        else
                        {
                            crossing = tPrev.AddTicks((tCur - tPrev).Ticks / 2);
                        }

                        if (clearCur)
                        {
                            // Was rejected, now clear: open a new sub-interval at the crossing.
                            clearStart = crossing;
                        }
                        else if (clearStart.HasValue)
                        {
                            // Was clear, now rejected: close the open sub-interval at the crossing.
                            result.Add((clearStart.Value, crossing));
                            clearStart = null;
                        }
                    }

                    tPrev = tCur;
                    sepPrev = sepCur;
                    moonAltPrev = moonAltCur;
                    agePrev = ageCur;
                    reqPrev = reqCur;
                    deltaPrev = deltaCur;
                    clearPrev = clearCur;
                    tCur = tCur.Add(sampleSize);
                }

                if (clearStart.HasValue) result.Add((clearStart.Value, win.End));
            }

            return result;
        }
    }
}
