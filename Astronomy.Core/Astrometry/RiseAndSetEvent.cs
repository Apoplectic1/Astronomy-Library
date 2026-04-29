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
        public DateTime? Rise { get; }
        public DateTime? Set  { get; }

        public RiseAndSetEvent(DateTime? rise, DateTime? set)
        {
            Rise = rise;
            Set  = set;
        }
    }
}
