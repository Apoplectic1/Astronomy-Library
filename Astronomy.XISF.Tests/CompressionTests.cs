using Astronomy.XISF.Compression;
using Xunit;

namespace Astronomy.XISF.Tests;

public sealed class XisfBlockCompressionTests
{
    [Fact]
    public void Shuffle_KnownVector_ItemSize2()
    {
        byte[] vec = { 1, 2, 3, 4, 5, 6 };
        byte[] shuffled = XisfBlockCompression.Shuffle(vec, 2);

        Assert.Equal(new byte[] { 1, 3, 5, 2, 4, 6 }, shuffled);
        Assert.Equal(vec, XisfBlockCompression.Unshuffle(shuffled, 2));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(1)]
    public void Compress_Decompress_RoundTrips(int itemSize)
    {
        var rng = new Random(20260604);
        for (int trial = 0; trial < 5; trial++)
        {
            int samples = rng.Next(1000, 60000);
            byte[] raw = new byte[samples * itemSize];
            rng.NextBytes(raw);

            BlockCompressionResult result = XisfBlockCompression.Compress(raw, itemSize);
            byte[] restored = XisfBlockCompression.Decompress(result.CompressedBytes, result.Info);

            Assert.Equal(raw, restored);
            Assert.Equal(result.Info.ChecksumHex, XisfBlockCompression.ComputeSha1Hex(result.CompressedBytes));
            Assert.Equal((byte)0x78, result.CompressedBytes[0]); // zlib wrapper, not raw deflate
            Assert.Equal(raw.LongLength, result.Info.UncompressedSize);
        }
    }

    [Fact]
    public void Compress_ShuffledAttributes_RoundTripThroughParse()
    {
        byte[] img = new byte[2 * 4096];
        new Random(1).NextBytes(img);

        BlockCompressionResult result = XisfBlockCompression.Compress(img, 2);
        string compAttr = result.Info.ToCompressionAttribute();
        string csumAttr = result.Info.ToChecksumAttribute();

        Assert.Equal($"zlib+sh:{img.Length}:2", compAttr);
        Assert.StartsWith("sha-1:", csumAttr);

        BlockCompressionInfo parsed = BlockCompressionInfo.Parse(compAttr, csumAttr);
        Assert.Equal(BlockCodec.ZlibSh, parsed.Codec);
        Assert.Equal(2, parsed.ItemSize);
        Assert.Equal(img.Length, parsed.UncompressedSize);
        Assert.Equal("sha-1", parsed.ChecksumName);
        Assert.True(parsed.IsCompressed);
    }

    [Fact]
    public void Parse_NullAttributes_IsUncompressed()
    {
        BlockCompressionInfo none = BlockCompressionInfo.Parse(null, null);

        Assert.False(none.IsCompressed);
        Assert.Equal(BlockCodec.None, none.Codec);
    }

    [Fact]
    public void Parse_ForeignCodec_DetectedAsCompressed()
    {
        BlockCompressionInfo lz4 = BlockCompressionInfo.Parse("lz4+sh:1048576:2", "sha-1:deadbeef");

        Assert.True(lz4.IsCompressed);
        Assert.Equal(BlockCodec.Other, lz4.Codec);
    }
}
