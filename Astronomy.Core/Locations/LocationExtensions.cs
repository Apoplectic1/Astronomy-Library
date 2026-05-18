using System.Runtime.CompilerServices;

namespace Astronomy.Core.Locations
{
    /// <summary>
    /// Internal extension helpers that fold the magnitude-plus-flag convention into the
    /// signed-degrees / east-positive-longitude shape that the geometry layer consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the per-callsite <c>location.North ? location.Latitude : -location.Latitude</c>
    /// / <c>location.West ? -location.Longitude : location.Longitude</c> preamble that used to
    /// repeat across the geometry callers; the convention is now enforced by the extension
    /// instead of by reviewer vigilance. <see cref="MethodImplOptions.AggressiveInlining"/>
    /// keeps hot loops (<c>AltitudeCurve.Sample</c>, <c>IntegratedQuality.OverSession</c>, ...)
    /// at parity with the inline form.
    /// </para>
    /// <para>
    /// <b>When to apply:</b> at <em>multi-value</em> call sites that consume both
    /// <c>LatSigned</c> and <c>LonEastDeg</c> (typically also paired with a target-side
    /// <see cref="Astronomy.Core.Targets.TargetExtensions.AsSignedRaDec"/> call).
    /// Single-value sites that need only one of the two stay inline -- the discard form
    /// (<c>var (lat, _) = loc.AsSignedDegrees();</c>) is no shorter and no clearer than
    /// the original conditional, so the convention is "extension at multi-value sites,
    /// inline elsewhere."
    /// </para>
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
