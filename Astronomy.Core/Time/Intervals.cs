using System;
using System.Collections.Generic;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Set operations over canonical <see cref="UtcInterval"/> lists: intersect, union,
    /// subtract, and clip. The composition layer between the Library's interval
    /// producers (visibility windows, moon-separation intervals, sun-separation
    /// intervals) and interval-consuming solvers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Canonical-list contract:</b> every input list must be ordered ascending by
    /// <see cref="UtcInterval.Start"/> and pairwise disjoint. Elements MAY touch
    /// end-to-start: adjacent-but-distinct intervals are a legitimate currency (e.g.
    /// same-side pieces split at a meridian flip — see
    /// <c>Session.Meridian.SplitAtFlip</c>), so touching is not treated as an
    /// un-coalesced bug. <see cref="Union"/>'s <em>output</em> additionally coalesces
    /// overlapping and touching runs. Every operation validates its inputs and throws
    /// on violation rather than repairing them: an unordered or overlapping list means
    /// a producer or intermediate step has a bug, and a silently "fixed" wrong input is
    /// far more expensive to find than an exception. All outputs satisfy the same
    /// contract, so results compose without re-validation cost concerns (n is
    /// single-digit per night for every current producer).
    /// </para>
    /// <para>
    /// Subtraction is the generic form of the forbidden-band clip (cf. Target
    /// Scheduler's <c>MaximumAltitudeClipper</c>): a span subtracted from a window
    /// yields 0–2 intervals, covering all six relative positions — disjoint before /
    /// after, head clip, tail clip, swallowed (empty), and strictly-inside (split) —
    /// by construction rather than by case enumeration.
    /// </para>
    /// </remarks>
    public static class Intervals
    {
        /// <summary>
        /// Instants contained in both <paramref name="a"/> and <paramref name="b"/>.
        /// Touching boundaries contribute nothing (half-open semantics).
        /// </summary>
        /// <exception cref="ArgumentNullException">Either list is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Either list violates the canonical-list contract.</exception>
        public static IReadOnlyList<UtcInterval> Intersect(
            IReadOnlyList<UtcInterval> a, IReadOnlyList<UtcInterval> b)
        {
            RequireCanonical(a, nameof(a));
            RequireCanonical(b, nameof(b));

            var result = new List<UtcInterval>();
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                DateTime s = a[i].Start > b[j].Start ? a[i].Start : b[j].Start;
                DateTime e = a[i].End < b[j].End ? a[i].End : b[j].End;
                if (s < e) result.Add(new UtcInterval(s, e));

                // Advance whichever interval ends first; on equal ends advancing both
                // would also be correct, advancing one is simply fewer branches.
                if (a[i].End <= b[j].End) i++;
                else j++;
            }
            return result;
        }

        /// <summary>
        /// Instants contained in <paramref name="a"/> or <paramref name="b"/>.
        /// Overlapping and touching intervals coalesce into single elements.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either list is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Either list violates the canonical-list contract.</exception>
        public static IReadOnlyList<UtcInterval> Union(
            IReadOnlyList<UtcInterval> a, IReadOnlyList<UtcInterval> b)
        {
            RequireCanonical(a, nameof(a));
            RequireCanonical(b, nameof(b));

            var result = new List<UtcInterval>();
            int i = 0, j = 0;
            bool open = false;
            DateTime curStart = default, curEnd = default;

            while (i < a.Count || j < b.Count)
            {
                // Two-pointer merge by Start across both sorted lists.
                UtcInterval next =
                    j >= b.Count ? a[i++] :
                    i >= a.Count ? b[j++] :
                    a[i].Start <= b[j].Start ? a[i++] : b[j++];

                if (!open)
                {
                    (curStart, curEnd, open) = (next.Start, next.End, true);
                }
                else if (next.Start <= curEnd)
                {
                    // Overlapping or touching: extend the open run.
                    if (next.End > curEnd) curEnd = next.End;
                }
                else
                {
                    result.Add(new UtcInterval(curStart, curEnd));
                    (curStart, curEnd) = (next.Start, next.End);
                }
            }

            if (open) result.Add(new UtcInterval(curStart, curEnd));
            return result;
        }

        /// <summary>
        /// Instants contained in <paramref name="a"/> but not in <paramref name="b"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either list is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Either list violates the canonical-list contract.</exception>
        public static IReadOnlyList<UtcInterval> Subtract(
            IReadOnlyList<UtcInterval> a, IReadOnlyList<UtcInterval> b)
        {
            RequireCanonical(a, nameof(a));
            RequireCanonical(b, nameof(b));

            var result = new List<UtcInterval>();
            int j = 0;
            foreach (UtcInterval window in a)
            {
                // Skip subtrahends entirely before this window. j never rewinds:
                // windows are ascending, so a subtrahend ending before this window's
                // start is before every later window too.
                while (j < b.Count && b[j].End <= window.Start) j++;

                DateTime cursor = window.Start;
                for (int k = j; k < b.Count && b[k].Start < window.End; k++)
                {
                    if (b[k].Start > cursor) result.Add(new UtcInterval(cursor, b[k].Start));
                    if (b[k].End > cursor) cursor = b[k].End;
                    if (cursor >= window.End) break;
                }

                if (cursor < window.End) result.Add(new UtcInterval(cursor, window.End));
            }
            return result;
        }

        /// <summary>
        /// The 0–2 intervals of <paramref name="window"/> not covered by
        /// <paramref name="span"/> — the forbidden-band clip: empty when the span
        /// swallows the window, two intervals when the span lies strictly inside it,
        /// one clipped interval otherwise (the window itself when they are disjoint).
        /// </summary>
        public static IReadOnlyList<UtcInterval> Subtract(UtcInterval window, UtcInterval span)
        {
            var result = new List<UtcInterval>(2);
            if (span.Start > window.Start)
            {
                DateTime e = span.Start < window.End ? span.Start : window.End;
                result.Add(new UtcInterval(window.Start, e));
            }
            if (span.End < window.End)
            {
                DateTime s = span.End > window.Start ? span.End : window.Start;
                result.Add(new UtcInterval(s, window.End));
            }
            return result;
        }

        /// <summary>
        /// Trims <paramref name="list"/> to <paramref name="bound"/> — equivalent to
        /// intersecting the list with the single bounding interval.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="list"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="list"/> violates the canonical-list contract.</exception>
        public static IReadOnlyList<UtcInterval> Clip(
            IReadOnlyList<UtcInterval> list, UtcInterval bound)
        {
            RequireCanonical(list, nameof(list));

            var result = new List<UtcInterval>();
            foreach (UtcInterval iv in list)
            {
                if (iv.End <= bound.Start) continue;
                if (iv.Start >= bound.End) break;
                DateTime s = iv.Start > bound.Start ? iv.Start : bound.Start;
                DateTime e = iv.End < bound.End ? iv.End : bound.End;
                result.Add(new UtcInterval(s, e));
            }
            return result;
        }

        // The canonical-list contract gate. O(n) per call -- trivial at the Library's
        // per-night interval counts, and the throw converts a latent producer bug into
        // a loud failure at the first composition instead of a silently wrong plan.
        // Internal so sibling interval consumers (Session.Meridian.SplitAtFlip) validate
        // through the same contract.
        internal static void RequireCanonical(IReadOnlyList<UtcInterval> list, string paramName)
        {
            ArgumentNullException.ThrowIfNull(list, paramName);
            for (int i = 0; i < list.Count; i++)
            {
                // default(UtcInterval) bypasses the ctor; its endpoints are
                // default(DateTime) (Kind=Unspecified), so this also rejects it.
                if (list[i].Start.Kind != DateTimeKind.Utc || list[i].End <= list[i].Start)
                {
                    throw new ArgumentException(
                        $"Element {i} is not a valid UtcInterval ({list[i]}). " +
                        "default(UtcInterval) bypasses construction and is not usable.",
                        paramName);
                }

                if (i > 0 && list[i].Start < list[i - 1].End)
                {
                    throw new ArgumentException(
                        $"Elements {i - 1} and {i} violate the canonical-list contract " +
                        $"(ordered ascending, pairwise disjoint): {list[i - 1]} then " +
                        $"{list[i]}. Overlapping intervals mean a producer bug -- " +
                        "coalesce deliberate overlaps with Union before composing. " +
                        "(Touching end-to-start is legal.)",
                        paramName);
                }
            }
        }
    }
}
