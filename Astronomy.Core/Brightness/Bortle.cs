using System;

namespace Astronomy.Core.Brightness
{
    /// <summary>
    /// Lookup tables mapping Bortle dark-sky class (1 = excellent, 9 = inner-city)
    /// to typical default values for the moonless zenith dark-sky brightness V₀
    /// (mag/arcsec²) and the atmospheric extinction coefficient k at 500 nm
    /// (mag/airmass). Used by the K-S sky-brightness model and by TargetPlanner's
    /// per-Location UI to pre-fill sensible defaults when the user picks a class.
    /// </summary>
    /// <remarks>
    /// Values are the standard amateur-astrophotography averages found in
    /// Bortle 2001 / IDA references; for site-specific accuracy users should
    /// override via measured extinction or SQM readings.
    /// </remarks>
    public static class Bortle
    {
        // Indexed by (bortleClass - 1). Class 1 = excellent dark, 9 = inner-city.
        private static readonly double[] sZenithMag = new double[]
        {
            21.99,  // 1 -- Excellent dark site
            21.93,  // 2 -- Typical truly dark
            21.69,  // 3 -- Rural
            20.97,  // 4 -- Rural/suburban transition
            20.49,  // 5 -- Suburban
            19.50,  // 6 -- Bright suburban
            18.94,  // 7 -- Suburban/urban transition
            17.80,  // 8 -- City
            16.50,  // 9 -- Inner-city
        };

        // k_500nm (mag/airmass), sea-level. Wavelength scaling to other bands is
        // applied externally via SkyBrightness.ScaleK (Rayleigh λ⁻⁴ in v1).
        private static readonly double[] sExtinctionK500 = new double[]
        {
            0.10,   // 1
            0.13,   // 2
            0.18,   // 3
            0.22,   // 4
            0.28,   // 5
            0.35,   // 6
            0.42,   // 7
            0.48,   // 8
            0.55,   // 9
        };

        /// <summary>
        /// Typical moonless zenith dark-sky brightness (mag/arcsec²) for the given
        /// Bortle class. Class is clamped to [1, 9].
        /// </summary>
        public static double DefaultZenithMag(int bortleClass)
            => sZenithMag[ClampIndex(bortleClass)];

        /// <summary>
        /// Typical atmospheric extinction coefficient k at 500 nm (mag/airmass)
        /// for the given Bortle class, sea-level. Class is clamped to [1, 9].
        /// </summary>
        public static double DefaultExtinctionK500(int bortleClass)
            => sExtinctionK500[ClampIndex(bortleClass)];

        private static int ClampIndex(int bortleClass)
        {
            if (bortleClass < 1) return 0;
            if (bortleClass > 9) return 8;
            return bortleClass - 1;
        }
    }
}
