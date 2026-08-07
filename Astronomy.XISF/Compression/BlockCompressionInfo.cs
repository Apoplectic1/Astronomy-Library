using System.Globalization;

namespace Astronomy.XISF.Compression;

/// <summary>The compression codec applied to an XISF data block.</summary>
public enum BlockCodec
{
    /// <summary>Uncompressed.</summary>
    None,

    /// <summary>Plain zlib, no shuffle (<c>"zlib"</c>).</summary>
    Zlib,

    /// <summary>zlib with byte-shuffle (<c>"zlib+sh"</c>).</summary>
    ZlibSh,

    /// <summary>LZ4 fast, raw block format (<c>"lz4"</c>).</summary>
    Lz4,

    /// <summary>LZ4 fast with byte-shuffle (<c>"lz4+sh"</c>).</summary>
    Lz4Sh,

    /// <summary>LZ4 high-compression, raw block format (<c>"lz4hc"</c>).</summary>
    Lz4Hc,

    /// <summary>LZ4 high-compression with byte-shuffle (<c>"lz4hc+sh"</c>).</summary>
    Lz4HcSh,

    /// <summary>Zstandard (<c>"zstd"</c>).</summary>
    Zstd,

    /// <summary>Zstandard with byte-shuffle (<c>"zstd+sh"</c>).</summary>
    ZstdSh,

    /// <summary>A codec token this library does not recognize — detected on read only; decoding it throws.</summary>
    Other,
}

/// <summary>
/// Parsed/formatted view of an XISF data block's <c>compression</c> and <c>checksum</c> attributes.
/// Shared by the read-side detector ("is this block already compressed?"), the codec
/// (<see cref="XisfBlockCompression"/>), and the image-read path. Pure and UI-free; reusable by any
/// consumer of XISF data blocks.
/// </summary>
public readonly struct BlockCompressionInfo
{
    /// <summary>The codec applied to the block.</summary>
    public BlockCodec Codec { get; init; }

    /// <summary>Raw codec token as written in the file (e.g. "zlib+sh"), or "" when uncompressed.</summary>
    public string CodecName { get; init; }

    /// <summary>Uncompressed block size in bytes (the value after the codec token).</summary>
    public long UncompressedSize { get; init; }

    /// <summary>Bytes-per-sample shuffle item size; 1 when there is no shuffle.</summary>
    public int ItemSize { get; init; }

    /// <summary>
    /// Checksum algorithm token in canonical hyphenated form (e.g. "sha-1"), or "" when none.
    /// <see cref="Parse"/> canonicalizes the legacy non-hyphenated aliases ("sha1"/"sha256"/"sha512")
    /// emitted by older producers, so a re-save writes the spec token.
    /// </summary>
    public string ChecksumName { get; init; }

    /// <summary>Lowercase-hex checksum digest, or "" when none.</summary>
    public string ChecksumHex { get; init; }

    /// <summary>True when the block is compressed (codec is not <see cref="BlockCodec.None"/>).</summary>
    public bool IsCompressed => Codec != BlockCodec.None;

    /// <summary>True when a checksum is present.</summary>
    public bool HasChecksum => !string.IsNullOrEmpty(ChecksumName);

    /// <summary>True when the codec is a byte-shuffled (<c>+sh</c>) variant.</summary>
    public bool IsShuffled => Codec is BlockCodec.ZlibSh or BlockCodec.Lz4Sh or BlockCodec.Lz4HcSh or BlockCodec.ZstdSh;

    /// <summary>An uncompressed, checksum-less descriptor.</summary>
    public static BlockCompressionInfo None => new()
    {
        Codec = BlockCodec.None,
        CodecName = string.Empty,
        UncompressedSize = 0,
        ItemSize = 1,
        ChecksumName = string.Empty,
        ChecksumHex = string.Empty
    };

    /// <summary>
    /// Parse the raw <c>compression</c> and <c>checksum</c> attribute strings. Either may be null/empty.
    /// <para><c>compression</c> grammar: <c>codec:uncompressedSize[:itemSize]</c> (itemSize present only
    /// for "+sh" variants).</para>
    /// <para><c>checksum</c> grammar: <c>algorithm:hexDigest</c>. Legacy non-hyphenated algorithm
    /// aliases ("sha1"/"sha256"/"sha512") are canonicalized to the spec tokens.</para>
    /// An unrecognized codec token parses as <see cref="BlockCodec.Other"/> (read-side detection must not
    /// abort on codecs this library does not decode), but a malformed attribute for a <em>known</em> codec —
    /// wrong field count (including sub-block forms, which no supported producer emits), non-numeric or
    /// non-positive sizes — is a contract violation and throws.
    /// </summary>
    /// <exception cref="InvalidDataException">A malformed attribute (see above).</exception>
    public static BlockCompressionInfo Parse(string? compressionAttr, string? checksumAttr)
    {
        string checksumName = string.Empty;
        string checksumHex = string.Empty;
        if (!string.IsNullOrWhiteSpace(checksumAttr))
        {
            string[] c = checksumAttr.Split(':');
            if (c.Length != 2 || string.IsNullOrWhiteSpace(c[0]) || string.IsNullOrWhiteSpace(c[1]))
            {
                throw new InvalidDataException(
                    $"Malformed XISF checksum attribute '{checksumAttr}' (expected \"algorithm:hexDigest\").");
            }
            checksumName = CanonicalChecksumToken(c[0].Trim().ToLowerInvariant());
            checksumHex = c[1].Trim().ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(compressionAttr))
        {
            return None with { ChecksumName = checksumName, ChecksumHex = checksumHex };
        }

        string[] parts = compressionAttr.Split(':');
        string codecName = parts[0].Trim().ToLowerInvariant();
        BlockCodec codec = CodecFromToken(codecName);

        if (codec == BlockCodec.Other)
        {
            // Not ours to validate: preserve the token (and size when it happens to parse) so read-side
            // detection can still report IsCompressed; decoding this block throws in Decompress.
            long size = 0;
            if (parts.Length > 1)
                _ = long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            return new BlockCompressionInfo
            {
                Codec = BlockCodec.Other,
                CodecName = codecName,
                UncompressedSize = size,
                ItemSize = 1,
                ChecksumName = checksumName,
                ChecksumHex = checksumHex
            };
        }

        bool shuffled = IsShuffledCodec(codec);
        int expectedParts = shuffled ? 3 : 2;
        if (parts.Length != expectedParts)
        {
            throw new InvalidDataException(
                $"Malformed XISF compression attribute '{compressionAttr}': codec '{codecName}' expects "
                + $"{expectedParts} ':'-separated fields, got {parts.Length} (sub-block forms are not supported).");
        }

        if (!long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long uncompressedSize)
            || uncompressedSize <= 0)
        {
            throw new InvalidDataException(
                $"Malformed XISF compression attribute '{compressionAttr}': uncompressed size "
                + $"'{parts[1].Trim()}' is not a positive integer.");
        }

        int itemSize = 1;
        if (shuffled
            && (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out itemSize)
                || itemSize < 1))
        {
            throw new InvalidDataException(
                $"Malformed XISF compression attribute '{compressionAttr}': shuffle item size "
                + $"'{parts[2].Trim()}' is not a positive integer.");
        }

        return new BlockCompressionInfo
        {
            Codec = codec,
            CodecName = codecName,
            UncompressedSize = uncompressedSize,
            ItemSize = itemSize,
            ChecksumName = checksumName,
            ChecksumHex = checksumHex
        };
    }

    /// <summary>The <c>compression</c> attribute value to write, or null when uncompressed.</summary>
    /// <exception cref="InvalidOperationException">
    /// The codec is <see cref="BlockCodec.Other"/> — this library never produces a block it cannot name.
    /// </exception>
    public string? ToCompressionAttribute() => Codec switch
    {
        BlockCodec.None => null,
        BlockCodec.Other => throw new InvalidOperationException(
            $"Cannot format a compression attribute for unrecognized codec '{CodecName}'."),
        _ when IsShuffled => string.Create(
            CultureInfo.InvariantCulture, $"{TokenFromCodec(Codec)}:{UncompressedSize}:{ItemSize}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{TokenFromCodec(Codec)}:{UncompressedSize}"),
    };

    /// <summary>The <c>checksum</c> attribute value to write, or null when none.</summary>
    public string? ToChecksumAttribute() =>
        HasChecksum ? $"{ChecksumName}:{ChecksumHex}" : null;

    // Legacy non-hyphenated aliases (old PixInsight/SGP-era producers) map to the XISF spec tokens;
    // anything else passes through untouched for ComputeChecksumHex to accept or reject.
    private static string CanonicalChecksumToken(string token) => token switch
    {
        "sha1" => "sha-1",
        "sha256" => "sha-256",
        "sha512" => "sha-512",
        _ => token
    };

    private static BlockCodec CodecFromToken(string token) => token switch
    {
        "zlib" => BlockCodec.Zlib,
        "zlib+sh" => BlockCodec.ZlibSh,
        "lz4" => BlockCodec.Lz4,
        "lz4+sh" => BlockCodec.Lz4Sh,
        "lz4hc" => BlockCodec.Lz4Hc,
        "lz4hc+sh" => BlockCodec.Lz4HcSh,
        "zstd" => BlockCodec.Zstd,
        "zstd+sh" => BlockCodec.ZstdSh,
        _ => BlockCodec.Other
    };

    internal static string TokenFromCodec(BlockCodec codec) => codec switch
    {
        BlockCodec.Zlib => "zlib",
        BlockCodec.ZlibSh => "zlib+sh",
        BlockCodec.Lz4 => "lz4",
        BlockCodec.Lz4Sh => "lz4+sh",
        BlockCodec.Lz4Hc => "lz4hc",
        BlockCodec.Lz4HcSh => "lz4hc+sh",
        BlockCodec.Zstd => "zstd",
        BlockCodec.ZstdSh => "zstd+sh",
        _ => throw new InvalidOperationException($"Codec {codec} has no XISF token.")
    };

    private static bool IsShuffledCodec(BlockCodec codec) =>
        codec is BlockCodec.ZlibSh or BlockCodec.Lz4Sh or BlockCodec.Lz4HcSh or BlockCodec.ZstdSh;
}
