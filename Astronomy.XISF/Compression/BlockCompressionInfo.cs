using System.Globalization;

namespace Astronomy.XISF.Compression;

/// <summary>The compression codec applied to an XISF data block.</summary>
public enum BlockCodec
{
    /// <summary>Uncompressed.</summary>
    None,

    /// <summary>zlib with byte-shuffle (<c>"zlib+sh"</c>) — what this library produces for multi-byte samples.</summary>
    ZlibSh,

    /// <summary>Plain zlib, no shuffle (<c>"zlib"</c>).</summary>
    Zlib,

    /// <summary>A codec this library does not produce (e.g. lz4 / zstd) — detected on read only.</summary>
    Other,
}

/// <summary>
/// Parsed/formatted view of an XISF image block's <c>compression</c> and <c>checksum</c> attributes.
/// Shared by the read-side detector ("is this block already compressed?") and the codec writer
/// (<see cref="XisfBlockCompression"/>). Pure and UI-free; reusable by any consumer of XISF data blocks.
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

    /// <summary>Checksum algorithm token (e.g. "sha-1"), or "" when none.</summary>
    public string ChecksumName { get; init; }

    /// <summary>Lowercase-hex checksum digest, or "" when none.</summary>
    public string ChecksumHex { get; init; }

    /// <summary>True when the block is compressed (codec is not <see cref="BlockCodec.None"/>).</summary>
    public bool IsCompressed => Codec != BlockCodec.None;

    /// <summary>True when a checksum is present.</summary>
    public bool HasChecksum => !string.IsNullOrEmpty(ChecksumName);

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
    /// <para><c>compression</c> grammar: <c>codec:uncompressedSize[:itemSize]</c> (itemSize present only for "+sh").</para>
    /// <para><c>checksum</c> grammar: <c>algorithm:hexDigest</c>.</para>
    /// </summary>
    public static BlockCompressionInfo Parse(string? compressionAttr, string? checksumAttr)
    {
        string checksumName = string.Empty;
        string checksumHex = string.Empty;
        if (!string.IsNullOrWhiteSpace(checksumAttr))
        {
            string[] c = checksumAttr.Split(':');
            checksumName = c[0].Trim().ToLowerInvariant();
            checksumHex = c.Length > 1 ? c[1].Trim().ToLowerInvariant() : string.Empty;
        }

        if (string.IsNullOrWhiteSpace(compressionAttr))
        {
            return new BlockCompressionInfo
            {
                Codec = BlockCodec.None,
                CodecName = string.Empty,
                UncompressedSize = 0,
                ItemSize = 1,
                ChecksumName = checksumName,
                ChecksumHex = checksumHex
            };
        }

        string[] parts = compressionAttr.Split(':');
        string codecName = parts[0].Trim().ToLowerInvariant();

        long uncompressedSize = 0;
        if (parts.Length > 1)
            long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uncompressedSize);

        int itemSize = 1;
        if (parts.Length > 2)
            int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out itemSize);
        if (itemSize < 1) itemSize = 1;

        BlockCodec codec = codecName switch
        {
            "zlib+sh" => BlockCodec.ZlibSh,
            "zlib" => BlockCodec.Zlib,
            _ => BlockCodec.Other
        };

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
    public string? ToCompressionAttribute() => Codec switch
    {
        BlockCodec.ZlibSh => string.Create(CultureInfo.InvariantCulture, $"zlib+sh:{UncompressedSize}:{ItemSize}"),
        BlockCodec.Zlib => string.Create(CultureInfo.InvariantCulture, $"zlib:{UncompressedSize}"),
        _ => null
    };

    /// <summary>The <c>checksum</c> attribute value to write, or null when none.</summary>
    public string? ToChecksumAttribute() =>
        HasChecksum ? $"{ChecksumName}:{ChecksumHex}" : null;
}
