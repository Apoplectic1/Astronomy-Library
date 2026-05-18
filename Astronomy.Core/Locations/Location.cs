using System;
using Astronomy.Core.Horizons;

namespace Astronomy.Core.Locations
{
    /// <summary>
    /// Immutable observer site: latitude / longitude in the Core magnitude-plus-flag
    /// convention, site-fixed time zone, atmospheric conditions, and per-azimuth
    /// terrain horizon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Site" is the operative framing: <see cref="Location"/> carries everything tied to
    /// a specific lat/lon position. Per-session inputs (observation moment, user planning
    /// preferences) live on the consumer side -- see
    /// <c>Astronomy.Core.Time.ObservationMoment</c> for the moment carrier, and consumer-
    /// specific records for planning preferences. Every property is read-only; mutations
    /// produce a new instance via <see cref="With"/>.
    /// </para>
    /// <para>
    /// <b>Hemisphere convention.</b> <see cref="Latitude"/> and <see cref="Longitude"/>
    /// are stored as non-negative magnitudes, with direction carried by the
    /// <see cref="North"/> / <see cref="West"/> bool flags. A negative magnitude passed
    /// to the constructor is normalized (flipped to positive) and the corresponding
    /// hemisphere flag is inverted, so <c>new Location(..., latitude: -40, north: true,
    /// ...)</c> produces <c>{ Latitude = 40, North = false }</c> -- the sign takes
    /// precedence over the flag.
    /// </para>
    /// <para>
    /// D/M/S derivations (<see cref="LatDegrees"/> / <see cref="LatMinutes"/> /
    /// <see cref="LatSeconds"/> and the Longitude equivalents) are computed on read
    /// instead of stored as fields -- no possibility of the derived values falling out of
    /// sync with the decimal value.
    /// </para>
    /// </remarks>
    public sealed class Location
    {
        /// <summary>Human-readable label for the location. Defaults to "Custom".</summary>
        public string         Name         { get; }

        /// <summary>Latitude magnitude in decimal degrees, non-negative. Hemisphere lives in <see cref="North"/>.</summary>
        public double         Latitude     { get; }

        /// <summary><see langword="true"/> for Northern hemisphere, <see langword="false"/> for Southern.</summary>
        public bool           North        { get; }

        /// <summary>Longitude magnitude in decimal degrees, non-negative. Hemisphere lives in <see cref="West"/>.</summary>
        public double         Longitude    { get; }

        /// <summary><see langword="true"/> for Western hemisphere (negative signed longitude), <see langword="false"/> for Eastern.</summary>
        public bool           West         { get; }

        /// <summary>Time zone the observer is in. Defaults to <see cref="TimeZoneInfo.Local"/>.</summary>
        public TimeZoneInfo   TimeZoneInfo { get; }

        /// <summary>
        /// Observer elevation above the geoid, meters. Defaults to 0 when omitted. Used by
        /// <c>ObserverInfo</c> for moon parallax (Meeus 11.1 / 11.2) and for elevation-
        /// corrected horizon dip in sun rise/set computations.
        /// </summary>
        public double         Elevation    { get; }

        /// <summary>
        /// Bortle dark-sky class for this site (1 = excellent dark, 9 = inner-city).
        /// Used by the K-S sky-brightness model to derive the moonless zenith
        /// brightness V₀ via <see cref="Astronomy.Core.Brightness.Bortle.DefaultZenithMag"/>.
        /// Defaults to 5 (suburban) when omitted.
        /// </summary>
        public int            BortleClass  { get; }

        /// <summary>
        /// Atmospheric extinction coefficient k at 500 nm (mag/airmass) for this site.
        /// Wavelength scaling for other bands is applied externally via
        /// <see cref="Astronomy.Core.Brightness.SkyBrightness.ScaleK"/>. Defaults to
        /// 0.28 (typical Bortle-5 sea-level) when omitted.
        /// </summary>
        public double         ExtinctionK  { get; }

        /// <summary>
        /// Per-azimuth terrain horizon at this site. Defaults to
        /// <see cref="ScalarHorizonProfile"/> at 0&#176; (no terrain blocking) when
        /// omitted. Polyline / obstruction-table profiles model real-world site horizons
        /// loaded from NINA <c>.hrz</c> files or equivalent.
        /// </summary>
        public IHorizonProfile LocalHorizon { get; }

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

        /// <summary>
        /// Constructs a fully-specified <see cref="Location"/>. Negative
        /// <paramref name="latitude"/> or <paramref name="longitude"/> flip the hemisphere
        /// flag (<paramref name="north"/> / <paramref name="west"/>) and are stored as
        /// positive magnitudes. A <see langword="null"/> <paramref name="timeZoneInfo"/>
        /// defaults to <see cref="TimeZoneInfo.Local"/>; a <see langword="null"/>
        /// <paramref name="localHorizon"/> defaults to a flat <see cref="ScalarHorizonProfile"/>
        /// at 0&#176;; a <see langword="null"/> <paramref name="name"/> defaults to "Custom".
        /// </summary>
        public Location(
            string?          name,
            double           latitude, bool north,
            double           longitude, bool west,
            TimeZoneInfo?    timeZoneInfo,
            IHorizonProfile? localHorizon = null,
            double           elevation   = 0.0,
            int              bortleClass = 5,
            double           extinctionK = 0.28)
        {
            // Sign normalization: negative magnitude flips the hemisphere flag so the stored
            // state is always (non-negative magnitude, explicit hemisphere).
            if (latitude  < 0) { latitude  = -latitude;  north = !north; }
            if (longitude < 0) { longitude = -longitude; west  = !west;  }

            Name         = name ?? "Custom";
            Latitude     = latitude;
            North        = north;
            Longitude    = longitude;
            West         = west;
            TimeZoneInfo = timeZoneInfo ?? TimeZoneInfo.Local;
            LocalHorizon = localHorizon ?? new ScalarHorizonProfile(0.0);
            Elevation    = elevation;
            BortleClass  = bortleClass;
            ExtinctionK  = extinctionK;
        }

        /// <summary>
        /// Named-argument builder. Callers pass only the fields they want to change:
        /// <c>mLocation = mLocation.With(elevation: 250.0)</c> or
        /// <c>mLocation = mLocation.With(latitude: 40.3, north: true)</c>.
        /// Any omitted argument inherits from the current instance.
        /// </summary>
        public Location With(
            string?          name         = null,
            double?          latitude     = null, bool? north = null,
            double?          longitude    = null, bool? west  = null,
            TimeZoneInfo?    timeZoneInfo = null,
            IHorizonProfile? localHorizon = null,
            double?          elevation    = null,
            int?             bortleClass  = null,
            double?          extinctionK  = null)
            => new Location(
                name         ?? this.Name,
                latitude     ?? this.Latitude,
                north        ?? this.North,
                longitude    ?? this.Longitude,
                west         ?? this.West,
                timeZoneInfo ?? this.TimeZoneInfo,
                localHorizon ?? this.LocalHorizon,
                elevation    ?? this.Elevation,
                bortleClass  ?? this.BortleClass,
                extinctionK  ?? this.ExtinctionK);

        /// <summary>
        /// Neutral, ship-safe placeholder values, freshly instantiated on each access.
        /// </summary>
        /// <remarks>
        /// Coordinates are deliberately rounded (40&#176;N, 75&#176;W, sea level) so the
        /// public Library source contains no author-specific values. Consumer apps
        /// resolve the user's actual site via their own configuration layers (e.g.
        /// TargetPlanner's <c>PersonalDefaults</c> + <c>SettingsStore</c>).
        /// </remarks>
        public static Location Default => new Location(
            name:         "Custom",
            latitude:     40.0, north: true,
            longitude:    75.0, west:  true,
            timeZoneInfo: TimeZoneInfo.Local,
            localHorizon: new ScalarHorizonProfile(0.0),
            elevation:    0.0,
            bortleClass:  5,
            extinctionK:  0.28);
    }
}
