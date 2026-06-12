using System.IO.Compression;
using System.Security.Cryptography;

namespace Astronomy.XISF.Compression;

/// <summary>The compressed bytes to store plus the <see cref="BlockCompressionInfo"/> that describes them.</summary>
public readonly struct BlockCompressionResult
{
    /// <summary>The compressed (and, for "+sh", byte-shuffled) bytes to write to the file.</summary>
    public byte[] CompressedBytes { get; init; }

    /// <summary>Codec / sizes / item-size / checksum describing <see cref="CompressedBytes"/>.</summary>
    public BlockCompressionInfo Info { get; init; }
}

/// <summary>
/// XISF image-block codec: byte-shuffle + zlib + SHA-1, matching the PixInsight/NINA on-disk format.
/// Pure and UI-free. Operates on opaque byte blocks, so it is reusable for any XISF data block by any
/// consumer. <see cref="Compress"/> and <see cref="Decompress"/> are symmetric —
/// round-tripping any block returns the original bytes.
/// </summary>
public static class XisfBlockCompression
{
    private const string CodecZlibSh = "zlib+sh";
    private const string CodecZlib = "zlib";
    private const string ChecksumSha1 = "sha-1";

    /// <summary>
    /// Byte-shuffle: regroup an N-byte block of <paramref name="itemSize"/>-byte samples so all byte-0s
    /// come first, then all byte-1s, … (improves zlib's ratio on multi-byte samples). Reversible via
    /// <see cref="Unshuffle"/> using the same itemSize. A trailing remainder (length not divisible by
    /// itemSize) is copied as-is.
    /// </summary>
    public static byte[] Shuffle(byte[] data, int itemSize)
    {
        if (itemSize <= 1) return data;

        int length = data.Length;
        int items = length / itemSize;
        byte[] shuffled = new byte[length];

        int s = 0;
        for (int b = 0; b < itemSize; b++)
        {
            int u = b;
            for (int i = 0; i < items; i++, s++, u += itemSize)
                shuffled[s] = data[u];
        }

        for (int r = items * itemSize; r < length; r++, s++)
            shuffled[s] = data[r];

        return shuffled;
    }

    /// <summary>Inverse of <see cref="Shuffle"/> for the same itemSize.</summary>
    public static byte[] Unshuffle(byte[] data, int itemSize)
    {
        if (itemSize <= 1) return data;

        int length = data.Length;
        int items = length / itemSize;
        byte[] restored = new byte[length];

        int s = 0;
        for (int b = 0; b < itemSize; b++)
        {
            int u = b;
            for (int i = 0; i < items; i++, s++, u += itemSize)
                restored[u] = data[s];
        }

        for (int r = items * itemSize; r < length; r++, s++)
            restored[r] = data[s];

        return restored;
    }

    /// <summary>
    /// Compress a raw block: shuffle (when itemSize &gt; 1) → zlib (max level) → SHA-1 over the
    /// compressed bytes. Always returns the compressed result (no no-gain fallback): a block stored
    /// uncompressed would read back as uncompressed and be re-attempted on every future save.
    /// Codec is "zlib+sh" when shuffled, else plain "zlib".
    /// </summary>
    public static BlockCompressionResult Compress(byte[] raw, int itemSize)
    {
        bool shuffle = itemSize > 1;
        byte[] prepared = shuffle ? Shuffle(raw, itemSize) : raw;
        byte[] compressed = ZlibCompress(prepared);

        BlockCompressionInfo info = new()
        {
            Codec = shuffle ? BlockCodec.ZlibSh : BlockCodec.Zlib,
            CodecName = shuffle ? CodecZlibSh : CodecZlib,
            UncompressedSize = raw.LongLength,
            ItemSize = shuffle ? itemSize : 1,
            ChecksumName = ChecksumSha1,
            ChecksumHex = ComputeSha1Hex(compressed)
        };

        return new BlockCompressionResult { CompressedBytes = compressed, Info = info };
    }

    /// <summary>
    /// Inverse of <see cref="Compress"/>: zlib-inflate then unshuffle. Present for round-trip tests and the
    /// future Tier-3 image-read path.
    /// </summary>
    public static byte[] Decompress(byte[] stored, BlockCompressionInfo info)
    {
        byte[] inflated = ZlibDecompress(stored, info.UncompressedSize);
        return info.Codec == BlockCodec.ZlibSh ? Unshuffle(inflated, info.ItemSize) : inflated;
    }

    /// <summary>Lowercase-hex SHA-1 digest of the given bytes.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "SHA-1 is the XISF format's data-integrity checksum (per the XISF spec / PixInsight / NINA), not a security mechanism.")]
    public static string ComputeSha1Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(data, hash);
        return Convert.ToHexStringLower(hash);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] ZlibDecompress(byte[] data, long expectedSize)
    {
        using MemoryStream input = new(data);
        using ZLibStream zlib = new(input, CompressionMode.Decompress);
        using MemoryStream output = expectedSize > 0 && expectedSize <= int.MaxValue
            ? new MemoryStream((int)expectedSize)
            : new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
