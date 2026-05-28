using System;
using System.Collections.Generic;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Locations;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;

namespace Astronomy.Core.Session
{
    /// <summary>
    /// Samples a stellar target's (altitude, azimuth) at a uniform time grid in a
    /// single pass. Returns per-sample <see cref="AltAzSample"/> records carrying
    /// both geometric and Saemundsson-refracted altitudes plus azimuth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Equivalent to calling <see cref="AltAzCalculator.At"/> once per grid point
    /// (plus a refraction call per sample), but <see cref="SiderealTime.Local"/>
    /// is evaluated only at the grid's start and advanced linearly by a constant
    /// sidereal-per-solar step for each subsequent sample. GMST is linear in UT
    /// to well below arcsecond precision across a single night, so the
    /// linear-advance result matches per-sample re-evaluation to many decimal
    /// places; the difference is far below chart pixel resolution.
    /// </para>
    /// <para>
    /// Returned samples carry both <see cref="AltAzSample.AltDegGeometric"/> (the
    /// unrefracted altitude consumed by session-placement math and horizon
    /// gates) and <see cref="AltAzSample.AltDegApparent"/> (Saemundsson-refracted,
    /// consumed by K-S sky brightness and visual displays). Azimuth is from North
    /// clockwise [0, 360).
    /// </para>
    /// </remarks>
    public static class AltitudeCurve
    {
        /// <summary>
        /// Returns <paramref name="count"/> samples at <paramref name="step"/>
        /// spacing, starting at <paramref name="startUtc"/>. Index 0 is the sample
        /// at <paramref name="startUtc"/>; index <c>i</c> is the sample at
        /// <c>startUtc + i * step</c>.
        /// </summary>
        /// <param name="target">Target RA/Dec in the Core convention (unsigned + North flag).</param>
        /// <param name="location">Observer latitude/longitude in the Core convention.</param>
        /// <param name="startUtc">
        /// First sample instant. Must be <see cref="DateTimeKind.Utc"/> per the Core
        /// contract; callers converting from local wall-clock should use
        /// <c>DateTime.SpecifyKind(localDt, DateTimeKind.Local).ToUniversalTime()</c>.
        /// </param>
        /// <param name="step">Spacing between samples. Must be positive.</param>
        /// <param name="count">Number of samples. Must be &gt;= 0.</param>
        /// <returns>
        /// Per-sample <see cref="AltAzSample"/>s, each carrying geometric +
        /// apparent altitudes and azimuth. For <paramref name="count"/> == 0
        /// returns an empty list.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="step"/> is non-positive or <paramref name="count"/> is negative.
        /// </exception>
        public static IReadOnlyList<AltAzSample> Sample(
            Target target, Location location, DateTime startUtc, TimeSpan step, int count)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(location);
            if (step <= TimeSpan.Zero)
                throw new ArgumentException("step must be positive", nameof(step));
            if (count < 0)
                throw new ArgumentException("count must be >= 0", nameof(count));
            if (count == 0) return Array.Empty<AltAzSample>();

            var (latSigned, lonDegEast) = location.AsSignedDegrees();
            var (decSigned, raHours) = target.AsSignedRaDec();

            double lstStart = SiderealTime.Local(startUtc, lonDegEast);
            // LST advances at the sidereal rate: one solar hour of UT elapses
            // SiderealHoursPerSolarDay / 24 sidereal hours of LST.
            double lstStepHours = step.TotalHours * SiderealTime.SiderealHoursPerSolarDay / 24.0;

            AltAzSample[] samples = new AltAzSample[count];
            for (int i = 0; i < count; i++)
            {
                // Compute each LST independently from the start rather than accumulating
                // per-step, so the result is insensitive to step count. For ~1000 samples
                // the difference vs accumulation is cosmetic, but it avoids any question
                // about drift for larger grids (e.g. a full-year precompute).
                double lst = lstStart + i * lstStepHours;
                double ha = lst - raHours;
                double altGeom = TargetGeometry.AltitudeAtHourAngle(ha, latSigned, decSigned);
                double az = TargetGeometry.AzimuthAtHourAngle(ha, latSigned, decSigned);
                double altApp = altGeom + Refraction.SaemundssonDeg(altGeom);
                samples[i] = new AltAzSample
                {
                    AltDegGeometric = altGeom,
                    AltDegApparent  = altApp,
                    AzDeg           = az,
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
        public static IReadOnlyList<AltAzSample> Sample(
            Target target, Location location, NightWindow night, TimeSpan step)
        {
            if (!night.IsValid) return Array.Empty<AltAzSample>();
            int count = (int)((night.AstronomicalDawn - night.AstronomicalDusk).TotalMinutes
                              / step.TotalMinutes) + 1;
            if (count <= 0) return Array.Empty<AltAzSample>();
            return Sample(target, location, night.AstronomicalDusk, step, count);
        }
    }
}
