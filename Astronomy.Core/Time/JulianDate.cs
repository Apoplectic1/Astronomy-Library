using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// Julian Date conversions for UTC instants.
    /// </summary>
    public static class JulianDate
    {
        /// <summary>
        /// Julian Date of the given UTC instant.
        /// </summary>
        /// <remarks>
        /// Uses the OADate idiom (days since 1899-12-30 00:00 UT plus the offset to
        /// JD 2415018.5), accurate to sub-millisecond for all dates representable by
        /// <see cref="DateTime"/>.
        /// </remarks>
        /// <param name="utc">
        /// Instant to convert. Must be <see cref="DateTimeKind.Utc"/> — this is the Library's
        /// central time-contract gate, and a non-UTC instant throws rather than being
        /// reinterpreted. See <see cref="TimeKindGuard.RequireUtc"/> for why.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="utc"/> is not <see cref="DateTimeKind.Utc"/>.
        /// </exception>
        public static double FromUtc(DateTime utc)
        {
            // Single choke point: SiderealTime.Local routes here, and every normalising caller
            // arrives as FromUtc(TimeKindGuard.AsUtc(x)). Guarding here means no downstream
            // primitive needs its own guard.
            TimeKindGuard.RequireUtc(utc, nameof(utc));

            return utc.ToOADate() + 2415018.5;
        }
    }
}
