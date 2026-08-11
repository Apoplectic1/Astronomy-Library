using System;
using System.Collections.Generic;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Side-of-meridian geometry and flip timing for scheduling: signed hour angle,
    /// <see cref="MeridianSide"/> at an instant, transit enumeration, the flip moment
    /// inside a session, and splitting candidate windows into same-side pieces.
    /// Composition over <see cref="TransitTime"/> / <see cref="SiderealTime"/> — no new
    /// astronomy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Flip" here is purely temporal: the instant <c>transit + trackPastMeridian</c> at
    /// which a mount tracking past the meridian by that allowance must flip. Placement
    /// policy around the flip (finish-east / finish-west / absorb the flip cost) and the
    /// mapping to a mount's pier side belong to callers.
    /// </para>
    /// <para>
    /// <b>Transit precision:</b> the analytic LST inversion carries sub-millisecond
    /// floating-point jitter — the same transit derived from two different search seeds
    /// can differ by ~0.1 ms. Instants returned here are therefore not tick-exact
    /// across recomputations; <see cref="SplitAtFlip"/> absorbs this with a one-second
    /// split tolerance so re-splitting already-split windows (the replanning path)
    /// never produces jitter slivers.
    /// </para>
    /// </remarks>
    public static class Meridian
    {
        // Minimum piece a SplitAtFlip cut may produce. Far above the ~0.1 ms transit
        // recomputation jitter, far below any scheduling quantum.
        private static readonly TimeSpan SplitTolerance = TimeSpan.FromSeconds(1);

        // Seed offset when searching for the transit AFTER a found one. A one-tick step
        // stutters: LST jitter can re-find the same transit a few ticks forward, over
        // and over. One minute is far above jitter, far below the sidereal day to the
        // real next transit.
        private static readonly TimeSpan TransitAdvance = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The target's signed hour angle at <paramref name="utc"/>, in hours in
        /// <c>[-12, +12)</c>: negative before upper transit, zero at transit, positive
        /// after.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">The instant. Non-UTC kinds are converted per the Library boundary convention.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double HourAngleAt(Target target, Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            DateTime t = TimeKindGuard.AsUtc(utc);
            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double ha = MeeusUtility.Norm24(SiderealTime.Local(t, lonDegEast) - target.RightAscension);
            return ha >= 12.0 ? ha - 24.0 : ha;
        }

        /// <summary>
        /// Which side of the meridian the target is on at <paramref name="utc"/> —
        /// sky-side semantics per <see cref="MeridianSide"/>. The transit instant itself
        /// is <see cref="MeridianSide.West"/>, consistent with half-open
        /// <c>[transit, …)</c> post-flip intervals.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">The instant. Non-UTC kinds are converted per the Library boundary convention.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static MeridianSide SideAt(Target target, Location location, DateTime utc)
            => HourAngleAt(target, location, utc) < 0.0 ? MeridianSide.East : MeridianSide.West;

        /// <summary>
        /// Every upper transit inside the half-open <paramref name="window"/>, ascending.
        /// A window longer than one sidereal day (~23h56m) can contain more than one;
        /// a window containing none yields an empty list.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="window">Half-open UTC search window.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<DateTime> TransitsIn(
            Target target, Location location, UtcInterval window)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            var result = new List<DateTime>();
            DateTime t = TransitTime.UtcAtOrAfter(target, location, window.Start);
            while (t < window.End)
            {
                result.Add(t);
                t = TransitTime.UtcAtOrAfter(target, location, t + TransitAdvance);
            }
            return result;
        }

        /// <summary>
        /// The first flip instant (<c>transit + trackPastMeridian</c>) inside the half-open
        /// <paramref name="session"/>, or <see langword="null"/> when no flip lands inside
        /// it — the dossier's <c>MeridianFlipTime</c>.
        /// </summary>
        /// <remarks>
        /// The transit is searched from <c>session.Start - trackPastMeridian</c>, so a
        /// transit occurring <em>before</em> the session whose shifted flip instant falls
        /// inside the session is found — the case a naive "transit inside session" search
        /// misses. A negative allowance (mounts that stop tracking before the meridian) is
        /// honored arithmetically.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="session">Half-open UTC session interval.</param>
        /// <param name="trackPastMeridian">
        /// How long the mount tracks past the meridian before it must flip. May be negative.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime? FlipTimeIn(
            Target target, Location location, UtcInterval session, TimeSpan trackPastMeridian)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            // The first transit at/after (Start - allowance) shifts to a flip at/after
            // Start; either it lands before End (the answer) or no flip is in the session.
            DateTime transit = TransitTime.UtcAtOrAfter(target, location, session.Start - trackPastMeridian);
            DateTime flip = transit + trackPastMeridian;
            return flip < session.End ? flip : null;
        }

        /// <summary>
        /// Splits each window at every in-window flip instant
        /// (<c>transit + trackPastMeridian</c>), so each returned piece contains no
        /// interior flip — the same-side intervals an interval solver schedules on.
        /// Total covered time is preserved exactly. A flip within one second of a
        /// window boundary produces no split: the tolerance absorbs the transit
        /// recomputation jitter (see the class remarks), so re-splitting already-split
        /// windows is a no-op rather than a source of micro-slivers, and a sub-second
        /// same-side piece is meaningless for scheduling anyway.
        /// </summary>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="windows">
        /// Canonical interval list (ordered, disjoint, merged) — same contract as
        /// <see cref="Intervals"/>, validated with a throw on violation.
        /// </param>
        /// <param name="trackPastMeridian">
        /// How long the mount tracks past the meridian before it must flip. May be negative.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="target"/>, <paramref name="location"/>, or
        /// <paramref name="windows"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="windows"/> violates the canonical-list contract.
        /// </exception>
        public static IReadOnlyList<UtcInterval> SplitAtFlip(
            Target target, Location location,
            IReadOnlyList<UtcInterval> windows, TimeSpan trackPastMeridian)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            Intervals.RequireCanonical(windows, nameof(windows));

            var result = new List<UtcInterval>();
            foreach (UtcInterval win in windows)
            {
                DateTime cursor = win.Start;
                DateTime transit = TransitTime.UtcAtOrAfter(target, location, win.Start - trackPastMeridian);
                while (true)
                {
                    DateTime flip = transit + trackPastMeridian;
                    if (flip >= win.End) break;
                    // Split only when both resulting pieces clear the jitter tolerance;
                    // a flip at/near a boundary (recomputation jitter, or exactly on a
                    // previous split point) produces no sliver.
                    if (flip - cursor >= SplitTolerance && win.End - flip >= SplitTolerance)
                    {
                        result.Add(new UtcInterval(cursor, flip));
                        cursor = flip;
                    }
                    transit = TransitTime.UtcAtOrAfter(target, location, transit + TransitAdvance);
                }
                result.Add(new UtcInterval(cursor, win.End));
            }
            return result;
        }
    }
}
