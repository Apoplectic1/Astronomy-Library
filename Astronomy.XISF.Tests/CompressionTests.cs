using System.Security.Cryptography;
using System.Text;
using Astronomy.XISF.Compression;
using K4os.Compression.LZ4;
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

    [Theory]
    [InlineData(BlockCodec.Zlib, 1, "zlib")]
    [InlineData(BlockCodec.Zlib, 2, "zlib+sh")]
    [InlineData(BlockCodec.Lz4, 1, "lz4")]
    [InlineData(BlockCodec.Lz4, 2, "lz4+sh")]
    [InlineData(BlockCodec.Lz4Hc, 1, "lz4hc")]
    [InlineData(BlockCodec.Lz4Hc, 2, "lz4hc+sh")]
    [InlineData(BlockCodec.Zstd, 1, "zstd")]
    [InlineData(BlockCodec.Zstd, 2, "zstd+sh")]
    public void Compress_Decompress_AllCodecs_RoundTripAndAttributeReparse(
        BlockCodec family, int itemSize, string expectedToken)
    {
        byte[] raw = new byte[48_000];
        new Random(20260806).NextBytes(raw);

        BlockCompressionResult result = XisfBlockCompression.Compress(raw, itemSize, family);

        Assert.Equal(expectedToken, result.Info.CodecName);
        Assert.Equal(raw.LongLength, result.Info.UncompressedSize);
        Assert.Equal(itemSize > 1 ? itemSize : 1, result.Info.ItemSize);

        byte[] restored = XisfBlockCompression.Decompress(result.CompressedBytes, result.Info);
        Assert.Equal(raw, restored);

        // Emitted attribute re-parses to the identical descriptor.
        BlockCompressionInfo reparsed = BlockCompressionInfo.Parse(
            result.Info.ToCompressionAttribute(), result.Info.ToChecksumAttribute());
        Assert.Equal(result.Info.Codec, reparsed.Codec);
        Assert.Equal(result.Info.UncompressedSize, reparsed.UncompressedSize);
        Assert.Equal(result.Info.ItemSize, reparsed.ItemSize);
        Assert.Equal(result.Info.ChecksumHex, reparsed.ChecksumHex);

        // And decode succeeds from the re-parsed descriptor too (the on-disk path).
        Assert.Equal(raw, XisfBlockCompression.Decompress(result.CompressedBytes, reparsed));
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
    public void Compress_ShuffledVariantAsArgument_Throws()
    {
        byte[] raw = new byte[64];

        Assert.Throws<ArgumentOutOfRangeException>(() => XisfBlockCompression.Compress(raw, 2, BlockCodec.ZlibSh));
        Assert.Throws<ArgumentOutOfRangeException>(() => XisfBlockCompression.Compress(raw, 1, BlockCodec.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => XisfBlockCompression.Compress(raw, 1, BlockCodec.Other));
    }

    [Fact]
    public void Parse_NullAttributes_IsUncompressed()
    {
        BlockCompressionInfo none = BlockCompressionInfo.Parse(null, null);

        Assert.False(none.IsCompressed);
        Assert.Equal(BlockCodec.None, none.Codec);
    }

    [Fact]
    public void Parse_ForeignCodec_DetectedAsCompressed_ButUndecodable()
    {
        // A codec token outside the XISF spec: read-side detection still reports "compressed",
        // but decoding it is a hard error.
        BlockCompressionInfo foreign = BlockCompressionInfo.Parse("bzip2:1048576", "sha-1:deadbeef");

        Assert.True(foreign.IsCompressed);
        Assert.Equal(BlockCodec.Other, foreign.Codec);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.Decompress(new byte[16], foreign));
        Assert.Contains("bzip2", ex.Message);
    }

    [Theory]
    [InlineData("zlib")]                 // missing size
    [InlineData("zlib:abc")]             // non-numeric size
    [InlineData("zlib:-5")]              // non-positive size
    [InlineData("zlib+sh:100")]          // shuffled without item size
    [InlineData("zlib+sh:100:0")]        // non-positive item size
    [InlineData("zlib:100:2")]           // unshuffled with extra field
    [InlineData("zlib+sh:100:2:64")]     // sub-block-style extra field
    [InlineData("lz4:1:2:3:4")]          // sub-block-style extra fields
    public void Parse_MalformedKnownCodecAttribute_Throws(string compressionAttr)
    {
        Assert.Throws<InvalidDataException>(() => BlockCompressionInfo.Parse(compressionAttr, null));
    }

    [Theory]
    [InlineData("sha-1")]                // missing digest
    [InlineData("sha-256:")]             // empty digest
    [InlineData("sha-256:aa:bb")]        // extra field
    public void Parse_MalformedChecksumAttribute_Throws(string checksumAttr)
    {
        Assert.Throws<InvalidDataException>(() => BlockCompressionInfo.Parse(null, checksumAttr));
    }

    // Legacy producers (old PixInsight/SGP-era files) wrote non-hyphenated algorithm tokens;
    // Parse canonicalizes them so verification works and a re-save writes the spec token.
    [Theory]
    [InlineData("sha1", "sha-1", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("sha256", "sha-256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("sha512", "sha-512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    public void Parse_LegacyChecksumAlias_Canonicalized(string alias, string canonical, string abcHex)
    {
        BlockCompressionInfo parsed = BlockCompressionInfo.Parse(null, $"{alias}:{abcHex}");

        Assert.Equal(canonical, parsed.ChecksumName);
        Assert.Equal($"{canonical}:{abcHex}", parsed.ToChecksumAttribute());
        XisfBlockCompression.VerifyChecksum(Encoding.ASCII.GetBytes("abc"), in parsed);
    }

    [Fact]
    public void Decompress_DeclaredSizeDisagreement_Throws()
    {
        byte[] raw = new byte[4096];
        new Random(7).NextBytes(raw);
        BlockCompressionResult result = XisfBlockCompression.Compress(raw, 1, BlockCodec.Zstd);

        BlockCompressionInfo lying = result.Info with { UncompressedSize = raw.Length + 100 };

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.Decompress(result.CompressedBytes, lying));
        Assert.Contains($"{raw.Length + 100}", ex.Message);
    }

    // Known NIST vectors for "abc" — pins the algorithm dispatch, not just self-consistency.
    [Theory]
    [InlineData("sha-1", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("sha-256", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("sha-512", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f")]
    [InlineData("sha3-256", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532")]
    [InlineData("sha3-512", "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0")]
    public void ComputeChecksumHex_KnownVectors(string algorithm, string expectedHex)
    {
        byte[] abc = Encoding.ASCII.GetBytes("abc");
        Assert.Equal(expectedHex, XisfBlockCompression.ComputeChecksumHex(algorithm, abc));

        // VerifyChecksum accepts the matching digest and rejects a tampered one.
        BlockCompressionInfo good = BlockCompressionInfo.Parse(null, $"{algorithm}:{expectedHex}");
        XisfBlockCompression.VerifyChecksum(abc, in good);

        BlockCompressionInfo bad = good with { ChecksumHex = new string('0', expectedHex.Length) };
        Assert.Throws<InvalidDataException>(() => XisfBlockCompression.VerifyChecksum(abc, in bad));
    }

    [Fact]
    public void ComputeChecksumHex_UnknownAlgorithm_Throws()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => XisfBlockCompression.ComputeChecksumHex("md5", new byte[4]));
        Assert.Contains("md5", ex.Message);
    }

    /// <summary>
    /// Interop pinned against NINA's writer: blocks encoded with NINA's exact calls (same packages, same
    /// levels, same attribute strings — see NINA.Image XISFData) must decode through this library.
    /// </summary>
    [Fact]
    public void Decompress_NinaEncodedLz4Sh_Decodes()
    {
        byte[] raw = new byte[20_000];
        new Random(42).NextBytes(raw);

        // NINA: shuffle (itemSize 2 for UInt16), then LZ4Codec.Encode(..., LZ4Level.L00_FAST).
        byte[] shuffled = XisfBlockCompression.Shuffle(raw, 2);
        byte[] tmp = new byte[LZ4Codec.MaximumOutputSize(shuffled.Length)];
        int written = LZ4Codec.Encode(shuffled, 0, shuffled.Length, tmp, 0, tmp.Length, LZ4Level.L00_FAST);
        byte[] stored = tmp[..written];

        // NINA writes: compression="lz4+sh:<uncompressedSize>:<itemSize>" checksum="sha-256:<hex>".
        string compAttr = $"lz4+sh:{raw.Length}:2";
        string csumAttr = $"sha-256:{Convert.ToHexStringLower(SHA256.HashData(stored))}";

        BlockCompressionInfo info = BlockCompressionInfo.Parse(compAttr, csumAttr);
        XisfBlockCompression.VerifyChecksum(stored, in info);

        Assert.Equal(raw, XisfBlockCompression.Decompress(stored, info));
    }

    [Fact]
    public void Decompress_NinaEncodedZstdSh_Decodes()
    {
        byte[] raw = new byte[20_000];
        new Random(43).NextBytes(raw);

        // NINA: shuffle, then new ZstdSharp.Compressor(1).Wrap(...).
        byte[] shuffled = XisfBlockCompression.Shuffle(raw, 2);
        using ZstdSharp.Compressor compressor = new(level: 1);
        byte[] stored = compressor.Wrap(shuffled).ToArray();

        BlockCompressionInfo info = BlockCompressionInfo.Parse($"zstd+sh:{raw.Length}:2", null);

        Assert.Equal(raw, XisfBlockCompression.Decompress(stored, info));
    }
}
