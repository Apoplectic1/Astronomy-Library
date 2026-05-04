using System;

namespace Astronomy.Core.Locations
{
    /// <summary>
    /// Immutable observer location: latitude / longitude in the Core magnitude-plus-flag
    /// convention, local moment, and analysis preferences (horizon / minimum duration above
    /// horizon) that drive consumer-side altitude / visibility analysis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every property is read-only; mutations produce a new instance via <see cref="With"/>.
    /// Construction takes a full parameter set so every caller is explicit about what it
    /// means; use <see cref="Default"/> for the Penns Park defaults.
    /// </para>
    /// <para>
    /// <b>Hemisphere convention.</b> <see cref="Latitude"/> and <see cref="Longitude"/> are
    /// stored as non-negative magnitudes, with direction carried by the <see cref="North"/>
    /// / <see cref="West"/> bool flags. A negative magnitude passed to the constructor is
    /// normalized (flipped to positive) and the corresponding hemisphere flag is inverted,
    /// so <c>new Location(..., latitude: -40, north: true, ...)</c> produces
    /// <c>{ Latitude = 40, North = false }</c> -- the sign takes precedence over the flag.
    /// </para>
    /// <para>
    /// D/M/S derivations (<see cref="LatDegrees"/> / <see cref="LatMinutes"/> /
    /// <see cref="LatSeconds"/> and the Longitude equivalents) are computed on read instead
    /// of stored as fields -- no possibility of the derived values falling out of sync with
    /// the decimal value.
    /// </para>
    /// </remarks>
    public sealed class Location
    {
        /// <summary>Human-readable label for the location (e.g. "Penns Park"). Defaults to "Custom".</summary>
        public string        Name         { get; }

        /// <summary>Latitude magnitude in decimal degrees, non-negative. Hemisphere lives in <see cref="North"/>.</summary>
        public double        Latitude     { get; }

        /// <summary><see langword="true"/> for Northern hemisphere, <see langword="false"/> for Southern.</summary>
        public bool          North        { get; }

        /// <summary>Longitude magnitude in decimal degrees, non-negative. Hemisphere lives in <see cref="West"/>.</summary>
        public double        Longitude    { get; }

        /// <summary><see langword="true"/> for Western hemisphere (negative signed longitude), <see langword="false"/> for Eastern.</summary>
        public bool          West         { get; }

        /// <summary>
        /// Minimum altitude (degrees above mathematical horizon) that counts as "above the
        /// horizon" for scheduling math. A flat-horizon scalar -- for azimuth-varying
        /// profiles, see <c>Astronomy.Core.Horizons.IHorizonProfile</c>.
        /// </summary>
        public double        Horizon      { get; }

        /// <summary>Minimum time above <see cref="Horizon"/> required for a target to qualify as observable.</summary>
        public TimeSpan      Duration     { get; }

        /// <summary>
        /// The moment the caller wants to evaluate. Kind is caller-owned: <c>Local</c> and
        /// <c>Unspecified</c> are both treated as local by downstream conversions (e.g.
        /// <see cref="AltAzCalculator.Of"/>); <c>Utc</c> no-ops through any
        /// <c>.ToUniversalTime()</c>.
        /// </summary>
        public DateTime      DateTime     { get; }

        /// <summary>Time zone the observer is in. Defaults to <see cref="TimeZoneInfo.Local"/>.</summary>
        public TimeZoneInfo  TimeZoneInfo { get; }

        /// <summary>
        /// Observer elevation above the geoid, meters. Defaults to 0 when omitted. Used by
        /// <c>ObserverInfo</c> for moon parallax (Meeus 11.1 / 11.2) and -- once wired --
        /// for elevation-corrected horizon dip in sun rise/set computations.
        /// </summary>
        public double        Elevation    { get; }

        /// <summary>
        /// Bortle dark-sky class for this site (1 = excellent dark, 9 = inner-city).
        /// Used by the K-S sky-brightness model to derive the moonless zenith
        /// brightness V₀ via <see cref="Astronomy.Core.Brightness.Bortle.DefaultZenithMag"/>.
        /// Defaults to 5 (suburban) when omitted.
        /// </summary>
        public int           BortleClass  { get; }

        /// <summary>
        /// Atmospheric extinction coefficient k at 500 nm (mag/airmass) for this site.
        /// Wavelength scaling for other bands is applied externally via
        /// <see cref="Astronomy.Core.Brightness.SkyBrightness.ScaleK"/>. Defaults to
        /// 0.28 (typical Bortle-5 sea-level) when omitted.
        /// </summary>
        public double        ExtinctionK  { get; }

        /// <summary>Sexagesimal latitude components -- whole-degrees digit of the DMS breakdown (always non-negative; hemisphere in <see cref="North"/>).</summary>
        public double LatDegrees => Math.Truncate(Latitude);
        /// <summary>Whole-arcminutes component of the latitude DMS breakdown.</summary>
        public double LatMinutes => Math.Floor(60.0 * (Latitude - LatDegrees));
        /// <summary>Fractional-arcseconds component of the latitude DMS breakdown.</summary>
        public double LatSeconds => 3600.0 * (Latitude - LatDegrees - LatMinutes / 60.0);

        /// <summary>Sexagesimal longitude components -- whole-degrees digit (non-negative; direction in <see cref="West"/>).</summary>
        public double LonDegrees => Math.Truncate(Longitude);
        /// <summary>Whole-arcminutes component of the longitude DMS breakdown.</summary>
        public double LonMinutes => Math.Floor(60.0 * (Longitude - LonDegrees));
        /// <summary>Fractional-arcseconds component of the longitude DMS breakdown.</summary>
        public double LonSeconds => 3600.0 * (Longitude - LonDegrees - LonMinutes / 60.0);

        /// <summary>Convenience accessor for <see cref="Duration"/> in minutes.</summary>
        public double MinutesAboveHorizon => Duration.TotalMinutes;

        /// <summary>
        /// Constructs a fully-specified <see cref="Location"/>. Negative
        /// <paramref name="latitude"/> or <paramref name="longitude"/> flip the hemisphere
        /// flag (<paramref name="north"/> / <paramref name="west"/>) and are stored as
        /// positive magnitudes. A <see langword="null"/> <paramref name="timeZoneInfo"/>
        /// defaults to <see cref="TimeZoneInfo.Local"/>; a <see langword="null"/>
        /// <paramref name="name"/> defaults to "Custom".
        /// </summary>
        public Location(
            string name,
            double latitude, bool north,
            double longitude, bool west,
            double horizon,
            TimeSpan duration,
            DateTime dateTime,
            TimeZoneInfo timeZoneInfo,
            double elevation = 0.0,
            int    bortleClass = 5,
            double extinctionK = 0.28)
        {
            // Sign normalization: negative magnitude flips the hemisphere flag so the stored
            // state is always (non-negative magnitude, explicit hemisphere).
            if (latitude < 0) { latitude = -latitude; north = !north; }
            if (longitude < 0) { longitude = -longitude; west = !west; }

            Name         = name ?? "Custom";
            Latitude     = latitude;
            North        = north;
            Longitude    = longitude;
            West         = west;
            Horizon      = horizon;
            Duration     = duration;
            DateTime     = dateTime;
            TimeZoneInfo = timeZoneInfo ?? TimeZoneInfo.Local;
            Elevation    = elevation;
            BortleClass  = bortleClass;
            ExtinctionK  = extinctionK;
        }

        /// <summary>
        /// Named-argument builder. Callers pass only the fields they want to change:
        /// <c>mLocation = mLocation.With(horizon: 35.0)</c> or
        /// <c>mLocation = mLocation.With(latitude: 40.3, north: true)</c>.
        /// Any omitted argument inherits from the current instance.
        /// </summary>
        public Location With(
            string name = null,
            double? latitude = null, bool? north = null,
            double? longitude = null, bool? west = null,
            double? horizon = null,
            TimeSpan? duration = null,
            DateTime? dateTime = null,
            TimeZoneInfo timeZoneInfo = null,
            double? elevation = null,
            int?    bortleClass = null,
            double? extinctionK = null)
            => new Location(
                name         ?? this.Name,
                latitude     ?? this.Latitude,
                north        ?? this.North,
                longitude    ?? this.Longitude,
                west         ?? this.West,
                horizon      ?? this.Horizon,
                duration     ?? this.Duration,
                dateTime     ?? this.DateTime,
                timeZoneInfo ?? this.TimeZoneInfo,
                elevation    ?? this.Elevation,
                bortleClass  ?? this.BortleClass,
                extinctionK  ?? this.ExtinctionK);

        /// <summary>
        /// Penns Park defaults, freshly instantiated on each access.
        /// </summary>
        /// <remarks>
        /// <see cref="DateTime"/> is <see cref="System.DateTime.Now"/> at the moment of the
        /// property read -- intentional for interactive callers (TargetPlanner / future
        /// scheduler UIs that boot at "now"), but <b>nondeterministic</b> for unit tests
        /// and pure library consumers. Such callers must override the moment explicitly,
        /// e.g. <c>Location.Default.With(dateTime: new DateTime(2026, 11, 15, 0, 0, 0,
        /// DateTimeKind.Utc))</c> -- the existing pattern in
        /// <c>Astronomy.Core.Tests</c>'s <c>MakeLocation</c> helpers.
        /// </remarks>
        public static Location Default => new Location(
            name:         "Penns Park",
            latitude:     40.282835, north: true,
            longitude:    74.997369, west:  true,
            horizon:      30,
            duration:     TimeSpan.FromMinutes(240),
            dateTime:     DateTime.Now,
            timeZoneInfo: TimeZoneInfo.Local,
            elevation:    80.67,
            bortleClass:  5,
            extinctionK:  0.28);
    }
}
