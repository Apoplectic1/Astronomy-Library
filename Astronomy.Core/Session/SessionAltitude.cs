using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Pure-evaluation helpers that report the floor (lowest altitude reached) and
    /// ceiling (highest altitude reached) of an already-placed session window.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="BestSession"/>: that class is about <em>placement</em>
    /// (deciding when a session should run); this one is about <em>evaluation</em>
    /// (reporting properties of a session that has already been placed). Typical use is
    /// post-<see cref="BestSession.PlaceBest"/> / <see cref="BestSession.PlaceCentered"/>:
    /// take the returned <c>(Start, End)</c> window and ask for its lowest altitude
    /// (<see cref="Floor"/>) or highest altitude (<see cref="Ceiling"/>). Future
    /// session-evaluation helpers (e.g. average altitude, minimum target-moon
    /// separation, integrated airmass) would slot in alongside.
    /// </remarks>
    public static class SessionAltitude
    {
        /// <summary>
        /// Returns the lowest altitude (degrees) the target reaches during the session
        /// <c>[<paramref name="sessionStartUtc"/>, <paramref name="sessionEndUtc"/>]</c>.
        /// </summary>
        /// <remarks>
        /// For a transit-near placement (the only kind <see cref="BestSession.PlaceBest"/>
        /// produces) altitude is monotone within each half-arc of the transit, so the
        /// floor is always at one of the session endpoints. This method evaluates both
        /// endpoints via <see cref="AltAzCalculator.At"/> and returns the smaller value.
        /// Two Meeus calls per invocation.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="sessionStartUtc">Session start, UTC.</param>
        /// <param name="sessionEndUtc">Session end, UTC. Must be &gt;= start.</param>
        /// <returns>Lowest altitude reached during the session, in degrees.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double Floor(
            Target target, Location location,
            DateTime sessionStartUtc, DateTime sessionEndUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            double altStart = AltAzCalculator.At(target, location, sessionStartUtc).Altitude;
            double altEnd   = AltAzCalculator.At(target, location, sessionEndUtc).Altitude;
            return Math.Min(altStart, altEnd);
        }

        /// <summary>
        /// Returns the highest altitude (degrees) the target reaches during the session
        /// <c>[<paramref name="sessionStartUtc"/>, <paramref name="sessionEndUtc"/>]</c>.
        /// </summary>
        /// <remarks>
        /// If the session straddles the next upper transit (HA = 0), the ceiling is
        /// <see cref="TargetGeometry.MeridianAltitude"/>. Otherwise altitude is monotone
        /// within the session and the ceiling is the higher of the two endpoint
        /// altitudes. Three Meeus calls in the worst case (transit lookup + two
        /// altitudes); the transit lookup is a single sidereal-time inverse, not a sweep.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="sessionStartUtc">Session start, UTC.</param>
        /// <param name="sessionEndUtc">Session end, UTC. Must be &gt;= start.</param>
        /// <returns>Highest altitude reached during the session, in degrees.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double Ceiling(
            Target target, Location location,
            DateTime sessionStartUtc, DateTime sessionEndUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            DateTime transitUtc = TransitTime.UtcAtOrAfter(target, location, sessionStartUtc);
            if (transitUtc <= sessionEndUtc)
            {
                double latDeg = location.North ? location.Latitude : -location.Latitude;
                double decDeg = target.North ? target.Declination : -target.Declination;
                return TargetGeometry.MeridianAltitude(latDeg, decDeg);
            }

            double altStart = AltAzCalculator.At(target, location, sessionStartUtc).Altitude;
            double altEnd   = AltAzCalculator.At(target, location, sessionEndUtc).Altitude;
            return Math.Max(altStart, altEnd);
        }

        /// <summary>
        /// Returns the altitude (degrees) the target reaches at the temporal midpoint of
        /// the session window
        /// <c>[<paramref name="sessionStartUtc"/>, <paramref name="sessionEndUtc"/>]</c>.
        /// </summary>
        /// <remarks>
        /// Single-sample window-quality proxy useful for tiebreaker decisions in interval
        /// scheduling (e.g. "which same-priority window has the better midpoint altitude").
        /// One Meeus call per invocation via <see cref="AltAzCalculator.At"/>; the midpoint
        /// itself is computed by tick-arithmetic on the two endpoints.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="sessionStartUtc">Session start, UTC.</param>
        /// <param name="sessionEndUtc">Session end, UTC. Must be &gt;= start.</param>
        /// <returns>Altitude at the temporal midpoint, in degrees.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double Midpoint(
            Target target, Location location,
            DateTime sessionStartUtc, DateTime sessionEndUtc)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (location == null) throw new ArgumentNullException(nameof(location));

            DateTime midpoint = sessionStartUtc + TimeSpan.FromTicks((sessionEndUtc - sessionStartUtc).Ticks / 2);
            return AltAzCalculator.At(target, location, midpoint).Altitude;
        }
    }
}
