using System;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Locations;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Computes the next upper transit (local meridian crossing, HA = 0) of a stellar
    /// target.
    /// </summary>
    public static class TransitTime
    {
        /// <summary>
        /// Returns the first UTC instant at or after <paramref name="searchFromUtc"/> when
        /// the target transits (crosses the local meridian, HA = 0) as seen from the given
        /// location.
        /// </summary>
        /// <remarks>
        /// Assumes stellar fixed RA/Dec. Inverts <c>LST(t) = RA</c> analytically in one step
        /// -- no numerical root finding, constant cost.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="searchFromUtc">
        /// The lower bound for the search. Must be UTC (<see cref="DateTimeKind.Utc"/>).
        /// </param>
        /// <returns>
        /// The next UTC instant at or after <paramref name="searchFromUtc"/> when the target
        /// transits. <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static DateTime UtcAtOrAfter(Target target, Location location, DateTime searchFromUtc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            double lonDegEast = location.West ? -location.Longitude : location.Longitude;
            double raHours = target.RightAscension;

            double lstRef = SiderealTime.Local(searchFromUtc, lonDegEast);
            double deltaLst = MeeusUtility.Norm24(raHours - lstRef);

            // Advance UT by the solar-hour equivalent of deltaLst sidereal hours.
            double deltaUtHours = deltaLst * 24.0 / SiderealTime.SiderealHoursPerSolarDay;
            return searchFromUtc.AddHours(deltaUtHours);
        }

        /// <summary>
        /// Returns the signed offset between the next upper transit (at or after
        /// <paramref name="sessionStartUtc"/>) and the temporal midpoint of the session
        /// window <c>[<paramref name="sessionStartUtc"/>, <paramref name="sessionEndUtc"/>]</c>.
        /// </summary>
        /// <remarks>
        /// Positive results mean the transit falls <em>after</em> the midpoint (the window
        /// is pre-transit-skewed); negative means before (post-transit-skewed); zero means
        /// the window is centered on transit. Caller can <c>.Duration()</c> for absolute
        /// distance, or inspect the sign for direction. Useful as a transit-centeredness
        /// tiebreaker input in interval scheduling.
        ///
        /// Composes <see cref="UtcAtOrAfter"/> on <paramref name="sessionStartUtc"/>; if the
        /// window doesn't span the next upper transit (e.g. the transit lies after
        /// <paramref name="sessionEndUtc"/>), the returned distance refers to that next
        /// transit (which may be entirely outside the window). Caller decides whether
        /// that's meaningful for their tiebreaker policy.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="sessionStartUtc">Session start, UTC.</param>
        /// <param name="sessionEndUtc">Session end, UTC. Must be &gt;= start.</param>
        /// <returns>
        /// Signed offset <c>transit - midpoint</c>. Positive = transit after midpoint.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static TimeSpan DistanceFromMidpoint(
            Target target, Location location,
            DateTime sessionStartUtc, DateTime sessionEndUtc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            DateTime midpoint = sessionStartUtc + TimeSpan.FromTicks((sessionEndUtc - sessionStartUtc).Ticks / 2);
            DateTime transit = UtcAtOrAfter(target, location, sessionStartUtc);
            return transit - midpoint;
        }
    }
}
