using System;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Result of a rise/set computation. <see cref="Rise"/> and <see cref="Set"/> are
    /// nullable so the caller can distinguish "the body never rises" / "the body never
    /// sets" / "circumpolar above the threshold for the whole day" cases without a
    /// sentinel <see cref="DateTime"/> (matches NINA's <c>RiseAndSetEvent</c> shape).
    /// </summary>
    /// <remarks>
    /// Both events, when present, are <see cref="DateTimeKind.Utc"/>.
    /// </remarks>
    public sealed class RiseAndSetEvent
    {
        /// <summary>UTC instant the body crossed above the threshold today, or <see langword="null"/> if it never rose.</summary>
        public DateTime? Rise { get; }

        /// <summary>UTC instant the body crossed below the threshold today, or <see langword="null"/> if it never set.</summary>
        public DateTime? Set  { get; }

        /// <summary>Wraps a (rise, set) pair. Pass <see langword="null"/> for either side to indicate the event didn't occur.</summary>
        public RiseAndSetEvent(DateTime? rise, DateTime? set)
        {
            Rise = rise;
            Set  = set;
        }
    }
}
