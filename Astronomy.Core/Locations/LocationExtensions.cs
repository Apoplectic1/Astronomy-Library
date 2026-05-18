using System.Runtime.CompilerServices;

namespace Astronomy.Core.Locations
{
    /// <summary>
    /// Internal extension helpers that fold the magnitude-plus-flag convention into the
    /// signed-degrees / east-positive-longitude shape that the geometry layer consumes.
    /// </summary>
    /// <remarks>
    /// Replaces the per-callsite <c>location.North ? location.Latitude : -location.Latitude</c>
    /// / <c>location.West ? -location.Longitude : location.Longitude</c> preamble that used to
    /// repeat across the geometry callers; the convention is now enforced by the extension
    /// instead of by reviewer vigilance. <see cref="MethodImplOptions.AggressiveInlining"/>
    /// keeps hot loops (<c>AltitudeCurve.Sample</c>, <c>IntegratedQuality.OverSession</c>, ...)
    /// at parity with the inline form.
    /// </remarks>
    internal static class LocationExtensions
    {
        /// <summary>
        /// Signed latitude (positive = north) and east-positive longitude in decimal
        /// degrees for <paramref name="loc"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (double LatSigned, double LonEastDeg) AsSignedDegrees(this Location loc)
            => (loc.North ? loc.Latitude  : -loc.Latitude,
                loc.West  ? -loc.Longitude : loc.Longitude);
    }
}
