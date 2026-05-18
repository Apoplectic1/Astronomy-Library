using System.Runtime.CompilerServices;

namespace Astronomy.Core.Targets
{
    /// <summary>
    /// Internal extension helpers that fold the magnitude-plus-flag declination
    /// convention into the signed-degrees shape that the geometry layer consumes,
    /// alongside the (already-canonical-on-read) decimal-hour right-ascension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the per-callsite <c>target.North ? target.Declination : -target.Declination</c>
    /// preamble; convention is now enforced by the extension instead of by reviewer
    /// vigilance. <see cref="MethodImplOptions.AggressiveInlining"/> keeps hot loops
    /// at parity with the inline form.
    /// </para>
    /// <para>
    /// <b>When to apply:</b> at <em>multi-value</em> call sites that consume both
    /// <c>DecSigned</c> and <c>RaHours</c> (typically alongside an
    /// <see cref="Astronomy.Core.Locations.LocationExtensions.AsSignedDegrees"/> call).
    /// Single-value sites that need only the signed declination stay inline; same
    /// convention as <see cref="Astronomy.Core.Locations.LocationExtensions"/>.
    /// </para>
    /// </remarks>
    internal static class TargetExtensions
    {
        /// <summary>
        /// Signed declination (positive = north) in decimal degrees and right
        /// ascension in decimal hours for <paramref name="tgt"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (double DecSigned, double RaHours) AsSignedRaDec(this Target tgt)
            => (tgt.North ? tgt.Declination : -tgt.Declination,
                tgt.RightAscension);
    }
}
