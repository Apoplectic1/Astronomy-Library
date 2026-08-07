#nullable enable
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Astronomy.XISF.Compression;
using Xunit;

namespace Astronomy.XISF.Tests;

public sealed class XisfBlockRewriterTests : IDisposable
{
    private readonly string mTempDir;

    public XisfBlockRewriterTests()
    {
        mTempDir = Path.Combine(Path.GetTempPath(), "astronomy-xisf-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mTempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(mTempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static byte[] TestPixels(int count = 24)
    {
        byte[] pixels = new byte[count];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7 + 1);
        return pixels;
    }

    /// <summary>
    /// Builds a monolithic XISF from an XML template containing <c>{LOC0}</c>/<c>{LOC1}</c>… placeholders,
    /// one per attachment, replaced with fixed-width <c>attachment:offset:size</c> values so the XML
    /// length is stable while offsets are being formatted. Attachments are packed immediately after the
    /// XML in index order.
    /// </summary>
    private string WriteXisf(string name, string xmlTemplate, params byte[][] attachments)
    {
        string path = Path.Combine(mTempDir, name);
        const int fixedDigits = 10;

        string BuildXml(long[] offsets)
        {
            string xml = xmlTemplate;
            for (int i = 0; i < attachments.Length; i++)
            {
                string location = string.Create(CultureInfo.InvariantCulture,
                    $"attachment:{offsets[i].ToString($"D{fixedDigits}", CultureInfo.InvariantCulture)}:{attachments[i].Length}");
                xml = xml.Replace($"{{LOC{i}}}", location, StringComparison.Ordinal);
            }
            return xml;
        }

        int xmlLen = Encoding.UTF8.GetByteCount(BuildXml(new long[attachments.Length]));
        long[] realOffsets = new long[attachments.Length];
        long position = 16 + xmlLen;
        for (int i = 0; i < attachments.Length; i++)
        {
            realOffsets[i] = position;
            position += attachments[i].Length;
        }
        byte[] xmlBytes = Encoding.UTF8.GetBytes(BuildXml(realOffsets));
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
        foreach (byte[] attachment in attachments)
            fs.Write(attachment, 0, attachment.Length);
        return path;
    }

    private static string SimpleImageTemplate(
        string? compressionAttr = null, string? checksumAttr = null, string extraHeader = "")
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.Append("<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">");
        xml.Append("<Image geometry=\"4:3:1\" sampleFormat=\"UInt16\" location=\"{LOC0}\" ");
        if (compressionAttr is not null) xml.Append($"compression=\"{compressionAttr}\" ");
        if (checksumAttr is not null) xml.Append($"checksum=\"{checksumAttr}\" ");
        xml.Append("/>");
        xml.Append(extraHeader);
        xml.Append("</xisf>");
        return xml.ToString();
    }

    private static string ReadXmlText(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        byte[] header = new byte[16];
        fs.ReadExactly(header, 0, 16);
        int xmlLen = header[8] | (header[9] << 8) | (header[10] << 16) | (header[11] << 24);
        byte[] xmlBytes = new byte[xmlLen];
        fs.ReadExactly(xmlBytes, 0, xmlLen);
        return Encoding.UTF8.GetString(xmlBytes);
    }

    [Fact]
    public async Task Rewrite_UncompressedToZstd19_InPlace_RoundTripsAndVerifies()
    {
        byte[] pixels = TestPixels();
        string path = WriteXisf("fresh.xisf", SimpleImageTemplate(), pixels);

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            path, path, BlockCodec.Zstd, zstdLevel: 19, TestContext.Current.CancellationToken);

        Assert.Equal(BlockCodec.ZstdSh, result.Compression.Codec);
        Assert.Equal(pixels.LongLength, result.Compression.UncompressedSize);

        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(pixels, image.Pixels);
        Assert.Equal(BlockCodec.ZstdSh, image.Compression.Codec);

        XisfChecksumResult verify = await XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(XisfChecksumVerdict.Verified, verify.Verdict);
    }

    [Fact]
    public async Task Rewrite_ZlibWithoutChecksum_ToZstd19_RoundTripsAndVerifies()
    {
        byte[] pixels = TestPixels();
        BlockCompressionResult stored = XisfBlockCompression.Compress(pixels, itemSize: 2, BlockCodec.Zlib);
        string path = WriteXisf("legacy.xisf",
            SimpleImageTemplate(compressionAttr: stored.Info.ToCompressionAttribute()),
            stored.CompressedBytes);

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            path, path, BlockCodec.Zstd, zstdLevel: 19, TestContext.Current.CancellationToken);

        Assert.Equal(BlockCodec.ZstdSh, result.Compression.Codec);

        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(pixels, image.Pixels);

        XisfChecksumResult verify = await XisfChecksumVerifier.VerifyAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(XisfChecksumVerdict.Verified, verify.Verdict);
    }

    [Fact]
    public async Task Rewrite_CompressedToNone_TempTarget_DecompressesAndStripsAttributes()
    {
        byte[] pixels = TestPixels();
        BlockCompressionResult stored = XisfBlockCompression.Compress(pixels, itemSize: 2, BlockCodec.Zstd);
        string source = WriteXisf("solveinput.xisf",
            SimpleImageTemplate(stored.Info.ToCompressionAttribute(), stored.Info.ToChecksumAttribute()),
            stored.CompressedBytes);
        byte[] sourceBytesBefore = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        string target = Path.Combine(mTempDir, "solveinput-uncompressed.xisf");

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            source, target, BlockCodec.None, ct: TestContext.Current.CancellationToken);

        Assert.False(result.Compression.IsCompressed);
        Assert.Equal(pixels.LongLength, result.AttachmentSize);

        XisfImageData image = await XisfImageReader.ReadImageAsync(target, TestContext.Current.CancellationToken);
        Assert.Equal(pixels, image.Pixels);
        Assert.False(image.Compression.IsCompressed);
        Assert.False(image.Compression.HasChecksum);

        // The source is untouched by a to-target rewrite.
        Assert.Equal(sourceBytesBefore, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rewrite_PreservesHeaderTextOutsideTheEditedAttributes()
    {
        byte[] pixels = TestPixels();
        // Distinctive spacing and entities that a re-serialization would normalize away.
        string template =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">"
            + "<Image geometry=\"4:3:1\"  sampleFormat=\"UInt16\"   location=\"{LOC0}\" colorSpace=\"Gray\">"
            + "<FITSKeyword   name=\"OBJECT\"  value=\"'M 31'\"   comment=\"a &gt; b\"/>"
            + "</Image><Property id=\"Custom:Note\" type=\"String\" value=\"  spaced  \"/></xisf>";
        string path = WriteXisf("preserve.xisf", template, pixels);

        await XisfBlockRewriter.RewriteAsync(path, path, BlockCodec.Zstd, ct: TestContext.Current.CancellationToken);

        string xml = ReadXmlText(path);
        Assert.Contains("<FITSKeyword   name=\"OBJECT\"  value=\"'M 31'\"   comment=\"a &gt; b\"/>", xml);
        Assert.Contains("<Property id=\"Custom:Note\" type=\"String\" value=\"  spaced  \"/>", xml);
        Assert.Contains("geometry=\"4:3:1\"  sampleFormat=\"UInt16\"   location=\"", xml);
        Assert.Contains("compression=\"zstd+sh:", xml);
        Assert.Contains("checksum=\"sha-1:", xml);
    }

    [Fact]
    public async Task Rewrite_ShiftsTrailingAttachment()
    {
        byte[] pixels = TestPixels();
        byte[] thumbnail = [0xDE, 0xAD, 0xBE, 0xEF, 0x42, 0x24, 0x99, 0x01];
        string template =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<xisf version=\"1.0\" xmlns=\"http://www.pixinsight.com/xisf\">"
            + "<Image geometry=\"4:3:1\" sampleFormat=\"UInt16\" location=\"{LOC0}\">"
            + "<Thumbnail geometry=\"2:2:2\" sampleFormat=\"UInt8\" location=\"{LOC1}\"/>"
            + "</Image></xisf>";
        string path = WriteXisf("withthumb.xisf", template, pixels, thumbnail);

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            path, path, BlockCodec.Zstd, ct: TestContext.Current.CancellationToken);

        // The thumbnail's bytes survive at its rewritten location.
        string xml = ReadXmlText(path);
        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        string thumbLocation = doc.Descendants(ns + "Thumbnail").Single().Attribute("location")!.Value;
        string[] parts = thumbLocation.Split(':');
        long thumbOffset = long.Parse(parts[1], CultureInfo.InvariantCulture);
        long thumbSize = long.Parse(parts[2], CultureInfo.InvariantCulture);
        Assert.Equal(thumbnail.Length, thumbSize);
        Assert.Equal(result.AttachmentOffset + result.AttachmentSize, thumbOffset);

        byte[] thumbBytes = new byte[thumbSize];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            fs.Seek(thumbOffset, SeekOrigin.Begin);
            fs.ReadExactly(thumbBytes, 0, thumbBytes.Length);
        }
        Assert.Equal(thumbnail, thumbBytes);

        // The primary still round-trips with the thumbnail present.
        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(pixels, image.Pixels);
    }

    [Fact]
    public async Task Rewrite_HonorsDeclaredBlockAlignment()
    {
        byte[] pixels = TestPixels();
        string template = SimpleImageTemplate(
            extraHeader: "<Property id=\"XISF:BlockAlignmentSize\" type=\"UInt16\" value=\"1024\"/>");
        string path = WriteXisf("aligned.xisf", template, pixels);

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            path, path, BlockCodec.Zstd, ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.AttachmentOffset % 1024);
        XisfImageData image = await XisfImageReader.ReadImageAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(pixels, image.Pixels);
    }

    [Fact]
    public async Task Rewrite_ResultGeometryMatchesWrittenFile()
    {
        byte[] pixels = TestPixels();
        string path = WriteXisf("geometry.xisf", SimpleImageTemplate(), pixels);

        XisfBlockRewriteResult result = await XisfBlockRewriter.RewriteAsync(
            path, path, BlockCodec.Zstd, zstdLevel: 19, TestContext.Current.CancellationToken);

        string xml = ReadXmlText(path);
        Assert.Equal(Encoding.UTF8.GetByteCount(xml), result.XmlLength);

        XDocument doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root!.GetDefaultNamespace();
        string location = doc.Descendants(ns + "Image").First().Attribute("location")!.Value;
        string[] parts = location.Split(':');
        Assert.Equal(result.AttachmentOffset, long.Parse(parts[1], CultureInfo.InvariantCulture));
        Assert.Equal(result.AttachmentSize, long.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Rewrite_CorruptDeclaredChecksum_ThrowsAndLeavesSourceUntouched()
    {
        byte[] pixels = TestPixels();
        BlockCompressionResult stored = XisfBlockCompression.Compress(pixels, itemSize: 2, BlockCodec.Zlib);
        string badChecksum = "sha-1:" + new string('0', 40);
        string path = WriteXisf("corrupt.xisf",
            SimpleImageTemplate(stored.Info.ToCompressionAttribute(), badChecksum),
            stored.CompressedBytes);
        byte[] before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => XisfBlockRewriter.RewriteAsync(path, path, BlockCodec.Zstd, ct: TestContext.Current.CancellationToken));
        Assert.Contains("checksum mismatch", ex.Message);

        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(mTempDir, "*.rwtmp-*"));
    }

    [Fact]
    public async Task Rewrite_LeavesNoTempFilesBehind()
    {
        byte[] pixels = TestPixels();
        string path = WriteXisf("clean.xisf", SimpleImageTemplate(), pixels);

        await XisfBlockRewriter.RewriteAsync(path, path, BlockCodec.Zstd, ct: TestContext.Current.CancellationToken);

        Assert.Empty(Directory.GetFiles(mTempDir, ".*rwtmp*"));
    }

    [Fact]
    public async Task Rewrite_ShuffledVariantCodec_Throws()
    {
        byte[] pixels = TestPixels();
        string path = WriteXisf("badcodec.xisf", SimpleImageTemplate(), pixels);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => XisfBlockRewriter.RewriteAsync(path, path, BlockCodec.ZstdSh, ct: TestContext.Current.CancellationToken));
    }
}
