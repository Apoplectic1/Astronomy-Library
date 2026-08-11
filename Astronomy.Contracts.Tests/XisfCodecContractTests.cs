using Astronomy.XISF.Compression;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #28 — the XISF codec-layer semantics a consumer's
/// read/re-store paths bake in. Normative spec: openspec/specs/xisf-block-compression/.
/// </summary>
public sealed class XisfCodecContractTests
{
    private static byte[] Raw()
    {
        byte[] raw = new byte[4096];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i * 31 % 251);
        return raw;
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #28 (three clauses, one silent-wrong-result surface):
    //   (i)   checksums cover the STORED (post-compression) bytes, never the raw
    //         pixels — a consumer verifying against raw bytes would reject every
    //         valid producer file;
    //   (ii)  LZ4 is the RAW block format (not LZ4 frame), so decode is impossible
    //         without the declared uncompressed size, and a size disagreement is a
    //         hard error rather than a truncated buffer;
    //   (iii) BlockCompressionInfo.Parse is TOLERANT on an unknown codec token
    //         (returns BlockCodec.Other so inspect-only reads keep working) while
    //         Decompress / ToCompressionAttribute are STRICT and throw naming it.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Checksum_CoversStoredBytes_NotRaw()
    {
        byte[] raw = Raw();
        BlockCompressionResult result = XisfBlockCompression.Compress(raw, itemSize: 2);

        // The declared digest is the digest of the stored (compressed) bytes...
        Assert.Equal(
            XisfBlockCompression.ComputeChecksumHex(result.Info.ChecksumName, result.CompressedBytes),
            result.Info.ChecksumHex);
        // ...and NOT the digest of the raw pixels.
        Assert.NotEqual(
            XisfBlockCompression.ComputeChecksumHex(result.Info.ChecksumName, raw),
            result.Info.ChecksumHex);

        // VerifyChecksum agrees: stored bytes pass, raw bytes fail.
        XisfBlockCompression.VerifyChecksum(result.CompressedBytes, result.Info);
        Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.VerifyChecksum(Raw(), result.Info));
    }

    [Fact]
    public void Lz4_RawBlockFormat_DecodeRequiresTheDeclaredSize()
    {
        byte[] raw = Raw();
        BlockCompressionResult result = XisfBlockCompression.Compress(raw, itemSize: 1, BlockCodec.Lz4);

        // With the true declared size the block round-trips.
        Assert.Equal(raw, XisfBlockCompression.Decompress(result.CompressedBytes, result.Info));

        // With a wrong declared size the decode is a hard error — raw-LZ4 carries no
        // internal length, so the declared size is load-bearing, not advisory.
        BlockCompressionInfo wrongSize = BlockCompressionInfo.Parse($"lz4:{raw.Length * 2}", null);
        Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.Decompress(result.CompressedBytes, wrongSize));
    }

    [Fact]
    public void UnknownCodecToken_TolerantParse_StrictUse()
    {
        // Parse never throws on an unknown token: read-side "is this block already
        // compressed?" inspection must keep working on files it merely inspects.
        BlockCompressionInfo info = BlockCompressionInfo.Parse("bzip2:1000", null);
        Assert.Equal(BlockCodec.Other, info.Codec);
        Assert.Equal("bzip2", info.CodecName);

        // Use is strict: decoding or re-formatting an Other block throws, naming the token.
        InvalidDataException decode = Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.Decompress(new byte[16], info));
        Assert.Contains("bzip2", decode.Message);

        InvalidOperationException format = Assert.Throws<InvalidOperationException>(
            () => info.ToCompressionAttribute());
        Assert.Contains("bzip2", format.Message);
    }
}
