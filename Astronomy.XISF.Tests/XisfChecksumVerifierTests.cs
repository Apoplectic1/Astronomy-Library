#nullable enable
using System.Text;
using Astronomy.XISF.Compression;
using Xunit;

namespace Astronomy.XISF.Tests;

public sealed class XisfChecksumVerifierTests : IDisposable
{
    private readonly string mTempDir;

    public XisfChecksumVerifierTests()
    {
        mTempDir = Path.Combine(Path.GetTempPath(), "astronomy-xisf-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mTempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(mTempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // Same minimal monolithic layout the reader tests build: signature + XML header + attachment,
    // fixed-width offset so the XML length is stable while the location attribute is formatted.
    private string WriteMonolithicXisf(
        string name, byte[] attachment,
        string? compressionAttr = null, string? checksumAttr = null, int truncateBytes = 0)
    {
        string path = Path.Combine(mTempDir, name);

        const int fixedDigits = 10;
        string BuildXml(long offset)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
            xml.Append("<Image geometry=\"4:3:1\" sampleFormat=\"UInt16\" ");
            xml.Append($"location=\"attachment:{offset.ToString($"D{fixedDigits}", System.Globalization.CultureInfo.InvariantCulture)}:{attachment.Length}\" ");
            if (compressionAttr is not null) xml.Append($"compression=\"{compressionAttr}\" ");
            if (checksumAttr is not null) xml.Append($"checksum=\"{checksumAttr}\" ");
            xml.Append("/></xisf>");
            return xml.ToString();
        }

        int xmlLen = Encoding.UTF8.GetByteCount(BuildXml(0));
        long attachmentOffset = 16 + xmlLen;
        byte[] xmlBytes = Encoding.UTF8.GetBytes(BuildXml(attachmentOffset));
        Assert.Equal(xmlLen, xmlBytes.Length);

        byte[] header = new byte[16];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, header, 0);
        header[8] = (byte)(xmlLen & 0xFF);
        header[9] = (byte)((xmlLen >> 8) & 0xFF);
        header[10] = (byte)((xmlLen >> 16) & 0xFF);
        header[11] = (byte)((xmlLen >> 24) & 0xFF);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(header, 0, 16);
        fs.Write(xmlBytes, 0, xmlBytes.Length);
        fs.Write(attachment, 0, attachment.Length - truncateBytes);
        return path;
    }

    private static byte[] TestPixels()
    {
        byte[] pixels = new byte[24];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 + 1);
        return pixels;
    }

    [Theory]
    [InlineData(BlockCodec.Zlib)]
    [InlineData(BlockCodec.Zstd)]
    public async Task VerifyAsync_IntactBlock_Verified(BlockCodec family)
    {
        BlockCompressionResult block = XisfBlockCompression.Compress(TestPixels(), 2, family);
        string path = WriteMonolithicXisf("intact.xisf", block.CompressedBytes,
            block.Info.ToCompressionAttribute(), block.Info.ToChecksumAttribute());

        XisfChecksumResult result = await XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(XisfChecksumVerdict.Verified, result.Verdict);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task VerifyAsync_CorruptedByte_MismatchWithBothDigests()
    {
        BlockCompressionResult block = XisfBlockCompression.Compress(TestPixels(), 2);
        byte[] corrupted = (byte[])block.CompressedBytes.Clone();
        corrupted[^1] ^= 0xFF;
        string path = WriteMonolithicXisf("corrupt.xisf", corrupted,
            block.Info.ToCompressionAttribute(), block.Info.ToChecksumAttribute());

        XisfChecksumResult result = await XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(XisfChecksumVerdict.Mismatch, result.Verdict);
        Assert.Contains(block.Info.ChecksumHex, result.Detail);
        Assert.Contains("computed", result.Detail);
    }

    [Fact]
    public async Task VerifyAsync_NoChecksumDeclared_NoChecksum()
    {
        string path = WriteMonolithicXisf("bare.xisf", TestPixels());

        XisfChecksumResult result = await XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(XisfChecksumVerdict.NoChecksum, result.Verdict);
        Assert.False(result.Compression.IsCompressed);
    }

    [Fact]
    public async Task VerifyAsync_TruncatedAttachment_ThrowsStructural()
    {
        BlockCompressionResult block = XisfBlockCompression.Compress(TestPixels(), 2);
        string path = WriteMonolithicXisf("truncated.xisf", block.CompressedBytes,
            block.Info.ToCompressionAttribute(), block.Info.ToChecksumAttribute(), truncateBytes: 3);

        await Assert.ThrowsAsync<InvalidDataException>(() => XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken));
    }
}
