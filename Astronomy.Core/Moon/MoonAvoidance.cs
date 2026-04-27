using System;

namespace Astronomy.Core.Moon
{
    /// <summary>
    /// Immutable parameters for the ACP/Target-Scheduler-style moon-avoidance Lorentzian.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carries the two core Lorentzian parameters (<see cref="SeparationDeg"/>,
    /// <see cref="WidthDays"/>) plus the TS relaxation-zone extension
    /// (<see cref="RelaxEnabled"/> and the three altitude bounds). Mirrors the immutable
    /// POCO pattern of <see cref="Astronomy.Core.Targets.Target"/> /
    /// <see cref="Astronomy.Core.Locations.Location"/>: every property is read-only;
    /// mutations produce a new instance via <see cref="With"/>.
    /// </para>
    /// <para>
    /// <see cref="Disabled"/> short-circuits all decisions to "not rejected" via the
    /// <see cref="Enabled"/> flag.
    /// </para>
    /// </remarks>
    public sealed class MoonAvoidanceProfile
    {
        /// <summary>
        /// Master switch. <see langword="false"/> means moon avoidance is off; downstream
        /// methods short-circuit to "accept everything".
        /// </summary>
        public bool   Enabled        { get; }

        /// <summary>
        /// Required target-moon separation at full moon, in degrees. The Lorentzian
        /// "distance" parameter (per ACP). Common values: 60° narrowband, 120° broadband.
        /// </summary>
        public double SeparationDeg  { get; }

        /// <summary>
        /// Width parameter of the Lorentzian, in days. The number of days off full moon at
        /// which the required separation drops to <see cref="SeparationDeg"/>/2. Common
        /// values: 7d narrowband, 14d broadband.
        /// </summary>
        public double WidthDays      { get; }

        /// <summary>
        /// When <see langword="true"/>, the relaxation-zone extension is applied: required
        /// separation and width are linearly ramped down as the moon approaches the
        /// horizon, and avoidance turns off entirely below <see cref="RelaxMinAltDeg"/>.
        /// When <see langword="false"/>, moon altitude is irrelevant; the plain Lorentzian
        /// applies always.
        /// </summary>
        public bool   RelaxEnabled   { get; }

        /// <summary>
        /// Lower altitude bound (degrees) of the relaxation zone. When relaxation is
        /// enabled and moon altitude is at or below this, avoidance is off entirely
        /// (<see cref="MoonAvoidance.IsRejected"/> returns <see langword="false"/>).
        /// </summary>
        public double RelaxMinAltDeg { get; }

        /// <summary>
        /// Upper altitude bound (degrees) of the relaxation zone. When moon altitude is
        /// above this, the full Lorentzian applies. Below it (but above
        /// <see cref="RelaxMinAltDeg"/>) and with <see cref="RelaxScale"/> &gt; 0,
        /// distance and width ramp linearly.
        /// </summary>
        public double RelaxMaxAltDeg { get; }

        /// <summary>
        /// Coefficient (degrees of separation per degree of moon altitude below
        /// <see cref="RelaxMaxAltDeg"/>) controlling how aggressively distance and width
        /// ramp inside the relaxation zone. When zero, the ramps don't apply (matching
        /// TS's <c>MoonRelaxScale &gt; 0</c> gate); the relaxation zone still flips
        /// avoidance off below <see cref="RelaxMinAltDeg"/>.
        /// </summary>
        public double RelaxScale     { get; }

        /// <summary>
        /// Constructs a fully-specified <see cref="MoonAvoidanceProfile"/>.
        /// </summary>
        public MoonAvoidanceProfile(
            bool   enabled,
            double separationDeg,
            double widthDays,
            bool   relaxEnabled,
            double relaxMinAltDeg,
            double relaxMaxAltDeg,
            double relaxScale)
        {
            Enabled        = enabled;
            SeparationDeg  = separationDeg;
            WidthDays      = widthDays;
            RelaxEnabled   = relaxEnabled;
            RelaxMinAltDeg = relaxMinAltDeg;
            RelaxMaxAltDeg = relaxMaxAltDeg;
            RelaxScale     = relaxScale;
        }

        /// <summary>
        /// Named-argument builder. Any omitted argument inherits from the current instance.
        /// </summary>
        public MoonAvoidanceProfile With(
            bool?   enabled = null,
            double? separationDeg = null,
            double? widthDays = null,
            bool?   relaxEnabled = null,
            double? relaxMinAltDeg = null,
            double? relaxMaxAltDeg = null,
            double? relaxScale = null)
            => new MoonAvoidanceProfile(
                enabled        ?? this.Enabled,
                separationDeg  ?? this.SeparationDeg,
                widthDays      ?? this.WidthDays,
                relaxEnabled   ?? this.RelaxEnabled,
                relaxMinAltDeg ?? this.RelaxMinAltDeg,
                relaxMaxAltDeg ?? this.RelaxMaxAltDeg,
                relaxScale     ?? this.RelaxScale);

        /// <summary>
        /// Avoidance disabled. <see cref="MoonAvoidance.IsRejected"/> always returns
        /// <see langword="false"/> with this profile; the curve consumers treat it as
        /// "no moon avoidance" and behave exactly as if no profile were supplied.
        /// </summary>
        public static MoonAvoidanceProfile Disabled => new MoonAvoidanceProfile(
            enabled:        false,
            separationDeg:  0.0,
            widthDays:      0.0,
            relaxEnabled:   false,
            relaxMinAltDeg: -15.0,
            relaxMaxAltDeg: 5.0,
            relaxScale:     0.0);

        /// <summary>
        /// Narrowband-imaging defaults: 60° required separation at full moon, 7d width.
        /// Relaxation off; turn it on by composing <c>.With(relaxEnabled: true, relaxScale: …)</c>.
        /// </summary>
        public static MoonAvoidanceProfile Narrowband => new MoonAvoidanceProfile(
            enabled:        true,
            separationDeg:  60.0,
            widthDays:      7.0,
            relaxEnabled:   false,
            relaxMinAltDeg: -15.0,
            relaxMaxAltDeg: 5.0,
            relaxScale:     0.0);

        /// <summary>
        /// Broadband-imaging defaults: 120° required separation at full moon, 14d width.
        /// Relaxation off.
        /// </summary>
        public static MoonAvoidanceProfile Broadband => new MoonAvoidanceProfile(
            enabled:        true,
            separationDeg:  120.0,
            widthDays:      14.0,
            relaxEnabled:   false,
            relaxMinAltDeg: -15.0,
            relaxMaxAltDeg: 5.0,
            relaxScale:     0.0);

        /// <summary>
        /// Custom-tuned profile. Relaxation defaults to off with TS-standard
        /// <c>(-15°, +5°)</c> bounds; pass <c>relaxEnabled: true</c> and a positive
        /// <c>relaxScale</c> to opt in.
        /// </summary>
        public static MoonAvoidanceProfile Custom(
            double separationDeg, double widthDays,
            bool   relaxEnabled = false,
            double relaxMinAltDeg = -15.0,
            double relaxMaxAltDeg = 5.0,
            double relaxScale = 0.0)
            => new MoonAvoidanceProfile(
                enabled:        true,
                separationDeg:  separationDeg,
                widthDays:      widthDays,
                relaxEnabled:   relaxEnabled,
                relaxMinAltDeg: relaxMinAltDeg,
                relaxMaxAltDeg: relaxMaxAltDeg,
                relaxScale:     relaxScale);
    }

    /// <summary>
    /// ACP/Target-Scheduler-style moon-avoidance Lorentzian: per-moment accept/reject
    /// driven by lunar age, target-moon separation, and (optionally) lunar altitude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors the BAIT/ACP formula popularized by NINA Target Scheduler
    /// (<c>AstrometryUtils.GetMoonAvoidanceLorentzianSeparation</c>) plus the optional
    /// altitude-relaxation extension from <c>MoonAvoidanceExpert</c>. The MoonDown
    /// override is intentionally omitted -- the project's hybrid curve-model carries that
    /// signal via curve-presence rather than a hard absolute reject.
    /// </para>
    /// </remarks>
    public static class MoonAvoidance
    {
        /// <summary>Days in one synodic lunar cycle (matches TS's <c>DAYS_IN_LUNAR_CYCLE</c>).</summary>
        public const double DaysInLunarCycle = 29.5305882;

        /// <summary>
        /// Required target-moon separation (degrees) under the ACP/TS Lorentzian at the
        /// given lunar age. Single source of truth for the formula.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>required = distance / (1 + ((0.5 − age/29.53) / (width/29.53))²)</c>.
        /// At full moon (age ≈ 14.77) returns <paramref name="distanceDeg"/>; at
        /// ±<paramref name="widthDays"/> off full, returns <paramref name="distanceDeg"/>/2;
        /// at new moon (age ≈ 0 or 29.53) collapses to a small fraction of
        /// <paramref name="distanceDeg"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="widthDays"/> is zero (would divide by zero).
        /// </exception>
        public static double LorentzianRequiredSep(
            double moonAgeDays, double distanceDeg, double widthDays)
        {
            if (widthDays == 0.0)
                throw new ArgumentException("widthDays must be non-zero", nameof(widthDays));

            double a = (0.5 - (moonAgeDays / DaysInLunarCycle)) / (widthDays / DaysInLunarCycle);
            return distanceDeg / (1.0 + a * a);
        }

        /// <summary>
        /// Required separation under <paramref name="profile"/> after relaxation-zone
        /// adjustments. Returns <c>0.0</c> when the profile disables avoidance, when
        /// relaxation has clamped distance or width to non-positive, or when moon
        /// altitude is below <see cref="MoonAvoidanceProfile.RelaxMinAltDeg"/> with
        /// relaxation enabled.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
        public static double RequiredSepWithRelax(
            double moonAgeDays, double moonAltDeg, MoonAvoidanceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!profile.Enabled) return 0.0;

            double distance = profile.SeparationDeg;
            double width    = profile.WidthDays;

            if (profile.RelaxEnabled)
            {
                // Below the floor: avoidance off entirely. Gated only on altitude --
                // matches TS's `if (moonAltitude <= MoonRelaxMinAltitude) return false;`.
                if (moonAltDeg <= profile.RelaxMinAltDeg) return 0.0;

                // Inside the zone with RelaxScale > 0: apply both ramps (TS rule). With
                // RelaxScale = 0, no ramps -- the user enabled relaxation only to get the
                // floor-cuts-off behavior, and intends the plain Lorentzian above the floor.
                if (moonAltDeg <= profile.RelaxMaxAltDeg && profile.RelaxScale > 0.0)
                {
                    distance += profile.RelaxScale * (moonAltDeg - profile.RelaxMaxAltDeg);

                    double range = profile.RelaxMaxAltDeg - profile.RelaxMinAltDeg;
                    if (range > 0.0)
                        width *= (moonAltDeg - profile.RelaxMinAltDeg) / range;
                }
            }

            if (distance <= 0.0) return 0.0;
            if (width    <= 0.0) return 0.0;

            return LorentzianRequiredSep(moonAgeDays, distance, width);
        }

        /// <summary>
        /// True if <paramref name="profile"/> rejects this moment given the
        /// target-moon separation, lunar age, and lunar altitude. Short-circuits to
        /// <see langword="false"/> when avoidance is disabled or the relaxation-adjusted
        /// threshold collapses to zero.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
        public static bool IsRejected(
            double actualSepDeg, double moonAgeDays, double moonAltDeg,
            MoonAvoidanceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!profile.Enabled) return false;

            double required = RequiredSepWithRelax(moonAgeDays, moonAltDeg, profile);
            if (required <= 0.0) return false;

            return actualSepDeg < required;
        }
    }
}
