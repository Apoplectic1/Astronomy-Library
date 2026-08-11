using System;
using System.Collections.Generic;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Moon
{
    /// <summary>
    /// Topocentric target-moon angular separation and moon-clear window helpers. Drives
    /// narrowband / broadband scheduling decisions that care about moon contamination.
    /// </summary>
    public static class MoonSeparation
    {
        /// <summary>
        /// Topocentric angular separation (degrees) between the target and the Moon at the
        /// given UTC instant, as seen from the observer location.
        /// </summary>
        /// <remarks>
        /// This is the number that actually governs moon-contamination in an image --
        /// geocentric separation is only a proxy. Composes Core's target Alt/Az with
        /// <see cref="AstroUtil"/>'s moon Alt/Az via the spherical law of cosines.
        /// Result is always in <c>[0, 180]</c>.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static double DegreesAt(Target target, Location location, DateTime utc)
            => ObserveAt(target, location, utc).SeparationDeg;

        /// <summary>
        /// Topocentric target-moon separation (degrees), topocentric moon altitude
        /// (degrees), and topocentric moon azimuth (degrees from North, clockwise)
        /// at the given UTC instant.
        /// </summary>
        /// <remarks>
        /// Same separation math as <see cref="DegreesAt"/>; <see cref="DegreesAt"/> is now
        /// a thin wrapper around this method. Both moon altitude and azimuth are
        /// topocentric for the observer location at <paramref name="utc"/>. Returning
        /// azimuth too lets K-S sky-brightness callers (which need full moon alt/az)
        /// use the same lookup that gives them the separation.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="utc">Instant to evaluate at. Must be UTC.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static (double SeparationDeg, double MoonAltDeg, double MoonAzDeg) ObserveAt(
            Target target, Location location, DateTime utc)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            AltAz targetAltAz = AltAzCalculator.At(target, location, utc);
            double tAlt = targetAltAz.Altitude;
            double tAz  = targetAltAz.Azimuth;

            double latSigned = location.North ?  location.Latitude  : -location.Latitude;
            double lonEast   = location.West  ? -location.Longitude :  location.Longitude;
            ObserverInfo observer = new ObserverInfo(latSigned, lonEast, location.Elevation);

            // Single-pass Meeus: GetMoonAltAz runs MoonPosition.Topocentric once and
            // returns both components. Calling GetMoonAltitude + GetMoonAzimuth would
            // pay the periodic-term sum twice for the same UTC instant.
            (double mAlt, double mAz) = AstroUtil.GetMoonAltAz(utc, observer);

            double t1  = tAlt * Math.PI / 180.0;
            double t2  = mAlt * Math.PI / 180.0;
            double da  = (tAz - mAz) * Math.PI / 180.0;

            double cosSep = Math.Sin(t1) * Math.Sin(t2) + Math.Cos(t1) * Math.Cos(t2) * Math.Cos(da);
            if (cosSep >  1.0) cosSep =  1.0;
            if (cosSep < -1.0) cosSep = -1.0;
            double sepDeg = Math.Acos(cosSep) * 180.0 / Math.PI;

            return (sepDeg, mAlt, mAz);
        }

        /// <summary>
        /// Contiguous UTC intervals during the night when the target-moon separation is at
        /// or above <paramref name="minSepDeg"/>.
        /// </summary>
        /// <remarks>
        /// Samples at 10-minute granularity then linearly interpolates threshold crossings
        /// between adjacent samples for a ~1-minute-accurate boundary.
        /// </remarks>
        /// <param name="target">Target RA/Dec. Non-null.</param>
        /// <param name="location">Observer position. Non-null.</param>
        /// <param name="night">Night-window bounds (Kind=Utc).</param>
        /// <param name="minSepDeg">Separation threshold in degrees (e.g. 60 for broadband, 30 for narrowband).</param>
        /// <returns>
        /// Empty list if the moon is below the threshold for the entire night, the night is
        /// invalid (polar day / polar night), or the target is never clear of the moon.
        /// Otherwise a canonical <see cref="UtcInterval"/> list (ordered, disjoint, merged).
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static IReadOnlyList<UtcInterval> IntervalsAboveDeg(
            Target target, Location location, NightWindow night, double minSepDeg)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);

            var result = new List<UtcInterval>();
            if (!night.IsValid) return result;

            DateTime startUtc = night.AstronomicalDusk;
            DateTime endUtc   = night.AstronomicalDawn;
            TimeSpan sampleSize = TimeSpan.FromMinutes(10);

            DateTime tPrev = startUtc;
            double sepPrev = DegreesAt(target, location, tPrev);
            bool abovePrev = sepPrev >= minSepDeg;
            DateTime? currentStart = abovePrev ? (DateTime?)tPrev : null;

            DateTime tCur = startUtc.Add(sampleSize);
            while (tCur <= endUtc)
            {
                double sepCur = DegreesAt(target, location, tCur);
                bool aboveCur = sepCur >= minSepDeg;

                if (abovePrev != aboveCur)
                {
                    double frac = (minSepDeg - sepPrev) / (sepCur - sepPrev);
                    DateTime crossing = tPrev.AddTicks((long)(frac * (tCur - tPrev).Ticks));
                    if (aboveCur)
                    {
                        currentStart = crossing;
                    }
                    else if (currentStart.HasValue)
                    {
                        // An interpolated crossing landing exactly on the open start
                        // would be a zero-length interval -- no clear time, nothing
                        // to emit.
                        if (crossing > currentStart.Value)
                            result.Add(new UtcInterval(currentStart.Value, crossing));
                        currentStart = null;
                    }
                }

                tPrev = tCur;
                sepPrev = sepCur;
                abovePrev = aboveCur;
                tCur = tCur.Add(sampleSize);
            }

            if (currentStart.HasValue && endUtc > currentStart.Value)
            {
                result.Add(new UtcInterval(currentStart.Value, endUtc));
            }

            return result;
        }
    }
}
