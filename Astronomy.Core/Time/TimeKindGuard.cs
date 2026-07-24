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

        /// <summary>
        /// Assert that <paramref name="dt"/> is already <see cref="DateTimeKind.Utc"/>,
        /// throwing <see cref="ArgumentException"/> if it is not.
        /// </summary>
        /// <remarks>
        /// The contract gate for time-based math. <see cref="DateTime.ToOADate"/> — which every
        /// Julian-Date conversion rests on — ignores <see cref="DateTime.Kind"/> entirely and reads
        /// the raw tick value, so a <see cref="DateTimeKind.Local"/> or
        /// <see cref="DateTimeKind.Unspecified"/> instant would be silently *reinterpreted* as UTC
        /// and yield an answer wrong by the caller's UTC offset. Failing loudly at the boundary is
        /// the whole point: a caller that meant local time has a bug, and a silent wrong altitude
        /// is far more expensive to find than an exception.
        /// <para>
        /// Callers that legitimately hold a local-frame instant convert first with
        /// <see cref="AsUtc"/>; this method never converts.
        /// </para>
        /// </remarks>
        public static DateTime RequireUtc(DateTime dt, string paramName)
        {
            if (dt.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    $"Expected DateTimeKind.Utc, got {dt.Kind} ({dt:O}). Library time math " +
                    "reinterprets a non-UTC instant as UTC and returns a silently wrong result. " +
                    "Convert at the call site (TimeZoneInfo.ConvertTimeToUtc / .ToUniversalTime, " +
                    "or DateTime.SpecifyKind when the value is already UTC but untagged).",
                    paramName);
            }

            return dt;
        }
    }
}
