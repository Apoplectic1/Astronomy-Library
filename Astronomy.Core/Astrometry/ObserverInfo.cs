namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Geographic observer location used as input to <see cref="AstroUtil"/> calls.
    /// Mirrors NINA's <c>NINA.Astrometry.ObserverInfo</c> shape so port code is
    /// drop-in interchangeable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Latitude in <c>[-90, +90]</c>; positive north. Longitude in <c>[-180, +180]</c>;
    /// positive east (so a western-hemisphere site is negative). Elevation in meters
    /// above the geoid. The CoordinateSharp-era code path used a <c>(lat, north,
    /// lon, west)</c> sign-flag pattern; the AstroUtil layer takes the conventional
    /// signed form. <see cref="Astronomy.Core.Locations.Location"/> resolves its
    /// hemisphere flags into signed degrees at the call site (canonical idiom in
    /// <c>AltAzCalculator.At</c>).
    /// </para>
    /// <para>
    /// <b>Value type.</b> Stored as a 24-byte <see langword="readonly"/> <see langword="struct"/>
    /// (three doubles) so passing it into AstroUtil hot paths does not allocate.
    /// Convertor's prior <c>sealed class</c> shape was the dominant per-call allocation
    /// in a caller's cache-prepare loop; conversion is byte-equivalent for callers because
    /// the type was already immutable and identity-free.
    /// </para>
    /// </remarks>
    public readonly struct ObserverInfo
    {
        /// <summary>Latitude, decimal degrees, positive north. Range [-90, +90].</summary>
        public double Latitude { get; }

        /// <summary>Longitude, decimal degrees, positive east. Range [-180, +180].</summary>
        public double Longitude { get; }

        /// <summary>Elevation above geoid, meters.</summary>
        public double Elevation { get; }

        /// <summary>
        /// Constructs an observer at <paramref name="latitude"/> /
        /// <paramref name="longitude"/> (signed degrees, positive north / east) and
        /// <paramref name="elevation"/> meters above the geoid. Inputs are stored
        /// verbatim; no range coercion or sign-flag resolution.
        /// </summary>
        public ObserverInfo(double latitude, double longitude, double elevation = 0.0)
        {
            Latitude  = latitude;
            Longitude = longitude;
            Elevation = elevation;
        }
    }
}
