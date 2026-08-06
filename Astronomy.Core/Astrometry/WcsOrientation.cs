using System;

namespace Astronomy.Core.Astrometry
{
    /// <summary>
    /// Sky orientation read out of a tangent-plane WCS plate solution's CD matrix
    /// (FITS keywords <c>CD1_1..CD2_2</c>): image-axis rotation, position angle,
    /// image parity, and pixel scales.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Domain of validity.</b> Results are correct for normal images and for
    /// single-mirrored images (odd mirror count — parity is read from the CD
    /// determinant's sign and the rotation is sign-adjusted). An image mirrored on
    /// <em>both</em> axes is mathematically indistinguishable from a normal image
    /// rotated 180&#176; — two mirrors compose to a rotation and leave the
    /// determinant unchanged — so that case cannot be detected from the matrix by
    /// construction; callers with such data get a 180&#176;-offset angle with
    /// <see cref="Flipped"/> false. (Consumers that fold angles modulo 180 are
    /// unaffected.)
    /// </para>
    /// <para>
    /// <b>Solver offsets stay with the solver.</b> <see cref="PositionAngleDegrees"/>
    /// is the generic WCS form — the [0,360) complement of image-axis rotation.
    /// Some solver integrations layer a convention offset on top (e.g. a further
    /// 180&#176;); applying one is the calling solver-wrapper's responsibility,
    /// never this conversion's.
    /// </para>
    /// </remarks>
    public readonly struct WcsOrientation
    {
        /// <summary>Image-axis rotation in degrees, normalized to [0, 360).</summary>
        public double RotationDegrees { get; init; }

        /// <summary>
        /// Position angle of the image's +y axis in degrees from celestial North
        /// turning toward East, normalized to [0, 360) — the complement
        /// (360 &#8722; <see cref="RotationDegrees"/>) of image-axis rotation.
        /// </summary>
        public double PositionAngleDegrees { get; init; }

        /// <summary>
        /// True when the image mirrors the sky (single-axis flip; CD determinant
        /// sign). See the type remarks for the undetectable both-axes case.
        /// </summary>
        public bool Flipped { get; init; }

        /// <summary>Pixel scale along the image x axis, arcseconds per pixel.</summary>
        public double PixelScaleXArcsec { get; init; }

        /// <summary>Pixel scale along the image y axis, arcseconds per pixel.</summary>
        public double PixelScaleYArcsec { get; init; }

        /// <summary>
        /// Derives orientation from the four CD-matrix elements of a tangent-plane
        /// WCS solution (degrees-per-pixel units, as FITS defines them).
        /// </summary>
        public static WcsOrientation FromCdMatrix(double cd11, double cd12, double cd21, double cd22)
        {
            double determinant = cd11 * cd22 - cd12 * cd21;
            int sign = determinant < 0 ? -1 : 1;

            double cdelta1 = sign * Math.Sqrt(cd11 * cd11 + cd21 * cd21);
            double cdelta2 = Math.Sqrt(cd12 * cd12 + cd22 * cd22);

            // cdelta2 is non-negative by construction, so this reduces to "determinant >= 0":
            // mirrored parity relative to the standard sky-projection handedness.
            bool flipped = cdelta1 >= 0 || cdelta2 < 0;

            double rot2Cd = Math.Atan2(sign * cd11, cd21) - Math.PI / 2d;
            double rotationDeg = (flipped ? -rot2Cd : rot2Cd) * (180d / Math.PI);

            double rotation = Modulus360(rotationDeg);

            return new WcsOrientation
            {
                RotationDegrees = rotation,
                PositionAngleDegrees = Modulus360(360d - rotation),
                Flipped = flipped,
                PixelScaleXArcsec = Math.Abs(cdelta1) * 3600d,
                PixelScaleYArcsec = Math.Abs(cdelta2) * 3600d,
            };
        }

        private static double Modulus360(double degrees) => ((degrees % 360d) + 360d) % 360d;
    }
}
