using System;

namespace Astronomy.Core.Time
{
    /// <summary>
    /// An observation instant paired with the time zone the observer experiences it in.
    /// <see cref="Utc"/> is the canonical instant for astronomy math; <see cref="Zone"/>
    /// carries the DST rules a consumer needs when round-tripping to local wall-clock for
    /// display, picker round-trips, or twilight-window framing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Convention: <see cref="Utc"/> is by definition <see cref="DateTimeKind.Utc"/>.
    /// Construct via <see cref="FromLocal"/> or <see cref="Now"/> rather than the primary
    /// constructor when starting from a local-frame instant -- they handle the DST-aware
    /// conversion against <paramref name="Zone"/>. The primary constructor stores what you
    /// give it and does not validate kind.
    /// </para>
    /// <para>
    /// Record-struct semantics give free structural equality and the
    /// <c>moment with { Zone = newZone }</c> mutation idiom that consumer code (e.g.
    /// TargetPlanner's UTC-offset spinner) relies on.
    /// </para>
    /// </remarks>
    public readonly record struct ObservationMoment(DateTime Utc, TimeZoneInfo Zone)
    {
        /// <summary>
        /// Builds an <see cref="ObservationMoment"/> from a local-frame instant and its
        /// time zone. Local-to-UTC conversion routes through
        /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>, which
        /// resolves DST cleanly (ambiguous local times in the autumn fall-back window are
        /// resolved by the system convention; invalid local times in the spring-forward
        /// window throw <see cref="ArgumentException"/>).
        /// </summary>
        /// <param name="local">A local-frame instant. Kind is normalised to
        /// <see cref="DateTimeKind.Unspecified"/> internally so the conversion uses
        /// <paramref name="zone"/> rather than the system local zone.</param>
        /// <param name="zone">The time zone <paramref name="local"/> is expressed in.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="zone"/> is <see langword="null"/>.
        /// </exception>
        public static ObservationMoment FromLocal(DateTime local, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
            return new ObservationMoment(utc, zone);
        }

        /// <summary>
        /// Builds an <see cref="ObservationMoment"/> at the current wall-clock instant,
        /// expressed in <paramref name="zone"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="zone"/> is <see langword="null"/>.
        /// </exception>
        public static ObservationMoment Now(TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            return new ObservationMoment(DateTime.UtcNow, zone);
        }
    }
}
