using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Night
{
    /// <summary>
    /// Night window at parameterized sun-altitude thresholds (astronomical -18&#176;,
    /// nautical -12&#176;, or civil -6&#176;). Sibling of
    /// <see cref="NightCalculator.ComputeNight"/>, which hard-codes the astronomical
    /// threshold; both delegate to the same Meeus-backed bracketing algorithm.
    /// </summary>
    /// <remarks>
    /// Dusk/dawn instants are returned as <see cref="DateTimeKind.Utc"/>. Arbitrary
    /// sun-altitude thresholds work since we now solve the rise/set equation directly
    /// (Meeus chapter 15) rather than reading three fixed CoordinateSharp bands.
    /// </remarks>
    public static class TwilightCalculator
    {
        /// <summary>Sun altitude threshold for astronomical twilight (&#8722;18&#176;).</summary>
        public const double AstronomicalTwilightSunAlt = -18.0;

        /// <summary>Sun altitude threshold for nautical twilight (&#8722;12&#176;).</summary>
        public const double NauticalTwilightSunAlt     = -12.0;

        /// <summary>Sun altitude threshold for civil twilight (&#8722;6&#176;).</summary>
        public const double CivilTwilightSunAlt        =  -6.0;

        /// <summary>
        /// Returns the night window (dusk -> dawn bracketing <paramref name="location"/>'s
        /// moment) where the sun is at or below <paramref name="sunAltBelowDeg"/>. Matches
        /// <see cref="NightCalculator.ComputeNight"/> when <paramref name="sunAltBelowDeg"/>
        /// is <see cref="AstronomicalTwilightSunAlt"/>.
        /// </summary>
        /// <param name="location">Observer position and local moment. Non-null.</param>
        /// <param name="sunAltBelowDeg">
        /// Geometric altitude threshold in degrees -- typically -18 (astronomical), -12
        /// (nautical), -6 (civil), or -0.833 (official sunrise/sunset). Other values are
        /// permitted -- the underlying solver handles any threshold the sun crosses.
        /// </param>
        /// <returns>
        /// A <see cref="NightWindow"/> with <see cref="DateTimeKind.Utc"/> dusk/dawn
        /// instants. Polar day / polar night falls back to
        /// <see cref="DateTime.MinValue"/> sentinels; use <see cref="NightWindow.IsValid"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="location"/> is <see langword="null"/>.
        /// </exception>
        public static NightWindow ComputeNight(Location location, double sunAltBelowDeg)
        {
            ArgumentNullException.ThrowIfNull(location);
            return NightCalculator.Compute(location, sunAltBelowDeg);
        }
    }
}
