using System;

namespace Astronomy.Core.Brightness
{
    /// <summary>
    /// Krisciunas–Schaefer 1991 closed-form sky-brightness model. Given a target
    /// position, the moon's position, the moon's phase angle, and atmospheric
    /// parameters, returns the predicted sky brightness in mag/arcsec² at the
    /// target. Combines a moonless dark-sky baseline (V₀ extincted by the target's
    /// airmass) with the moon-induced contribution from K-S 1991 eq. 15.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conventions: lower magnitude values = brighter sky. Phase angle α = 0°
    /// is full moon (brightest); α = 180° is new moon (no contribution).
    /// </para>
    /// <para>
    /// V1 approximation: extinction coefficient k is assumed to scale with
    /// wavelength via Rayleigh λ⁻⁴ from a 500 nm reference (see <see cref="ScaleK"/>).
    /// The aerosol component (less wavelength-sensitive) is folded into the
    /// effective k₅₀₀ rather than modeled separately. Acceptable for amateur-band
    /// sky-brightness estimates; a tabulated Hayes–Latham extinction profile would
    /// be a v2 refinement.
    /// </para>
    /// <para>
    /// Bandwidth: each nL contribution (dark-sky baseline, twilight, moon) scales
    /// linearly with passband width for continuum-spectrum sources. The reference
    /// is V-band (<see cref="BWRefNm"/> = 85 nm). A 3 nm narrowband filter
    /// integrates ~28× less continuum brightness — <c>2.5·log₁₀(85/3) ≈ 2.7 mag</c>
    /// darker than the V-band prediction. Narrow airglow emission lines (sodium D
    /// 589 nm, OI 557.7 nm, [OIII] 500.7 nm) are not modeled — a tabulated line
    /// catalog would be a v3 refinement (narrowband OIII catches [OIII] airglow).
    /// </para>
    /// <para>
    /// Reference: Krisciunas, K., &amp; Schaefer, B. E. 1991, PASP, 103, 1033,
    /// "A Model of the Brightness of Moonlight."
    /// </para>
    /// </remarks>
    public static class SkyBrightness
    {
        /// <summary>
        /// V-band reference passband width (nm). Per-band brightness predictions
        /// are scaled by <c>bandwidthNm / BWRefNm</c> on the assumption that
        /// continuum sources contribute linearly with passband width.
        /// </summary>
        public const double BWRefNm = 85.0;

        /// <summary>
        /// V-band centroid wavelength (nm) — reference for the Rayleigh
        /// λ⁻⁴ scaling applied to the twilight contribution (sun-scattered
        /// Rayleigh light is wavelength-dependent: blue scatters more than red).
        /// </summary>
        public const double VBandCenterNm = 540.0;

        /// <summary>
        /// Total sky brightness in mag/arcsec² at the target position, including
        /// the moonless dark-sky baseline (V₀ scaled by target airmass), the solar
        /// twilight contribution (when sun is above astronomical-twilight threshold),
        /// and the moon's contribution (zero if moon is below the horizon).
        /// Contributions compose in linear (nanolambert) space and convert back to
        /// magnitudes for the return value.
        /// </summary>
        /// <param name="targetAltDeg">Target altitude (degrees, 0 = horizon, 90 = zenith).</param>
        /// <param name="targetAzDeg">Target azimuth (degrees from North, clockwise).</param>
        /// <param name="moonAltDeg">
        /// Moon <b>apparent</b> altitude (degrees) -- i.e. the moon's altitude as
        /// it appears through the atmosphere (geometric altitude + atmospheric
        /// refraction). The moon contribution gates on <c>moonAltDeg &gt; 0</c>,
        /// so passing apparent altitude aligns the K-S moonset with the visually
        /// observed horizon (~34&#8242; refraction lift, ~2 min later than the
        /// geometric moonset depending on the moon's descent rate). Callers
        /// already holding a geometric altitude (from e.g.
        /// <see cref="Astronomy.Core.Moon.MoonSeparation"/>) should add
        /// <see cref="Astronomy.Core.Astrometry.Refraction.SaemundssonDeg"/>
        /// to convert before passing in.
        /// </param>
        /// <param name="moonAzDeg">Moon azimuth (degrees from North, clockwise).</param>
        /// <param name="moonPhaseAngleDeg">Phase angle (0 = full, 180 = new).</param>
        /// <param name="sunAltDeg">Sun altitude (degrees). Below −18° contributes zero.</param>
        /// <param name="extinctionKBand">Atmospheric extinction at the band's wavelength (mag/airmass).</param>
        /// <param name="v0Mag">Moonless zenith dark-sky brightness (mag/arcsec²), V-band broadband.</param>
        /// <param name="bandwidthNm">
        /// Filter passband (nm). Each nL contribution (dark, twilight, moon) is scaled by
        /// <c>bandwidthNm / <see cref="BWRefNm"/></c> before summing — continuum sources
        /// contribute linearly with passband width. Pass <see cref="BWRefNm"/> for the
        /// V-band reference (no scaling).
        /// </param>
        /// <param name="centerNm">
        /// Filter center wavelength (nm). Used for the Rayleigh λ⁻⁴ scaling of the
        /// twilight contribution (sun-scattered Rayleigh light is wavelength-dependent;
        /// blue narrowband sees brighter twilight than red narrowband). Pass
        /// <see cref="VBandCenterNm"/> for the V-band reference (no scaling). The
        /// per-band extinction <paramref name="extinctionKBand"/> already encodes
        /// the line-of-sight extinction wavelength dependence; this parameter only
        /// controls the twilight-scatter wavelength scaling.
        /// </param>
        /// <returns>
        /// Sky brightness at the target in mag/arcsec². <see cref="double.NaN"/>
        /// if target is at or below the horizon (no observation).
        /// </returns>
        public static double KsAt(
            double targetAltDeg, double targetAzDeg,
            double moonAltDeg,   double moonAzDeg,
            double moonPhaseAngleDeg,
            double sunAltDeg,
            double extinctionKBand,
            double v0Mag,
            double bandwidthNm,
            double centerNm)
        {
            if (targetAltDeg <= 0.0) return double.NaN;

            double targetX = Airmass(targetAltDeg);

            // Moonless dark sky at target altitude. V₀ at zenith brightens with airmass
            // (more atmospheric column scattering ground/airglow light) and dims with
            // extinction. Standard amateur formula:
            //     V(X) = V₀ - 2.5 log10(X) + k (X - 1)
            // The first term increases brightness (lower mag) with airmass; the second
            // adds extinction loss for objects seen through more air.
            double vDark = v0Mag - 2.5 * Math.Log10(targetX) + extinctionKBand * (targetX - 1.0);
            double bDark = MagToNanolamberts(vDark);

            // Solar twilight contribution. Twilight.ZenithBrightening returns the
            // V-band-calibrated mag-delta by which solar scattering has brightened
            // the zenith sky vs V₀; we convert that delta to nanolamberts as a
            // separate addition rather than combining magnitudes (which doesn't
            // compose linearly), then scale the nL contribution by the Rayleigh
            // λ⁻⁴ ratio relative to V-band so narrowband-blue sees brighter twilight
            // than narrowband-red (the physically-correct direction). Outside
            // twilight (sun ≤ −18°) the delta is zero and the scaling is moot.
            double bTwilight = 0.0;
            double deltaTwilightMag = Twilight.ZenithBrightening(sunAltDeg);
            if (deltaTwilightMag > 0.0)
            {
                double vTwilight = vDark - deltaTwilightMag;   // brighter sky = lower mag
                double bTwilightVBand = MagToNanolamberts(vTwilight) - bDark;
                double r = VBandCenterNm / centerNm;
                double rayleighScale = r * r * r * r;
                bTwilight = bTwilightVBand * rayleighScale;
            }

            double bMoon = 0.0;
            if (moonAltDeg > 0.0)
            {
                double rhoDeg = AngularDistanceDeg(targetAltDeg, targetAzDeg, moonAltDeg, moonAzDeg);
                double moonX = Airmass(moonAltDeg);

                // K-S 1991 eq. 8 -- moon's illuminance outside the atmosphere as a
                // function of phase angle (units: foot-lamberts, but the constants
                // line up with the eq. 15 conversion below).
                double absAlpha = Math.Abs(moonPhaseAngleDeg);
                double iStar = Math.Pow(10.0,
                    -0.4 * (3.84 + 0.026 * absAlpha + 4.0e-9 * Math.Pow(absAlpha, 4)));

                // K-S 1991 eq. 16/17 -- aureole (Rayleigh + Mie) scattering function
                // f(ρ): nL per (foot-lambert × airmass-attenuation). Rayleigh term
                // dominates at large ρ; Mie aureole term dominates near the moon.
                double rhoRad = rhoDeg * Math.PI / 180.0;
                double cosRho = Math.Cos(rhoRad);
                double f = Math.Pow(10.0, 5.36) * (1.06 + cosRho * cosRho)
                         + Math.Pow(10.0, 6.15 - rhoDeg / 40.0);

                // K-S 1991 eq. 15 -- moon's contribution to the sky brightness in nL.
                // Light from the moon is extincted by the moon's own airmass before
                // reaching the scattering layer; what scatters into the line of sight
                // toward the target is then attenuated by the target's airmass.
                bMoon = f * iStar
                      * Math.Pow(10.0, -0.4 * extinctionKBand * moonX)
                      * (1.0 - Math.Pow(10.0, -0.4 * extinctionKBand * targetX));
            }

            // Continuum bandwidth scaling: integrated nL brightness in the filter's
            // passband scales linearly with passband width for continuous-spectrum
            // sources (dark-sky, twilight scatter, moonlight scatter). Applied once
            // to the summed nL contribution before the mag conversion.
            double bandwidthScale = bandwidthNm / BWRefNm;
            return NanolambertsToMag((bDark + bTwilight + bMoon) * bandwidthScale);
        }

        /// <summary>
        /// Rayleigh λ⁻⁴ scaling from a 500 nm reference k to a band's center
        /// wavelength. v1 approximation: k(λ) = k₅₀₀ × (500 / λ)⁴. Real sites have
        /// non-Rayleigh aerosol contributions that flatten the curve at longer
        /// wavelengths; folding them in is a v2 refinement.
        /// </summary>
        public static double ScaleK(double k500nm, double centerNm)
        {
            if (centerNm <= 0.0) return k500nm;
            double r = 500.0 / centerNm;
            return k500nm * r * r * r * r;
        }

        /// <summary>
        /// Airmass at altitude via Pickering 2002 (accurate to ~1% even near the
        /// horizon, unlike sec(z) which diverges). Returns 1.0 at the zenith,
        /// rising rapidly toward the horizon. Below horizon returns a large
        /// sentinel.
        /// </summary>
        public static double Airmass(double altDeg)
        {
            if (altDeg <= 0.0) return 100.0;
            double a = altDeg + 244.0 / (165.0 + 47.0 * Math.Pow(altDeg, 1.1));
            return 1.0 / Math.Sin(a * Math.PI / 180.0);
        }

        /// <summary>
        /// Convert moon synodic age (days since new moon) to K-S phase angle α
        /// in degrees. α = 0° at full moon (max brightness), α = 180° at new moon.
        /// Useful glue between <see cref="Astronomy.Core.Moon.LunarAge.DaysAt"/>
        /// and <see cref="KsAt"/>.
        /// </summary>
        public static double PhaseAngleDegFromAgeDays(double ageDays)
        {
            // Read the synodic-month length from the canonical source so a future
            // refinement of the constant can't drift between callers. Compile-time
            // constant fold (LunarAge.SynodicMonthDays is `public const`) -- no
            // runtime indirection.
            double synodicMonth = Moon.LunarAge.SynodicMonthDays;
            double elong = (ageDays % synodicMonth) * 360.0 / synodicMonth;
            if (elong < 0.0) elong += 360.0;
            return Math.Abs(180.0 - elong);
        }

        // ---- internal helpers ----

        // Garstang/K-S nL ↔ mag/arcsec² conversion.
        private static double MagToNanolamberts(double mag)
            => 34.08 * Math.Exp(20.7233 - 0.92104 * mag);

        private static double NanolambertsToMag(double nL)
            => (20.7233 - Math.Log(nL / 34.08)) / 0.92104;

        // Spherical-trig great-circle distance between two horizon-coordinate points.
        private static double AngularDistanceDeg(double alt1, double az1, double alt2, double az2)
        {
            const double R = Math.PI / 180.0;
            double cos = Math.FusedMultiplyAdd(
                Math.Cos(alt1 * R) * Math.Cos(alt2 * R), Math.Cos((az1 - az2) * R),
                Math.Sin(alt1 * R) * Math.Sin(alt2 * R));
            if (cos > 1.0) cos = 1.0;
            else if (cos < -1.0) cos = -1.0;
            return Math.Acos(cos) / R;
        }
    }
}
