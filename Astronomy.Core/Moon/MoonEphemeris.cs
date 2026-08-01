using System;
using System.Collections.Generic;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Brightness;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Time;

namespace Astronomy.Core.Moon
{
    /// <summary>
    /// Samples the Moon's per-minute state (topocentric AltAz + distance + age +
    /// phase + illumination) at a uniform time grid as observed from a fixed
    /// site. The shared per-night primitive that drives moon-clear gates
    /// (<see cref="MoonSeparation"/>) and sky-brightness walks
    /// (<see cref="Astronomy.Core.Brightness.SkyBrightness.KsAt"/>) downstream;
    /// memoized by TP's <c>mMoonAxis</c> and IS's Tier 2 cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-sample cost is ~5 µs (one
    /// <see cref="Astronomy.Core.Astrometry.Meeus.MoonPosition.Topocentric"/>
    /// periodic-term sum plus the airmass / refraction / illumination glue) —
    /// O(1) per sample, no hot-loop allocations.
    /// </para>
    /// </remarks>
    public static class MoonEphemeris
    {
        /// <summary>
        /// Returns <paramref name="count"/> samples at <paramref name="step"/>
        /// spacing, starting at <paramref name="startUtc"/>. Index 0 is the sample
        /// at <paramref name="startUtc"/>; index <c>i</c> is the sample at
        /// <c>startUtc + i * step</c>.
        /// </summary>
        /// <param name="location">Observer position (latitude / longitude / elevation).</param>
        /// <param name="startUtc">
        /// First sample instant. Must be <see cref="DateTimeKind.Utc"/> per the Core
        /// contract.
        /// </param>
        /// <param name="step">Spacing between samples. Must be positive.</param>
        /// <param name="count">Number of samples. Must be &gt;= 0.</param>
        /// <returns>
        /// Per-sample <see cref="MoonSample"/>s. For <paramref name="count"/> == 0
        /// returns an empty list.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="step"/> is non-positive or <paramref name="count"/> is negative.
        /// </exception>
        public static IReadOnlyList<MoonSample> Sample(
            Location location, DateTime startUtc, TimeSpan step, int count)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (step <= TimeSpan.Zero)
                throw new ArgumentException("step must be positive", nameof(step));
            if (count < 0)
                throw new ArgumentException("count must be >= 0", nameof(count));
            if (count == 0) return Array.Empty<MoonSample>();

            var (latSigned, lonEast) = location.AsSignedDegrees();
            double elevationM = location.Elevation;
            DateTime utc0 = TimeKindGuard.AsUtc(startUtc);

            MoonSample[] samples = new MoonSample[count];
            for (int i = 0; i < count; i++)
            {
                DateTime t = utc0 + TimeSpan.FromTicks(step.Ticks * i);

                double jd = JulianDate.FromUtc(t);
                double lstDeg = SiderealTime.Local(t, lonEast) * 15.0;

                // Topocentric (RA, Dec, distance) -- parallax-corrected via Meeus 40.
                (double raDeg, double decDeg, double distKm) =
                    MoonPosition.Topocentric(jd, lstDeg, latSigned, elevationM);

                // Reduce (LST, RA, Dec, lat) -> (geometric alt, az) via the shared
                // TargetGeometry primitives. HA in sidereal hours; Az from North CW.
                double haHours = MeeusUtility.NormPm180(lstDeg - raDeg) / 15.0;
                double altGeom = TargetGeometry.AltitudeAtHourAngle(haHours, latSigned, decDeg);
                double az      = TargetGeometry.AzimuthAtHourAngle (haHours, latSigned, decDeg);
                double altApp  = altGeom + Refraction.SaemundssonDeg(altGeom);

                double ageDays   = LunarAge.DaysAt(t);
                double phaseDeg  = SkyBrightness.PhaseAngleDegFromAgeDays(ageDays);
                double illumFrac = MoonIllumination.Fraction(jd);

                samples[i] = new MoonSample
                {
                    AltDegGeometric = altGeom,
                    AltDegApparent  = altApp,
                    AzDeg           = az,
                    DistanceKm      = distKm,
                    AgeDays         = ageDays,
                    PhaseAngleDeg   = phaseDeg,
                    IlluminatedFrac = illumFrac,
                };
            }
            return samples;
        }

        /// <summary>
        /// Convenience overload that samples across the astronomical night
        /// <paramref name="night"/> (<see cref="NightWindow.AstronomicalDusk"/> to
        /// <see cref="NightWindow.AstronomicalDawn"/>) at <paramref name="step"/>
        /// spacing. Returns an empty list when <paramref name="night"/> is invalid
        /// (polar day / polar night).
        /// </summary>
        public static IReadOnlyList<MoonSample> Sample(
            Location location, NightWindow night, TimeSpan step)
        {
            if (!night.IsValid) return Array.Empty<MoonSample>();
            int count = (int)((night.AstronomicalDawn - night.AstronomicalDusk).TotalMinutes
                              / step.TotalMinutes) + 1;
            if (count <= 0) return Array.Empty<MoonSample>();
            return Sample(location, night.AstronomicalDusk, step, count);
        }
    }
}
