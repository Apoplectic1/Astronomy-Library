namespace Astronomy.Core.Session
{
    /// <summary>
    /// A single per-minute observation of a stellar target's apparent sky position
    /// at a fixed observer. Returned by <c>AltitudeCurve.Sample</c> and
    /// memoized by per-target trajectory caches in downstream consumers (TP's
    /// <c>mDayAxis</c>, IS's Tier 3 bitmap precompute).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both refracted (apparent) and geometric altitude are carried so callers
    /// can pick the convention that matches their use:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="AltDegGeometric"/> is the unrefracted altitude — what
    /// session-placement math, moon-clear gates, and horizon polylines consume.</item>
    /// <item><see cref="AltDegApparent"/> includes Saemundsson refraction — what
    /// brightness models (K-S sky brightness) and visual displays consume.</item>
    /// </list>
    /// <para>
    /// Azimuth is measured from North clockwise (N=0°, E=90°, S=180°, W=270°),
    /// matching the NINA public-API convention.
    /// </para>
    /// </remarks>
    public readonly struct AltAzSample
    {
        /// <summary>Unrefracted (geometric) altitude in degrees above the horizon.</summary>
        public double AltDegGeometric { get; init; }

        /// <summary>Saemundsson-refracted (apparent) altitude in degrees above the horizon.</summary>
        public double AltDegApparent { get; init; }

        /// <summary>Azimuth in degrees, measured clockwise from North in [0, 360).</summary>
        public double AzDeg { get; init; }
    }
}
