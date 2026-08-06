#nullable enable
using System.Text;
using Astronomy.XISF.Compression;
using Xunit;

namespace Astronomy.XISF.Tests;

public sealed class XisfImageReaderTests : IDisposable
{
    private readonly string mTempDir;

    public XisfImageReaderTests()
    {
        mTempDir = Path.Combine(Path.GetTempPath(), "astronomy-xisf-imageread-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mTempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(mTempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Builds a minimal monolithic XISF file: signature block + XML header + the image attachment.
    /// The attachment lands immediately after the XML; its offset is zero-padded to a fixed width so the
    /// XML length is stable while the location attribute is being formatted.
    /// </summary>
    private string WriteMonolithicXisf(
        string name, byte[] attachment,
        string geometry = "4:3:1", string? sampleFormat = "UInt16",
        string? compressionAttr = null, string? checksumAttr = null,
        string? locationOverride = null, int truncateBytes = 0)
    {
        string path = Path.Combine(mTempDir, name);

        const int fixedDigits = 10;
        string BuildXml(long offset)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
            xml.Append("<Image ");
            if (geometry is not null) xml.Append($"geometry=\"{geometry}\" ");
            if (sampleFormat is not null) xml.Append($"sampleFormat=\"{sampleFormat}\" ");
            string location = locationOverride
                ?? $"attachment:{offset.ToString($"D{fixedDigits}", System.Globalization.CultureInfo.InvariantCulture)}:{attachment.Length}";
            xml.Append($"location=\"{location}\" ");
            if (compressionAttr is not null) xml.Append($"compression=\"{compressionAttr}\" ");
            if (checksumAttr is not null) xml.Append($"checksum=\"{checksumAttr}\" ");
            xml.Append("/></xisf>");
            return xml.ToString();
        }

        // Two-pass: measure XML with a placeholder offset, then rebuild with the real one (same width).
        int xmlLen = Encoding.UTF8.GetByteCount(BuildXml(0));
        long attachmentOffset = 16 + xmlLen;
        byte[] xmlBytes = Encoding.UTF8.GetBytes(BuildXml(attachmentOffset));
        Assert.Equal(xmlLen, xmlBytes.Length); // fixed-width offset keeps the length stable

        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
        header[8] = (byte)(xmlLen & 0xFF);
        header[9] = (byte)((xmlLen >> 8) & 0xFF);
        header[10] = (byte)((xmlLen >> 16) & 0xFF);
        header[11] = (byte)((xmlLen >> 24) & 0xFF);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(header, 0, 16);
        fs.Write(xmlBytes, 0, xmlBytes.Length);
        int writeLen = attachment.Length - truncateBytes;
        fs.Write(attachment, 0, writeLen);
        return path;
    }

    private static byte[] TestPixels(int count = 24)
    {
        byte[] pixels = new byte[count];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 + 1);
        return pixels;
    }

    [Fact]
    public async Task ReadImage_UncompressedAttachment_ReturnsExactBytesAndMetadata()
    {
        byte[] pixels = TestPixels(); // 4 × 3 × 1 × 2 bytes (UInt16)
        string path = WriteMonolithicXisf("uncompressed.xisf", pixels);

        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(pixels, image.Pixels);
        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(1, image.Channels);
        Assert.Equal("UInt16", image.SampleFormat);
        Assert.Equal(2, image.BytesPerSample);
        Assert.False(image.Compression.IsCompressed);
    }

    [Theory]
    [InlineData(BlockCodec.Zlib)]
    [InlineData(BlockCodec.Lz4)]
    [InlineData(BlockCodec.Lz4Hc)]
    [InlineData(BlockCodec.Zstd)]
    public async Task ReadImage_CompressedAttachment_RoundTrips(BlockCodec family)
    {
        byte[] pixels = TestPixels();
        BlockCompressionResult stored = XisfBlockCompression.Compress(pixels, itemSize: 2, family);
        string path = WriteMonolithicXisf($"compressed-{family}.xisf", stored.CompressedBytes,
            compressionAttr: stored.Info.ToCompressionAttribute(),
            checksumAttr: stored.Info.ToChecksumAttribute());

        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(pixels, image.Pixels);
        Assert.True(image.Compression.IsCompressed);
        Assert.Equal(stored.Info.Codec, image.Compression.Codec);
    }

    [Fact]
    public async Task ReadImage_TruncatedAttachment_Throws()
    {
        byte[] pixels = TestPixels();
        string path = WriteMonolithicXisf("truncated.xisf", pixels, truncateBytes: 4);

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("extends past the end", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public async Task ReadImage_MalformedLocation_Throws()
    {
        byte[] pixels = TestPixels();
        string path = WriteMonolithicXisf("badlocation.xisf", pixels, locationOverride: "attachment:garbage");

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("location", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public async Task ReadImage_CorruptedBlock_FailsChecksum()
    {
        byte[] pixels = TestPixels();
        BlockCompressionResult stored = XisfBlockCompression.Compress(pixels, itemSize: 2);
        string path = WriteMonolithicXisf("corrupt.xisf", stored.CompressedBytes,
            compressionAttr: stored.Info.ToCompressionAttribute(),
            checksumAttr: stored.Info.ToChecksumAttribute());

        // Flip one byte inside the attachment (last byte of the file).
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Seek(-1, SeekOrigin.End);
            int b = fs.ReadByte();
            fs.Seek(-1, SeekOrigin.End);
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("checksum mismatch", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public async Task ReadImage_GeometryDisagreesWithPixelCount_Throws()
    {
        byte[] pixels = TestPixels(24);
        string path = WriteMonolithicXisf("badgeometry.xisf", pixels, geometry: "5:3:1"); // needs 30 bytes

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("geometry", ex.Message);
    }

    [Fact]
    public async Task ReadImage_UnknownSampleFormat_Throws()
    {
        byte[] pixels = TestPixels();
        string path = WriteMonolithicXisf("badformat.xisf", pixels, sampleFormat: "Int13");

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("Int13", ex.Message);
    }

    /// <summary>
    /// The pixel-free header path is pinned: a header read of a file whose block uses a codec this
    /// library cannot decode still succeeds, because it never touches the data block.
    /// </summary>
    [Fact]
    public async Task HeaderRead_UnsupportedCodecAttachment_StillSucceeds()
    {
        byte[] pixels = TestPixels();
        string path = WriteMonolithicXisf("foreigncodec.xisf", pixels,
            compressionAttr: $"bzip2:{pixels.Length}", checksumAttr: null);

        XisfHeader header = await XisfHeaderReader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(4, header.PixelWidth);
        Assert.Equal(3, header.PixelHeight);

        // While the image read of the same file fails fast on the unknown codec.
        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("bzip2", ex.Message);
    }
}
