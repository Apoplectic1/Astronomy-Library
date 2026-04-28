namespace Astronomy.PCL
{
    /// <summary>
    /// Color space of an XISF image, mirroring <c>pcl::ColorSpace::value_type</c>.
    /// </summary>
    public enum XisfColorSpace
    {
        /// <summary>Unknown or unsupported color space.</summary>
        Unknown = -1,
        /// <summary>Grayscale monochrome.</summary>
        Gray = 0,
        /// <summary>RGB color.</summary>
        Rgb = 1,
        /// <summary>CIE XYZ color space.</summary>
        CieXYZ = 2,
        /// <summary>CIE L*a*b* color space.</summary>
        CieLab = 3,
        /// <summary>CIE L*c*h* color space.</summary>
        CieLch = 4,
        /// <summary>HSV: Hue, Saturation, Value.</summary>
        Hsv = 5,
        /// <summary>HSI: Hue, Saturation, Intensity.</summary>
        Hsi = 6
    }
}
