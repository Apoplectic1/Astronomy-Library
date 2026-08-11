using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// An immutable UTC time interval with half-open semantics: <see cref="Start"/> is
    /// inside the interval, <see cref="End"/> is not (<c>[Start, End)</c>). The shared
    /// currency for every interval-producing and interval-consuming API in the Library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Construction is the contract gate: both endpoints must already be
    /// <see cref="DateTimeKind.Utc"/> (no conversion — a stray Local kind is a caller
    /// bug, see <see cref="TimeKindGuard.RequireUtc"/>), and <c>End</c> must be strictly
    /// after <c>Start</c>. There is no empty interval; "no time" is an empty
    /// <c>IReadOnlyList&lt;UtcInterval&gt;</c>, not a zero-length element.
    /// </para>
    /// <para>
    /// Half-open semantics make boundary math total: intervals that touch end-to-start
    /// share no instant, so <see cref="Intervals.Intersect"/> of touching intervals is
    /// empty and <see cref="Intervals.Union"/> coalesces them without double-counting
    /// the boundary. Producers' window-boundary <em>placement</em> (e.g. dusk/dawn
    /// clamping in <c>VisibilityWindows</c>) is unaffected — only the edge convention
    /// is made explicit.
    /// </para>
    /// <para>
    /// <c>default(UtcInterval)</c> bypasses the constructor and is invalid (both
    /// endpoints <c>default(DateTime)</c>); the <see cref="Intervals"/> operations
    /// reject it via their input validation.
    /// </para>
    /// </remarks>
    public readonly record struct UtcInterval
    {
        /// <summary>Inclusive start instant (Kind=Utc).</summary>
        public DateTime Start { get; }

        /// <summary>Exclusive end instant (Kind=Utc). Strictly after <see cref="Start"/>.</summary>
        public DateTime End { get; }

        /// <summary>
        /// Creates the interval <c>[start, end)</c>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="start"/> or <paramref name="end"/> is not
        /// <see cref="DateTimeKind.Utc"/>, or <paramref name="end"/> is not strictly
        /// after <paramref name="start"/>.
        /// </exception>
        public UtcInterval(DateTime start, DateTime end)
        {
            TimeKindGuard.RequireUtc(start, nameof(start));
            TimeKindGuard.RequireUtc(end, nameof(end));
            if (end <= start)
            {
                throw new ArgumentException(
                    $"End must be strictly after Start (got [{start:O}, {end:O})). A " +
                    "zero-length interval has no representation -- express \"no time\" " +
                    "as an empty interval list.",
                    nameof(end));
            }

            Start = start;
            End = end;
        }

        /// <summary>Interval length. Always positive.</summary>
        public TimeSpan Duration => End - Start;

        /// <summary>
        /// Whether <paramref name="utc"/> lies inside the half-open interval
        /// (<c>Start &lt;= utc &lt; End</c>).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="utc"/> is not <see cref="DateTimeKind.Utc"/>.
        /// </exception>
        public bool Contains(DateTime utc)
        {
            TimeKindGuard.RequireUtc(utc, nameof(utc));
            return utc >= Start && utc < End;
        }

        /// <summary>
        /// Whether this interval and <paramref name="other"/> share any instant.
        /// Touching intervals (one's <see cref="End"/> equal to the other's
        /// <see cref="Start"/>) do not overlap.
        /// </summary>
        public bool Overlaps(UtcInterval other) => Start < other.End && other.Start < End;

        /// <summary>Deconstructs into <c>(Start, End)</c>.</summary>
        public void Deconstruct(out DateTime start, out DateTime end)
        {
            start = Start;
            end = End;
        }

        /// <summary>Round-trip (ISO 8601) rendering as <c>[Start, End)</c>.</summary>
        public override string ToString() => $"[{Start:O}, {End:O})";
    }
}
