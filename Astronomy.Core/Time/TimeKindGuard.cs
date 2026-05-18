using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Canonical helper for normalising arbitrary <see cref="DateTime"/> input to
    /// <see cref="DateTimeKind.Utc"/> at Library boundary surfaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Public Library entry points that take a <see cref="DateTime"/> should route it
    /// through <see cref="AsUtc"/> before downstream math. The convention is stated
    /// in CLAUDE.md: <see cref="DateTimeKind.Local"/> is converted via the machine
    /// timezone; <see cref="DateTimeKind.Unspecified"/> is treated as Local;
    /// <see cref="DateTimeKind.Utc"/> is a no-op.
    /// </para>
    /// <para>
    /// One deliberate exception: <see cref="Astronomy.Core.Moon.LunarAge.DaysAt"/>
    /// throws on non-Utc inputs rather than converting, because it sits inside the
    /// <c>BestSession.MoonClearIntersect</c> tight loop where a stray non-Utc kind
    /// would silently corrupt the result across an entire night sweep.
    /// </para>
    /// </remarks>
    internal static class TimeKindGuard
    {
        /// <summary>
        /// Normalise <paramref name="dt"/> to <see cref="DateTimeKind.Utc"/>.
        /// </summary>
        public static DateTime AsUtc(DateTime dt)
        {
            switch (dt.Kind)
            {
                case DateTimeKind.Utc:   return dt;
                case DateTimeKind.Local: return dt.ToUniversalTime();
                default:                 return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
            }
        }
    }
}
