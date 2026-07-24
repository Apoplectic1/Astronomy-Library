using System;

namespace Astronomy.Core.Moon
{
    /// <summary>
    /// Immutable parameters for the K-S Δmag moon gate: how much moon-driven sky
    /// brightening a session minute may carry and still be accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate accepts a minute iff the Krisciunas–Schaefer-predicted sky brightness at
    /// the target is within <see cref="ToleranceMag"/> of the moonless baseline
    /// (<see cref="Astronomy.Core.Brightness.SkyBrightness.KsMoonDeltaMag"/>). One scalar
    /// tolerance, full physics — moon phase, airmass attenuation, target altitude and
    /// separation all enter through the K-S model rather than through shape parameters.
    /// </para>
    /// <para>
    /// The profile deliberately carries <b>no bandwidth</b> (the continuum bandwidth scale
    /// cancels exactly in the Δ) and <b>no site fields</b> — the gate derives
    /// <c>v0Mag</c> and band extinction from the <see cref="Astronomy.Core.Locations.Location"/>
    /// in scope (<see cref="Astronomy.Core.Brightness.Bortle.DefaultZenithMag"/> +
    /// <see cref="Astronomy.Core.Brightness.SkyBrightness.ScaleK"/>). What distinguishes a
    /// narrowband from a broadband policy is the <i>tolerance</i>: an emission-line target's
    /// signal doesn't scale with sky continuum, so narrowband can afford a far larger sky
    /// brightening before SNR suffers.
    /// </para>
    /// <para>
    /// Mirrors the immutable POCO pattern of <see cref="Astronomy.Core.Targets.Target"/> /
    /// <see cref="Astronomy.Core.Locations.Location"/>: read-only properties, mutations via
    /// <see cref="With"/>. <see cref="Disabled"/> short-circuits all decisions to
    /// "not rejected" via the <see cref="Enabled"/> flag.
    /// </para>
    /// </remarks>
    public sealed class MoonLimitProfile
    {
        /// <summary>
        /// Master switch. <see langword="false"/> means the moon gate is off; downstream
        /// methods short-circuit to "accept everything".
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Maximum acceptable moon-driven sky brightening at the target, in mag/arcsec²
        /// over the moonless baseline. Rough integration-cost intuition for
        /// background-limited imaging: 0.3 mag ≈ 1.3× sky, 0.75 mag ≈ 2× sky,
        /// 1.0 mag ≈ 2.5× sky.
        /// </summary>
        public double ToleranceMag { get; }

        /// <summary>
        /// Filter center wavelength (nm). Drives the extinction wavelength scaling
        /// (<see cref="Astronomy.Core.Brightness.SkyBrightness.ScaleK"/> from the site's
        /// k₅₀₀) and the twilight Rayleigh scaling inside the K-S evaluation.
        /// </summary>
        public double CenterNm { get; }

        /// <summary>Constructs a fully-specified <see cref="MoonLimitProfile"/>.</summary>
        public MoonLimitProfile(bool enabled, double toleranceMag, double centerNm)
        {
            Enabled      = enabled;
            ToleranceMag = toleranceMag;
            CenterNm     = centerNm;
        }

        /// <summary>
        /// Named-argument builder. Any omitted argument inherits from the current instance.
        /// </summary>
        public MoonLimitProfile With(
            bool?   enabled = null,
            double? toleranceMag = null,
            double? centerNm = null)
            => new MoonLimitProfile(
                enabled      ?? this.Enabled,
                toleranceMag ?? this.ToleranceMag,
                centerNm     ?? this.CenterNm);

        /// <summary>
        /// Gate disabled. Curve consumers treat it as "no moon gate" and behave exactly
        /// as if no profile were supplied. Singleton — MoonLimitProfile is immutable.
        /// </summary>
        public static readonly MoonLimitProfile Disabled = new MoonLimitProfile(
            enabled: false, toleranceMag: 0.0, centerNm: Brightness.SkyBrightness.VBandCenterNm);

        /// <summary>
        /// Narrowband-imaging default: tolerance 1.0 mag (sky ×2.5) at Hα center 656 nm.
        /// Anchored to the classic Lorentzian (60°/7d) boundary's <b>cycle-median</b> Δmag
        /// at the Bortle-5 reference site (calibration 2026-07-24): the old rule's implied
        /// tolerance wobbled from ~0.15 (crescent) to ~1.6 (full moon), so a single
        /// physical tolerance is deliberately stricter than the classic rule near full
        /// moon and more permissive at crescent. Singleton.
        /// </summary>
        public static readonly MoonLimitProfile Narrowband = new MoonLimitProfile(
            enabled: true, toleranceMag: 1.0, centerNm: 656.0);

        /// <summary>
        /// Broadband-imaging default: tolerance 0.30 mag (sky ×1.32) at V-band center.
        /// Anchored to the classic Lorentzian (120°/14d) boundary's <b>cycle-median</b>
        /// Δmag (calibration 2026-07-24): the old rule's implied tolerance ran ~0.06
        /// (crescent) to ~2.0 (full moon ≈ 6× integration cost — unphysical for
        /// broadband), so this gate is deliberately much stricter near full moon.
        /// Singleton.
        /// </summary>
        public static readonly MoonLimitProfile Broadband = new MoonLimitProfile(
            enabled: true, toleranceMag: 0.30, centerNm: Brightness.SkyBrightness.VBandCenterNm);

        /// <summary>Custom-tuned profile (enabled).</summary>
        public static MoonLimitProfile Custom(double toleranceMag, double centerNm)
            => new MoonLimitProfile(enabled: true, toleranceMag: toleranceMag, centerNm: centerNm);
    }
}
