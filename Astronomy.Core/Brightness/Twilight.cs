using System;

namespace Astronomy.Core.Brightness
{
    /// <summary>
    /// Solar-twilight contribution to zenith sky brightness as a function of sun
    /// altitude. Below astronomical twilight (sun_alt ≤ −18°) the contribution is
    /// zero; through nautical / civil twilight it rises rapidly; at and above
    /// horizon (full daylight) it saturates.
    /// </summary>
    /// <remarks>
    /// V1 closed-form fit. Quadratic in sun-depression: ΔV = ((alt + 18) / 18)² × 10
    /// for alt ∈ (−18°, 0°). Calibrated against Patat 2006 / Schaefer 1990 V-band
    /// reference values to ~1 mag accuracy at temperate latitudes:
    /// <list type="bullet">
    /// <item>Sun −18° (astronomical) → ΔV = 0 (sky at V₀)</item>
    /// <item>Sun −12° (nautical)     → ΔV ≈ 1.1 mag brighter</item>
    /// <item>Sun −6°  (civil)        → ΔV ≈ 4.4 mag brighter</item>
    /// <item>Sun  0°  (sunset)       → ΔV ≈ 10 mag brighter</item>
    /// </list>
    /// v2 refinement: drop in Patat 2006's polynomial cubic fit for finer accuracy.
    /// </remarks>
    public static class Twilight
    {
        /// <summary>
        /// Magnitude-delta brightening of the zenith sky vs the dark baseline V₀
        /// produced by atmospheric scattering of solar light. Returns 0 below
        /// astronomical twilight (sun_alt ≤ −18°) and saturates at 12 for sun
        /// above the horizon. See class <see cref="Twilight"/> remarks for the
        /// quadratic-fit calibration sources and v2 refinement notes.
        /// </summary>
        /// <param name="sunAltDeg">
        /// Sun altitude in degrees. Negative values = below the horizon. Method
        /// short-circuits at −18° (no contribution) and 0° (saturation cap of
        /// 12 mag).
        /// </param>
        /// <returns>
        /// Brightening in magnitudes (≥ 0). Compose into total sky brightness in
        /// nanolambert space, not magnitude space; <see cref="SkyBrightness.KsAt"/>
        /// performs the magnitude ↔ nanolambert conversion.
        /// </returns>
        public static double ZenithBrightening(double sunAltDeg)
        {
            if (sunAltDeg <= -18.0) return 0.0;
            if (sunAltDeg >= 0.0) return 12.0;

            double frac = (sunAltDeg + 18.0) / 18.0;   // 0 at -18, 1 at 0
            return frac * frac * 10.0;                  // quadratic ramp 0 -> 10
        }
    }
}
