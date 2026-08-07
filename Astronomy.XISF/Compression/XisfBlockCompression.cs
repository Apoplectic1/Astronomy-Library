using System.IO.Compression;
using System.Security.Cryptography;
using K4os.Compression.LZ4;

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
/// XISF data-block codec: byte-shuffle + zlib / LZ4 / LZ4-HC / Zstandard + checksum, matching the
/// PixInsight/NINA on-disk format. Pure and UI-free. Operates on opaque byte blocks, so it is reusable
/// for any XISF data block by any consumer. <see cref="Compress"/> and <see cref="Decompress"/> are
/// symmetric — round-tripping any block returns the original bytes.
/// </summary>
public static class XisfBlockCompression
{
    private const string ChecksumSha1 = "sha-1";

    /// <summary>
    /// Byte-shuffle: regroup an N-byte block of <paramref name="itemSize"/>-byte samples so all byte-0s
    /// come first, then all byte-1s, … (improves the codec's ratio on multi-byte samples). Reversible via
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
    /// Compress a raw block: shuffle (when itemSize &gt; 1) → codec → SHA-1 over the compressed bytes.
    /// Always returns the compressed result (no no-gain fallback): a block stored uncompressed would read
    /// back as uncompressed and be re-attempted on every future save. The recorded codec is the "+sh"
    /// variant when shuffled. Default levels match the producers this library interoperates with
    /// (NINA/PixInsight): zlib SmallestSize, LZ4 fast, LZ4-HC level 6, zstd level 1.
    /// </summary>
    /// <param name="raw">The uncompressed block.</param>
    /// <param name="itemSize">Bytes per sample; &gt; 1 enables the byte-shuffle.</param>
    /// <param name="codec">
    /// Base codec family: <see cref="BlockCodec.Zlib"/> (default), <see cref="BlockCodec.Lz4"/>,
    /// <see cref="BlockCodec.Lz4Hc"/>, or <see cref="BlockCodec.Zstd"/>. The shuffled variant is selected
    /// automatically from <paramref name="itemSize"/> — passing a "+sh" member here throws.
    /// </param>
    /// <param name="level">
    /// Optional encoder effort for <see cref="BlockCodec.Zstd"/> (1–22; null = the level-1 interop
    /// default). Level changes encode cost and output size only — any zstd decoder reads any level.
    /// Other codec families take no level; passing one for them throws rather than being silently
    /// ignored.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="codec"/> is not a base family, or <paramref name="level"/> is provided for a
    /// non-zstd family or falls outside 1–22.
    /// </exception>
    public static BlockCompressionResult Compress(byte[] raw, int itemSize, BlockCodec codec = BlockCodec.Zlib, int? level = null)
    {
        if (codec is not (BlockCodec.Zlib or BlockCodec.Lz4 or BlockCodec.Lz4Hc or BlockCodec.Zstd))
        {
            throw new ArgumentOutOfRangeException(nameof(codec), codec,
                "Pass a base codec family (Zlib/Lz4/Lz4Hc/Zstd); the +sh variant is selected from itemSize.");
        }

        if (level is not null)
        {
            if (codec is not BlockCodec.Zstd)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level,
                    $"A compression level is only meaningful for {nameof(BlockCodec.Zstd)}; {codec} has a fixed interop level.");
            }
            if (level is < 1 or > 22)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "zstd level must be within 1–22.");
            }
        }

        bool shuffle = itemSize > 1;
        byte[] prepared = shuffle ? Shuffle(raw, itemSize) : raw;

        byte[] compressed = codec switch
        {
            BlockCodec.Zlib => ZlibCompress(prepared),
            BlockCodec.Lz4 => Lz4Compress(prepared, LZ4Level.L00_FAST),
            BlockCodec.Lz4Hc => Lz4Compress(prepared, LZ4Level.L06_HC),
            _ => ZstdCompress(prepared, level ?? 1),
        };

        BlockCodec effective = (codec, shuffle) switch
        {
            (BlockCodec.Zlib, true) => BlockCodec.ZlibSh,
            (BlockCodec.Lz4, true) => BlockCodec.Lz4Sh,
            (BlockCodec.Lz4Hc, true) => BlockCodec.Lz4HcSh,
            (BlockCodec.Zstd, true) => BlockCodec.ZstdSh,
            _ => codec,
        };

        BlockCompressionInfo info = new()
        {
            Codec = effective,
            CodecName = BlockCompressionInfo.TokenFromCodec(effective),
            UncompressedSize = raw.LongLength,
            ItemSize = shuffle ? itemSize : 1,
            ChecksumName = ChecksumSha1,
            ChecksumHex = ComputeSha1Hex(compressed)
        };

        return new BlockCompressionResult { CompressedBytes = compressed, Info = info };
    }

    /// <summary>
    /// Inverse of <see cref="Compress"/> for every supported codec: decode then unshuffle. The decoded
    /// byte count must equal <see cref="BlockCompressionInfo.UncompressedSize"/> — a disagreement is a
    /// corrupt or mis-described block and throws.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Unrecognized codec, or the decoded size disagrees with the declared uncompressed size.
    /// </exception>
    /// <exception cref="InvalidOperationException">The block is not compressed (<see cref="BlockCodec.None"/>).</exception>
    public static byte[] Decompress(byte[] stored, BlockCompressionInfo info)
    {
        byte[] decoded = info.Codec switch
        {
            BlockCodec.None => throw new InvalidOperationException(
                "Block is not compressed; there is nothing to decompress."),
            BlockCodec.Other => throw new InvalidDataException(
                $"Cannot decompress XISF block: unrecognized codec '{info.CodecName}'."),
            BlockCodec.Zlib or BlockCodec.ZlibSh => ZlibDecompress(stored, info.UncompressedSize),
            BlockCodec.Lz4 or BlockCodec.Lz4Sh or BlockCodec.Lz4Hc or BlockCodec.Lz4HcSh
                => Lz4Decompress(stored, info.UncompressedSize),
            _ => ZstdDecompress(stored, info.UncompressedSize),
        };

        if (decoded.LongLength != info.UncompressedSize)
        {
            throw new InvalidDataException(
                $"XISF block decoded to {decoded.LongLength} bytes but its compression attribute declares "
                + $"{info.UncompressedSize} (codec '{info.CodecName}').");
        }

        return info.IsShuffled ? Unshuffle(decoded, info.ItemSize) : decoded;
    }

    /// <summary>
    /// Verify <paramref name="stored"/> against the block's declared checksum (which covers the stored,
    /// i.e. compressed, bytes). No-op when the block declares no checksum.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The computed digest differs from the declared one, or the declared algorithm is unrecognized.
    /// </exception>
    public static void VerifyChecksum(ReadOnlySpan<byte> stored, in BlockCompressionInfo info)
    {
        if (!info.HasChecksum) return;

        string actual = ComputeChecksumHex(info.ChecksumName, stored);
        if (!string.Equals(actual, info.ChecksumHex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"XISF block checksum mismatch ({info.ChecksumName}): declared {info.ChecksumHex}, "
                + $"computed {actual}.");
        }
    }

    /// <summary>
    /// Lowercase-hex digest of <paramref name="data"/> using an XISF checksum algorithm token:
    /// "sha-1", "sha-256", "sha-512", "sha3-256", or "sha3-512".
    /// </summary>
    /// <exception cref="InvalidDataException">The algorithm token is not one the XISF spec defines.</exception>
    public static string ComputeChecksumHex(string algorithm, ReadOnlySpan<byte> data) => algorithm switch
    {
        "sha-1" => ComputeSha1Hex(data),
        "sha-256" => Convert.ToHexStringLower(SHA256.HashData(data)),
        "sha-512" => Convert.ToHexStringLower(SHA512.HashData(data)),
        "sha3-256" => Convert.ToHexStringLower(SHA3_256.HashData(data)),
        "sha3-512" => Convert.ToHexStringLower(SHA3_512.HashData(data)),
        _ => throw new InvalidDataException($"Unrecognized XISF checksum algorithm '{algorithm}'."),
    };

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

    private static byte[] Lz4Compress(byte[] data, LZ4Level level)
    {
        byte[] buffer = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        int written = LZ4Codec.Encode(data, 0, data.Length, buffer, 0, buffer.Length, level);
        return buffer[..written];
    }

    // LZ4 raw-block decode fills exactly the declared size or the block is corrupt; the negative return
    // (malformed stream) surfaces through the same declared-size check in Decompress via an empty result.
    private static byte[] Lz4Decompress(byte[] data, long expectedSize)
    {
        if (expectedSize is <= 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                $"LZ4 block has no usable declared uncompressed size ({expectedSize}); raw-block LZ4 "
                + "cannot be decoded without it.");
        }

        byte[] output = new byte[(int)expectedSize];
        int decoded = LZ4Codec.Decode(data, 0, data.Length, output, 0, output.Length);
        if (decoded != output.Length)
        {
            throw new InvalidDataException(
                $"LZ4 block decoded to {decoded} bytes but its compression attribute declares {output.Length}.");
        }
        return output;
    }

    private static byte[] ZstdCompress(byte[] data, int level)
    {
        using ZstdSharp.Compressor compressor = new(level);
        return compressor.Wrap(data).ToArray();
    }

    private static byte[] ZstdDecompress(byte[] data, long expectedSize)
    {
        using ZstdSharp.Decompressor decompressor = new();
        if (expectedSize is > 0 and <= int.MaxValue)
        {
            byte[] output = new byte[(int)expectedSize];
            int written = decompressor.Unwrap(data, output, 0);
            if (written != output.Length)
            {
                throw new InvalidDataException(
                    $"zstd block decoded to {written} bytes but its compression attribute declares {output.Length}.");
            }
            return output;
        }
        return decompressor.Unwrap(data).ToArray();
    }
}
