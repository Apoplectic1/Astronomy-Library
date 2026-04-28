namespace Astronomy.PCL
{
    /// <summary>
    /// Geometry, color space, and sample-format metadata for one image inside an XISF file.
    /// Returned by <see cref="XisfFile.SelectImage(int)"/>.
    /// </summary>
    public readonly struct XisfImageInfo
    {
        /// <summary>Construct an image info record. Generally produced by <see cref="XisfFile.SelectImage(int)"/>.</summary>
        public XisfImageInfo(int width, int height, int channelCount, int bitsPerSample, bool isFloatingPoint, XisfColorSpace colorSpace)
        {
            Width = width;
            Height = height;
            ChannelCount = channelCount;
            BitsPerSample = bitsPerSample;
            IsFloatingPoint = isFloatingPoint;
            ColorSpace = colorSpace;
        }

        /// <summary>Image width in pixels.</summary>
        public int Width { get; }
        /// <summary>Image height in pixels.</summary>
        public int Height { get; }
        /// <summary>Number of channels (1 for Gray, 3 for RGB).</summary>
        public int ChannelCount { get; }
        /// <summary>Bits per sample on disk: 8, 16, 32, or 64.</summary>
        public int BitsPerSample { get; }
        /// <summary>True if the on-disk samples are IEEE 754 floats; false if integer.</summary>
        public bool IsFloatingPoint { get; }
        /// <summary>The image's color space.</summary>
        public XisfColorSpace ColorSpace { get; }

        /// <summary>Total sample count: <c>Width * Height * ChannelCount</c>.</summary>
        public long SampleCount => (long)Width * Height * ChannelCount;
    }
}
