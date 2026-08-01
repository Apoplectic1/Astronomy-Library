namespace Astronomy.Core.Moon
{
    /// <summary>
    /// A single per-minute observation of the Moon's state at a fixed observer.
    /// Returned by <c>MoonEphemeris.Sample</c> and memoized by per-night
    /// moon caches in downstream consumers (TP's <c>mMoonAxis</c>, IS's Tier 2
    /// nightly precompute). Target-independent — the same MoonSample sequence
    /// serves every target observed from the same site on the same night.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All positions are topocentric (parallax-corrected) — the geocentric Moon
    /// position differs by up to ~1° at typical observer elevations, which is
    /// non-negligible for moon-clear gate decisions and sky-brightness work.
    /// </para>
    /// <para>
    /// Both refracted (apparent) and geometric altitudes are carried:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="AltDegGeometric"/> — the raw geometric position (what
    /// <see cref="MoonSeparation.ObserveAt"/> also reports).</item>
    /// <item><see cref="AltDegApparent"/> — what the K-S evaluations
    /// (<see cref="Astronomy.Core.Brightness.SkyBrightness.KsAt"/> /
    /// <see cref="Astronomy.Core.Brightness.SkyBrightness.KsMoonDeltaMag"/>, and
    /// therefore the moon gate) consume (apparent-altitude convention).</item>
    /// </list>
    /// <para>
    /// <see cref="DistanceKm"/> enables downstream parallax-aware separation
    /// refinements when a consumer needs sub-arcminute angular accuracy beyond
    /// the AltAz pair alone.
    /// </para>
    /// <para>
    /// <see cref="AgeDays"/> is the cheap per-minute phase axis (see
    /// <see cref="LunarAge.DaysAt"/>);
    /// <see cref="PhaseAngleDeg"/> drives the K-S <c>iStar</c> term;
    /// <see cref="IlluminatedFrac"/> is for UI labels (and is geocentric — the
    /// topocentric correction is &lt; 0.0001 and intentionally not modeled).
    /// </para>
    /// </remarks>
    public readonly struct MoonSample
    {
        /// <summary>Topocentric unrefracted altitude (degrees).</summary>
        public double AltDegGeometric { get; init; }

        /// <summary>Topocentric Saemundsson-refracted altitude (degrees).</summary>
        public double AltDegApparent { get; init; }

        /// <summary>Topocentric azimuth (degrees, North=0, clockwise).</summary>
        public double AzDeg { get; init; }

        /// <summary>Topocentric distance to the Moon (kilometres).</summary>
        public double DistanceKm { get; init; }

        /// <summary>Synodic age (days since new moon), in [0, 29.5305888).</summary>
        public double AgeDays { get; init; }

        /// <summary>Krisciunas–Schaefer phase angle (degrees, 0 = full, 180 = new).</summary>
        public double PhaseAngleDeg { get; init; }

        /// <summary>Geocentric illuminated fraction of the lunar disc, [0, 1].</summary>
        public double IlluminatedFrac { get; init; }
    }
}
