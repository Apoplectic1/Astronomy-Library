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
        /// Honors the full <paramref name="horizon"/> profile via
        /// <see cref="VisibilityWindows.For"/>: <see cref="ScalarHorizonProfile"/> takes the
        /// closed-form path, while ridge / tree / building profiles route through scan-and-
        /// bisect refinement against the target's actual azimuth track.
        /// </para>
        /// <para>
        /// When <paramref name="profile"/> is non-<see langword="null"/> and enabled, the
        /// candidate windows are intersected with moon-clear sub-intervals (per the
        /// ACP/TS Lorentzian, sampled at 10-minute resolution) before placement. When
        /// <paramref name="profile"/> is <see langword="null"/> or
        /// <see cref="MoonAvoidanceProfile.Disabled"/>, the moon-aware path short-circuits
        /// and the result is byte-identical to the legacy moon-blind output.
        /// </para>
        /// <para>
        /// Window-boundary semantics: both dusk and dawn boundaries are inclusive. A
        /// target whose rising hour-angle coincides with dusk-exactly is included in
        /// the visibility window; same for setting at dawn-exactly. Internally
        /// computed via <c>Max(lstDusk, riseHA)</c> / <c>Min(lstDawn, setHA)</c> in
        /// <see cref="VisibilityWindows.For"/>.
        /// </para>
        /// </remarks>
        /// <returns>
        /// A <c>(Start, End, Quality)</c> tuple (times are <see cref="DateTimeKind.Utc"/>)
        /// or <see langword="null"/> if no window fits. Non-positive
        /// <paramref name="minDuration"/> also returns <see langword="null"/> -- a
        /// zero-or-negative session length is the degenerate "no fit possible"
        /// case, and consumers (chart UIs, schedulers) want a uniform null answer
        /// rather than translating an exception into the same null themselves.
        /// </returns>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Dusk/dawn pair (UTC).</param>
        /// <param name="horizon">Horizon profile. Non-null.</param>
        /// <param name="minDuration">Minimum acceptable session length. Non-positive returns null.</param>
        /// <param name="maxDuration">Maximum session length. Must be >= minDuration.</param>
        /// <param name="altitudeQuality">
        /// Optional altitude → quality function. <see langword="null"/> means
        /// <c>sin(altitude)</c>; the implementation then takes the
        /// <see cref="IntegratedQuality.SinAltitudeOverSession"/> closed-form fast
        /// path instead of the generic Simpson rule used for non-null lambdas (~25×
        /// faster per candidate window). Pass a non-null function only when you
        /// genuinely need a different quality model.
        /// </param>
        /// <param name="profile">
        /// Optional moon-avoidance profile. <see langword="null"/> or
        /// <see cref="MoonAvoidanceProfile.Disabled"/> takes the legacy moon-blind
        /// path; non-null + enabled intersects candidate windows with moon-clear
        /// sub-intervals before placement.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="minDuration"/> &gt; <paramref name="maxDuration"/>
        /// (the only genuinely impossible-to-satisfy combination -- non-positive
        /// minDuration is treated as a runtime "no fit" return, not a caller bug).
        /// </exception>
        public static (DateTime Start, DateTime End, double Quality)? For(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double>? altitudeQuality = null,
            MoonAvoidanceProfile? profile = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);
            if (minDuration <= TimeSpan.Zero) return null;
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

            return PlaceBestInternal(target, location, candidates, minDuration, maxDuration, altitudeQuality);
        }

        /// <summary>
        /// Resolves the candidate windows for a night without performing placement.
        /// Returns visibility windows intersected with moon-clear sub-intervals when
        /// <paramref name="profile"/> is non-<see langword="null"/> and enabled, or just
        /// visibility windows otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use this when you need the same candidate set for multiple placement strategies
        /// (e.g. <see cref="PlaceBest"/> for transit-centered-or-wall-pushed AND
        /// <see cref="PlaceCentered"/> for strict-centered) on the same night without
        /// resolving the moon mask twice. <see cref="For"/> calls this internally before
        /// placement; the public expose is for callers that want to share one candidate
        /// set across multiple placement attempts.
        /// </para>
        /// <para>
        /// Same moon-aware contract as <see cref="For"/>: a non-null + enabled
        /// <paramref name="profile"/> reads moon position via
        /// <see cref="Moon.MoonSeparation.ObserveAt"/> at 10-minute cadence inside each
        /// visibility window; profile-null and profile-Disabled short-circuit to the
        /// visibility result unchanged.
        /// </para>
        /// <para>
        /// Window-boundary semantics inherited from <see cref="VisibilityWindows.For"/>:
        /// both dusk and dawn boundaries are inclusive (<c>Max(lstDusk, riseHA)</c> /
        /// <c>Min(lstDawn, setHA)</c>). Moon-clear intersection narrows but does not
        /// shift these boundary semantics.
        /// </para>
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Dusk/dawn pair (UTC).</param>
        /// <param name="horizon">Horizon profile. Non-null.</param>
        /// <param name="profile">
        /// Optional moon-avoidance profile. When non-null and enabled, candidate windows
        /// are intersected with moon-clear sub-intervals.
        /// </param>
        /// <returns>
        /// Candidate windows (UTC), possibly empty. Iteration order matches
        /// <see cref="VisibilityWindows.For"/>'s output (left-to-right by start time).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<(DateTime Start, DateTime End)> ResolveCandidates(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            MoonAvoidanceProfile? profile = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);

            var visibility = VisibilityWindows.For(target, location, night, horizon);
            if (visibility.Count == 0) return visibility;
            if (profile == null || !profile.Enabled) return visibility;
            return MoonClearIntersect(target, location, visibility, profile);
        }

        /// <summary>
        /// Picks the highest-quality D-hour session across a caller-supplied list of
        /// candidate windows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per-window placement: if the transit occurs inside the window, prefer a
        /// transit-centered session (clamped to the window if it would spill past either
        /// edge); otherwise push the session against the edge closer to transit
        /// (altitude is monotone inside the window when transit is outside, so the far
        /// edge is the low-altitude end). Session length is the lesser of
        /// <paramref name="maxDuration"/> and the window length. Quality per candidate
        /// is computed via <see cref="IntegratedQuality.OverSession"/> using the
        /// caller-supplied <paramref name="altitudeQuality"/> function.
        /// </para>
        /// <para>
        /// This is the placement primitive that <see cref="For"/> calls internally after
        /// computing visibility windows (and optionally intersecting with moon-clear
        /// sub-intervals). Exposed publicly so callers that have already resolved their
        /// candidate windows externally -- e.g. by walking pre-cached moon samples and
        /// computing moon-clear sub-intervals up-front -- can skip <see cref="For"/>'s
        /// internal moon sweep and pass the windows in directly.
        /// </para>
        /// <para>
        /// Window-boundary semantics inherited from <see cref="VisibilityWindows.For"/>:
        /// both dusk and dawn boundaries are inclusive when callers compose their
        /// candidate list from visibility windows.
        /// </para>
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="windows">
        /// Pre-resolved candidate windows (visibility, optionally already intersected with
        /// any moon-mask the caller wants to apply). UTC instants. Iteration order doesn't
        /// matter; the highest-quality candidate wins.
        /// </param>
        /// <param name="minDuration">
        /// Minimum acceptable session length. Windows shorter than this are skipped.
        /// Must be positive.
        /// </param>
        /// <param name="maxDuration">
        /// Maximum session length. Sessions are capped to this even if the window is
        /// wider. Must be &gt;= <paramref name="minDuration"/>.
        /// </param>
        /// <param name="altitudeQuality">
        /// Optional altitude → quality function. <see langword="null"/> means
        /// <c>sin(altitude)</c>, dispatched through
        /// <see cref="IntegratedQuality.SinAltitudeOverSession"/> closed-form (~25×
        /// faster per candidate window than the generic Simpson rule). Pass a non-null
        /// function only when a different quality model is needed.
        /// </param>
        /// <returns>
        /// A <c>(Start, End, Quality)</c> tuple (UTC) for the best candidate, or
        /// <see langword="null"/> if no window accommodates <paramref name="minDuration"/>.
        /// Non-positive <paramref name="minDuration"/> also returns <see langword="null"/>
        /// (treated as the degenerate "no fit possible" case, not a caller bug; see
        /// <see cref="For"/> for the rationale).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="windows"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="minDuration"/> &gt; <paramref name="maxDuration"/>.
        /// </exception>
        public static (DateTime Start, DateTime End, double Quality)? PlaceBest(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> windows,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double>? altitudeQuality = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(windows);
            if (minDuration <= TimeSpan.Zero) return null;
            if (minDuration > maxDuration)
                throw new ArgumentException("minDuration must be <= maxDuration");

            return PlaceBestInternal(target, location, windows, minDuration, maxDuration, altitudeQuality);
        }

        /// <summary>
        /// Picks the strict transit-centered D-hour session that fits inside any of the
        /// caller-supplied candidate windows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// "Strict" means the session is exactly <c>[transit - duration/2, transit + duration/2]</c>
        /// -- no clamping, no wall-pushing. If that interval doesn't lie entirely inside
        /// any candidate window, returns <see langword="null"/>. This matches the chart's
        /// "Symmetric" semantics: the session must be symmetric about the meridian.
        /// </para>
        /// <para>
        /// For each window, the next transit at-or-after <c>window.Start</c> is consulted
        /// (via <see cref="TransitTime.UtcAtOrAfter"/>). If that transit lies inside the
        /// window AND the centered session fits, that placement is returned. Returns the
        /// first successful fit; under typical "one transit per night" stellar use the
        /// answer is unique anyway.
        /// </para>
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="windows">
        /// Pre-resolved candidate windows (visibility, optionally moon-mask-intersected).
        /// UTC instants.
        /// </param>
        /// <param name="duration">
        /// Strict-centered session length. Must be positive.
        /// </param>
        /// <returns>
        /// A <c>(Start, End)</c> tuple (UTC) for the centered session, or
        /// <see langword="null"/> if no window contains the centered placement.
        /// Non-positive <paramref name="duration"/> also returns <see langword="null"/>
        /// (degenerate "no fit possible" case; see <see cref="For"/>).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="windows"/> is <see langword="null"/>.
        /// </exception>
        public static (DateTime Start, DateTime End)? PlaceCentered(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> windows,
            TimeSpan duration)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(windows);
            if (duration <= TimeSpan.Zero) return null;

            long halfTicks = duration.Ticks / 2;
            TimeSpan halfDuration = TimeSpan.FromTicks(halfTicks);

            foreach (var win in windows)
            {
                DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, win.Start);
                if (transitUtc > win.End) continue;

                DateTime centeredStart = transitUtc - halfDuration;
                DateTime centeredEnd = centeredStart + duration;
                if (centeredStart >= win.Start && centeredEnd <= win.End)
                    return (centeredStart, centeredEnd);
            }

            return null;
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        // Existing transit-centered-or-wall-pushed placement, factored out so the legacy
        // and moon-aware code paths share it. Behavior is preserved exactly relative to
        // the pre-refactor body. Internal so the public PlaceBest can do its own
        // validation once and skip re-checking inside this loop.
        // altitudeQuality == null dispatches to IntegratedQuality.SinAltitudeOverSession
        // (closed-form, ~25× faster than the Simpson lambda path).
        private static (DateTime Start, DateTime End, double Quality)? PlaceBestInternal(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> windows,
            TimeSpan minDuration, TimeSpan maxDuration,
            Func<double, double>? altitudeQuality)
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
                    // Push against the edge with HIGHER altitude. When transit is outside
                    // the window, altitude is monotone within the window and the higher
                    // endpoint is the "transit-side" end -- but which transit (today's
                    // vs yesterday's vs tomorrow's) the window came from cannot be inferred
                    // from a single TransitTime.UtcAtOrAfter call: that always returns the
                    // next transit, which is wrong for descending-arc windows whose
                    // relevant transit was the PREVIOUS one. Comparing endpoint altitudes
                    // recovers the correct "transit-side" edge in all shifted-transit cases
                    // at the cost of two extra alt-az calls per candidate.
                    double altWinStart = AltAzCalculator.At(target, location, win.Start).Altitude;
                    double altWinEnd   = AltAzCalculator.At(target, location, win.End).Altitude;
                    sessionStart = altWinStart >= altWinEnd
                        ? win.Start
                        : win.End.AddHours(-sessionHrs);
                }

                DateTime sessionEnd = sessionStart.AddHours(sessionHrs);
                double quality = altitudeQuality is null
                    ? IntegratedQuality.SinAltitudeOverSession(
                        target, location, sessionStart, sessionDuration)
                    : IntegratedQuality.OverSession(
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
        internal static IReadOnlyList<(DateTime Start, DateTime End)> MoonClearIntersect(
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
