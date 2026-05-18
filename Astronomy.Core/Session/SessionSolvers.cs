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
    /// Parameter-iteration solvers that fix all-but-one of the placement constraints and
    /// find the extremum of the remaining variable. Distinct from <see cref="BestSession"/>
    /// (placement at fixed parameters) and <see cref="SessionAltitude"/> (evaluation of a
    /// placed session) — this class is <em>search</em>: each method asks "given these
    /// constraints, what's the most permissive value of variable X that still yields a
    /// viable session?"
    /// </summary>
    /// <remarks>
    /// Two consumer patterns motivate the API today: end-user surfaces that answer
    /// "what's possible tonight?" questions (longest session at the user's horizon, lowest
    /// horizon needed for a desired session length), and scheduler relaxation paths that
    /// search adjacent parameter settings when the rigid constraints don't yield a
    /// solution.
    /// </remarks>
    public static class SessionSolvers
    {
        // Quality-function default sentinel removed: BestSession.PlaceBest now treats
        // a null altitudeQuality as "use sin(altitude)" and dispatches to
        // IntegratedQuality.SinAltitudeOverSession (~25× faster than the Simpson
        // path with the equivalent lambda). All downstream callers pass nullable
        // through directly.

        /// <summary>
        /// Returns the longest D-hour session that fits inside any of the night's viable
        /// candidate windows (visibility, optionally intersected with moon-clear sub-
        /// intervals), capped at <paramref name="cap"/> if supplied.
        /// </summary>
        /// <remarks>
        /// <para>
        /// "Longest D" is bounded by the longest candidate window's length: a window of
        /// length L can host at most an L-hour session. When <paramref name="cap"/> is
        /// non-null and shorter than L, the returned session is capped at cap and placed
        /// via <see cref="BestSession.PlaceBest"/>'s transit-centered-or-wall-pushed
        /// heuristic. When uncapped (or cap >= L), the returned session is the entire
        /// longest candidate window.
        /// </para>
        /// <para>
        /// This is the auto-resolve flavor: visibility windows and (optionally) moon-clear
        /// sub-intervals are computed internally. Callers that already have pre-resolved
        /// candidate windows (e.g. by walking a per-night cache) should call
        /// <see cref="LongestDurationIn"/> instead.
        /// </para>
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Night window (UTC dawn/dusk).</param>
        /// <param name="horizon">Horizon profile. Non-null.</param>
        /// <param name="cap">
        /// Optional upper bound on the returned duration. If supplied, must be positive.
        /// </param>
        /// <param name="profile">
        /// Optional moon-avoidance profile. When non-null and enabled, moon-clear sub-
        /// intervals are intersected with the visibility windows before searching.
        /// </param>
        /// <param name="altitudeQuality">
        /// Optional altitude → quality function used when capping requires PlaceBest to
        /// pick among candidates. Defaults to sin(altitude) when null.
        /// </param>
        /// <returns>
        /// A <c>(Start, End, Duration)</c> tuple (UTC) for the longest fittable session,
        /// or <see langword="null"/> if no viable window exists. Non-positive
        /// <paramref name="cap"/> also returns <see langword="null"/> (degenerate
        /// "no fit possible" case, not a caller bug).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDuration(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            TimeSpan? cap = null,
            MoonAvoidanceProfile? profile = null,
            Func<double, double>? altitudeQuality = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);
            if (cap.HasValue && cap.Value <= TimeSpan.Zero) return null;

            var candidates = BestSession.ResolveCandidates(target, location, night, horizon, profile);
            return LongestDurationInInternal(target, location, candidates, cap,
                altitudeQuality);
        }

        /// <summary>
        /// Pre-resolved-windows variant of <see cref="LongestDuration"/>. Caller supplies
        /// the candidate windows directly (visibility, optionally moon-mask-intersected
        /// externally), skipping the internal visibility + moon-sweep work. Mirrors the
        /// two-tier API pattern used by <see cref="BestSession.For"/> /
        /// <see cref="BestSession.PlaceBest"/>.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="candidates">
        /// Pre-resolved candidate windows (UTC). May be empty.
        /// </param>
        /// <param name="cap">Optional upper bound on the returned duration. If supplied, must be positive.</param>
        /// <param name="altitudeQuality">
        /// Optional altitude → quality function. Defaults to sin(altitude) when null.
        /// </param>
        /// <returns>
        /// A <c>(Start, End, Duration)</c> tuple (UTC) for the longest fittable session,
        /// or <see langword="null"/> when <paramref name="candidates"/> is empty,
        /// contains no positive-length window, or <paramref name="cap"/> is non-positive
        /// (the degenerate "no fit possible" case).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="candidates"/> is <see langword="null"/>.
        /// </exception>
        public static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDurationIn(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> candidates,
            TimeSpan? cap = null,
            Func<double, double>? altitudeQuality = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(candidates);
            if (cap.HasValue && cap.Value <= TimeSpan.Zero) return null;

            return LongestDurationInInternal(target, location, candidates, cap,
                altitudeQuality);
        }

        /// <summary>
        /// Returns the lowest scalar horizon (degrees) at which a <paramref name="duration"/>-long
        /// session still fits inside the night, optionally subject to a moon-avoidance
        /// profile. Bisects between <paramref name="minHorizonDeg"/> and the target's
        /// meridian altitude.
        /// </summary>
        /// <remarks>
        /// <para>
        /// "Lowest" here means the largest horizon angle at which the session is still
        /// achievable: above the returned angle the session no longer fits (visibility
        /// arc too short); at or below it, the session fits with progressively more
        /// slack. Returned horizon is approximate — bisection precision after
        /// <paramref name="maxIterations"/> iterations is roughly
        /// <c>(meridianAlt − minHorizonDeg) / 2^maxIterations</c>; default 20 iterations
        /// across a typical 60° bracket gives sub-arcminute precision.
        /// </para>
        /// <para>
        /// The session at the returned horizon is computed via
        /// <see cref="BestSession.PlaceBest"/> with the supplied
        /// <paramref name="altitudeQuality"/> (default: sin(altitude)).
        /// </para>
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Night window (UTC dawn/dusk).</param>
        /// <param name="duration">Required session length. Must be positive.</param>
        /// <param name="minHorizonDeg">
        /// Lower bound on the search (degrees). Defaults to 0.0. Must be in [-90, 90].
        /// </param>
        /// <param name="profile">
        /// Optional moon-avoidance profile. When non-null and enabled, candidate windows
        /// are intersected with moon-clear sub-intervals at each iteration's trial horizon.
        /// </param>
        /// <param name="altitudeQuality">
        /// Optional altitude → quality function for the final placement. Defaults to
        /// sin(altitude) when null.
        /// </param>
        /// <param name="maxIterations">
        /// Bisection iteration cap. Default 20. Must be positive.
        /// </param>
        /// <returns>
        /// A <c>(HorizonDeg, Start, End)</c> tuple (UTC times) for the largest horizon at
        /// which the session fits, or <see langword="null"/> when no horizon down to
        /// <paramref name="minHorizonDeg"/> can host the requested duration. Non-positive
        /// <paramref name="duration"/> also returns <see langword="null"/> (degenerate
        /// "no fit possible" case, not a caller bug).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="minHorizonDeg"/> is outside [-90, 90], or
        /// <paramref name="maxIterations"/> is non-positive.
        /// </exception>
        public static (double HorizonDeg, DateTime Start, DateTime End)? LowestHorizon(
            Target target, Location location, NightWindow night,
            TimeSpan duration,
            double minHorizonDeg = 0.0,
            MoonAvoidanceProfile? profile = null,
            Func<double, double>? altitudeQuality = null,
            int maxIterations = 20)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            if (duration <= TimeSpan.Zero) return null;
            if (minHorizonDeg < -90.0 || minHorizonDeg > 90.0)
                throw new ArgumentException("minHorizonDeg must be in [-90, 90]", nameof(minHorizonDeg));
            if (maxIterations <= 0)
                throw new ArgumentException("maxIterations must be positive", nameof(maxIterations));

            // Bracket: upper bound is the target's meridian altitude (no point trying H
            // above this -- the target never gets that high). If meridian itself is below
            // the floor, no horizon search can succeed.
            double latSigned = location.North ? location.Latitude : -location.Latitude;
            double decSigned = target.North ? target.Declination : -target.Declination;
            double meridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);
            if (meridianAlt <= minHorizonDeg) return null;

            // Quick rejection: if D doesn't fit even at the floor horizon, no answer.
            if (!FitsAt(target, location, night, minHorizonDeg, duration, profile))
                return null;

            // Bisection: find the largest H in [minHorizonDeg, meridianAlt] where D fits.
            double lo = minHorizonDeg;
            double hi = meridianAlt;
            for (int i = 0; i < maxIterations; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (FitsAt(target, location, night, mid, duration, profile))
                    lo = mid;
                else
                    hi = mid;
            }

            // Place the session at the converged horizon for the final return tuple.
            var horizonProfile = new ScalarHorizonProfile(lo);
            var candidates = BestSession.ResolveCandidates(target, location, night, horizonProfile, profile);
            var session = BestSession.PlaceBest(target, location, candidates, duration, duration, altitudeQuality);
            if (session == null) return null;
            return (lo, session.Value.Start, session.Value.End);
        }

        /// <summary>
        /// Returns the longest strict transit-centered session that fits inside any of
        /// the night's viable candidate windows, capped at <paramref name="cap"/> if
        /// supplied. Companion to <see cref="LongestDuration"/> for the Symmetric-curve
        /// semantics: session = <c>[transit - D/2, transit + D/2]</c> exactly, no wall-
        /// pushing, no clamp.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a candidate window containing transit T, the longest centered session is
        /// bounded by the closer wall: <c>D_max = 2 * min(T - winStart, winEnd - T)</c>.
        /// Candidates that don't contain transit are skipped (a strict-centered session
        /// can't span an above-horizon dip or moon-blocked gap on the way to transit).
        /// </para>
        /// <para>
        /// Auto-resolve flavor: visibility windows and (optionally) moon-clear sub-
        /// intervals are computed internally. Callers with pre-resolved candidates
        /// should use <see cref="LongestDurationCenteredIn"/>.
        /// </para>
        /// </remarks>
        /// <returns>
        /// A <c>(Start, End, Duration)</c> tuple (UTC) for the longest fittable centered
        /// session, or <see langword="null"/> if no candidate window contains transit
        /// with positive room on both sides. Non-positive <paramref name="cap"/>
        /// returns <see langword="null"/> (degenerate "no fit possible" case).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="horizon"/> is <see langword="null"/>.
        /// </exception>
        public static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDurationCentered(
            Target target, Location location, NightWindow night, IHorizonProfile horizon,
            TimeSpan? cap = null,
            MoonAvoidanceProfile? profile = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(horizon);
            if (cap.HasValue && cap.Value <= TimeSpan.Zero) return null;

            var candidates = BestSession.ResolveCandidates(target, location, night, horizon, profile);
            return LongestDurationCenteredInInternal(target, location, candidates, cap);
        }

        /// <summary>
        /// Pre-resolved-windows variant of <see cref="LongestDurationCentered"/>. Caller
        /// supplies the candidate windows directly.
        /// </summary>
        /// <remarks>
        /// No <c>altitudeQuality</c> parameter — strict-centered placement has no
        /// quality choice to make (each candidate window has at most one centered
        /// placement, and the longest D wins outright).
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="candidates"/> is <see langword="null"/>.
        /// </exception>
        /// <returns>
        /// A <c>(Start, End, Duration)</c> tuple (UTC), or <see langword="null"/> when
        /// no candidate fits. Non-positive <paramref name="cap"/> returns
        /// <see langword="null"/> (degenerate "no fit possible" case).
        /// </returns>
        public static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDurationCenteredIn(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> candidates,
            TimeSpan? cap = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(candidates);
            if (cap.HasValue && cap.Value <= TimeSpan.Zero) return null;

            return LongestDurationCenteredInInternal(target, location, candidates, cap);
        }

        /// <summary>
        /// Returns the lowest scalar horizon (degrees) at which a strict transit-centered
        /// <paramref name="duration"/>-long session still fits inside the night, optionally
        /// subject to a moon-avoidance profile. Companion to <see cref="LowestHorizon"/>
        /// for the Symmetric-curve semantics.
        /// </summary>
        /// <remarks>
        /// Same bisection shape as <see cref="LowestHorizon"/>. The "fits" predicate uses
        /// <see cref="BestSession.PlaceCentered"/>: a horizon allows the centered session
        /// iff some candidate window contains transit with at least <c>duration / 2</c>
        /// of room on each side.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="minHorizonDeg"/> is outside [-90, 90], or
        /// <paramref name="maxIterations"/> is non-positive.
        /// </exception>
        /// <returns>
        /// A <c>(HorizonDeg, Start, End)</c> tuple (UTC times), or <see langword="null"/>
        /// when no horizon down to <paramref name="minHorizonDeg"/> can host the centered
        /// session. Non-positive <paramref name="duration"/> returns <see langword="null"/>
        /// (degenerate "no fit possible" case, not a caller bug).
        /// </returns>
        public static (double HorizonDeg, DateTime Start, DateTime End)? LowestHorizonCentered(
            Target target, Location location, NightWindow night,
            TimeSpan duration,
            double minHorizonDeg = 0.0,
            MoonAvoidanceProfile? profile = null,
            int maxIterations = 20)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            if (duration <= TimeSpan.Zero) return null;
            if (minHorizonDeg < -90.0 || minHorizonDeg > 90.0)
                throw new ArgumentException("minHorizonDeg must be in [-90, 90]", nameof(minHorizonDeg));
            if (maxIterations <= 0)
                throw new ArgumentException("maxIterations must be positive", nameof(maxIterations));

            double latSigned = location.North ? location.Latitude : -location.Latitude;
            double decSigned = target.North ? target.Declination : -target.Declination;
            double meridianAlt = TargetGeometry.MeridianAltitude(latSigned, decSigned);
            if (meridianAlt <= minHorizonDeg) return null;

            if (!FitsCenteredAt(target, location, night, minHorizonDeg, duration, profile))
                return null;

            double lo = minHorizonDeg;
            double hi = meridianAlt;
            for (int i = 0; i < maxIterations; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (FitsCenteredAt(target, location, night, mid, duration, profile))
                    lo = mid;
                else
                    hi = mid;
            }

            var horizonProfile = new ScalarHorizonProfile(lo);
            var candidates = BestSession.ResolveCandidates(target, location, night, horizonProfile, profile);
            var session = BestSession.PlaceCentered(target, location, candidates, duration);
            if (session == null) return null;
            return (lo, session.Value.Start, session.Value.End);
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        // Internal LongestDurationIn worker shared by both public flavors after their
        // validation passes. Skips re-validation.
        private static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDurationInInternal(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> candidates,
            TimeSpan? cap,
            Func<double, double>? altitudeQuality)
        {
            TimeSpan longest = TimeSpan.Zero;
            foreach (var c in candidates)
            {
                TimeSpan span = c.End - c.Start;
                if (span > longest) longest = span;
            }
            if (longest <= TimeSpan.Zero) return null;

            TimeSpan finalDur = (cap.HasValue && longest > cap.Value) ? cap.Value : longest;
            var session = BestSession.PlaceBest(target, location, candidates, finalDur, finalDur, altitudeQuality);
            if (session == null) return null;
            return (session.Value.Start, session.Value.End, finalDur);
        }

        // Does a duration-long session fit at the given trial horizon?
        private static bool FitsAt(
            Target target, Location location, NightWindow night,
            double horizonDeg, TimeSpan duration,
            MoonAvoidanceProfile? profile)
        {
            var horizonProfile = new ScalarHorizonProfile(horizonDeg);
            var candidates = BestSession.ResolveCandidates(target, location, night, horizonProfile, profile);
            foreach (var c in candidates)
            {
                if ((c.End - c.Start) >= duration) return true;
            }
            return false;
        }

        // Internal LongestDurationCentered worker. Per-candidate: find the transit, skip
        // if it's not in this window, otherwise compute the symmetric room (twice the
        // closer wall distance) and track the largest across windows.
        private static (DateTime Start, DateTime End, TimeSpan Duration)? LongestDurationCenteredInInternal(
            Target target, Location location,
            IReadOnlyList<(DateTime Start, DateTime End)> candidates,
            TimeSpan? cap)
        {
            TimeSpan maxD = TimeSpan.Zero;
            DateTime bestTransit = default;
            foreach (var c in candidates)
            {
                DateTime transit = TransitTime.UtcAtOrAfter(target, location, c.Start);
                if (transit > c.End) continue;  // transit not in this window
                TimeSpan leftRoom  = transit - c.Start;
                TimeSpan rightRoom = c.End - transit;
                TimeSpan room = leftRoom < rightRoom ? leftRoom : rightRoom;
                TimeSpan dWindow = TimeSpan.FromTicks(room.Ticks * 2);
                if (dWindow > maxD)
                {
                    maxD = dWindow;
                    bestTransit = transit;
                }
            }
            if (maxD <= TimeSpan.Zero) return null;

            TimeSpan finalDur = (cap.HasValue && maxD > cap.Value) ? cap.Value : maxD;
            TimeSpan half = TimeSpan.FromTicks(finalDur.Ticks / 2);
            return (bestTransit - half, bestTransit + half, finalDur);
        }

        // Does a duration-long strict-centered session fit at the given trial horizon?
        // Mirror of FitsAt, but uses BestSession.PlaceCentered's return-non-null as the
        // predicate instead of a window-length scan.
        private static bool FitsCenteredAt(
            Target target, Location location, NightWindow night,
            double horizonDeg, TimeSpan duration,
            MoonAvoidanceProfile? profile)
        {
            var horizonProfile = new ScalarHorizonProfile(horizonDeg);
            var candidates = BestSession.ResolveCandidates(target, location, night, horizonProfile, profile);
            return BestSession.PlaceCentered(target, location, candidates, duration) != null;
        }
    }
}
