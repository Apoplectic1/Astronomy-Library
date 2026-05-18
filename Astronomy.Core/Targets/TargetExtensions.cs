using System.Runtime.CompilerServices;

namespace Astronomy.Core.Targets
{
    /// <summary>
    /// Internal extension helpers that fold the magnitude-plus-flag declination
    /// convention into the signed-degrees shape that the geometry layer consumes,
    /// alongside the (already-canonical-on-read) decimal-hour right-ascension.
    /// </summary>
    /// <remarks>
    /// Replaces the per-callsite <c>target.North ? target.Declination : -target.Declination</c>
    /// preamble; convention is now enforced by the extension instead of by reviewer
    /// vigilance. <see cref="MethodImplOptions.AggressiveInlining"/> keeps hot loops
    /// at parity with the inline form.
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
