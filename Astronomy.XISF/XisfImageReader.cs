using System.Globalization;
using System.Xml.Linq;
using Astronomy.XISF.Compression;

namespace Astronomy.XISF;

/// <summary>
/// The primary image of a monolithic XISF file, materialized: raw pixel bytes plus the metadata a caller
/// needs to interpret every sample. Pixels are exactly the decoded attachment — no format conversion,
/// normalization, or channel reordering.
/// </summary>
public sealed class XisfImageData
{
    /// <summary>Decoded (decompressed, unshuffled) pixel bytes of the primary image block.</summary>
    public required byte[] Pixels { get; init; }

    /// <summary>Image width in pixels (geometry's first field).</summary>
    public required int Width { get; init; }

    /// <summary>Image height in pixels (geometry's second field).</summary>
    public required int Height { get; init; }

    /// <summary>Channel count (geometry's third field; 1 for mono, 3 for RGB).</summary>
    public required int Channels { get; init; }

    /// <summary>The XISF <c>sampleFormat</c> token as written (e.g. "UInt16", "Float32").</summary>
    public required string SampleFormat { get; init; }

    /// <summary>Bytes per sample implied by <see cref="SampleFormat"/>.</summary>
    public required int BytesPerSample { get; init; }

    /// <summary>How the block was stored on disk (codec, sizes, checksum) — informational for callers.</summary>
    public required BlockCompressionInfo Compression { get; init; }
}

/// <summary>
/// Reads the primary image's pixel data out of a monolithic XISF file: locates the image's attached data
/// block, verifies its declared checksum, decompresses through <see cref="XisfBlockCompression"/>, and
/// returns the buffer with its geometry/sample metadata. Read-only — never mutates the file. Reading
/// pixels is deliberately a separate operation from <see cref="XisfHeaderReader.ReadAsync"/>, which stays
/// metadata-only.
/// </summary>
public static class XisfImageReader
{
    /// <summary>
    /// Reads and decodes the primary image of the XISF file at <paramref name="filePath"/>.
    /// </summary>
    /// <param name="filePath">Absolute path to a .xisf file.</param>
    /// <param name="ct">Cancellation token; observed at the file I/O boundaries.</param>
    /// <exception cref="InvalidDataException">
    /// The file violates the XISF structural contract: bad signature/XML, missing image or attributes,
    /// malformed location/compression/checksum metadata, attachment out of file range, checksum mismatch,
    /// or a decoded size that disagrees with the declared geometry.
    /// </exception>
    public static async Task<XisfImageData> ReadImageAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        await using FileStream fs = new(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        (XDocument doc, XNamespace ns) = await XisfXmlLoader.LoadAsync(fs, filePath, ct).ConfigureAwait(false);

        XElement imageEl = doc.Descendants(ns + "Image").FirstOrDefault()
            ?? throw new InvalidDataException(
                $"XISF has no <Image> element (expected the mandatory primary image) at '{filePath}'.");

        (int width, int height, int channels) = ParseGeometry(imageEl.Attribute("geometry")?.Value, filePath);
        (string sampleFormat, int bytesPerSample) = ParseSampleFormat(imageEl.Attribute("sampleFormat")?.Value, filePath);
        (long offset, long size) = ParseLocation(imageEl.Attribute("location")?.Value, filePath);

        if (offset + size > fs.Length)
        {
            throw new InvalidDataException(
                $"XISF <Image> attachment (offset {offset}, size {size}) extends past the end of the "
                + $"{fs.Length}-byte file at '{filePath}' — truncated or corrupt.");
        }

        BlockCompressionInfo compression;
        try
        {
            compression = BlockCompressionInfo.Parse(
                imageEl.Attribute("compression")?.Value, imageEl.Attribute("checksum")?.Value);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{ex.Message} (XISF <Image> at '{filePath}')", ex);
        }

        byte[] stored = new byte[size];
        fs.Seek(offset, SeekOrigin.Begin);
        await fs.ReadExactlyAsync(stored, 0, (int)size, ct).ConfigureAwait(false);

        byte[] pixels;
        try
        {
            XisfBlockCompression.VerifyChecksum(stored, in compression);
            pixels = compression.IsCompressed
                ? XisfBlockCompression.Decompress(stored, compression)
                : stored;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{ex.Message} (XISF <Image> attachment at '{filePath}')", ex);
        }

        long expectedBytes = (long)width * height * channels * bytesPerSample;
        if (pixels.LongLength != expectedBytes)
        {
            throw new InvalidDataException(
                $"XISF <Image> pixel data is {pixels.LongLength} bytes but geometry "
                + $"{width}:{height}:{channels} × {bytesPerSample}-byte {sampleFormat} samples requires "
                + $"{expectedBytes} at '{filePath}'.");
        }

        return new XisfImageData
        {
            Pixels = pixels,
            Width = width,
            Height = height,
            Channels = channels,
            SampleFormat = sampleFormat,
            BytesPerSample = bytesPerSample,
            Compression = compression,
        };
    }

    // "width:height:channels" — all three mandatory and positive for the pixel-read contract
    // (the header reader's metadata-only path needs just width/height and keeps its laxer parse).
    // Internal: shared with XisfBlockRewriter, which validates the same primary-image contract.
    internal static (int Width, int Height, int Channels) ParseGeometry(string? geometry, string filePath)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            throw new InvalidDataException(
                $"XISF <Image> has no 'geometry' attribute (expected \"width:height:channels\") at '{filePath}'.");
        }

        string[] parts = geometry.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int channels)
            || width <= 0 || height <= 0 || channels <= 0)
        {
            throw new InvalidDataException(
                $"XISF <Image> geometry '{geometry}' is not \"width:height:channels\" with positive "
                + $"dimensions at '{filePath}'.");
        }

        return (width, height, channels);
    }

    // Internal: shared with XisfBlockRewriter, which needs the shuffle item size for re-encoding.
    internal static (string Token, int BytesPerSample) ParseSampleFormat(string? sampleFormat, string filePath)
    {
        if (string.IsNullOrWhiteSpace(sampleFormat))
        {
            throw new InvalidDataException(
                $"XISF <Image> has no 'sampleFormat' attribute at '{filePath}'.");
        }

        string token = sampleFormat.Trim();
        int bytes = token switch
        {
            "UInt8" => 1,
            "UInt16" => 2,
            "UInt32" => 4,
            "UInt64" => 8,
            "Float32" => 4,
            "Float64" => 8,
            "Complex32" => 8,
            "Complex64" => 16,
            _ => throw new InvalidDataException(
                $"XISF <Image> sampleFormat '{token}' is not a sample format the XISF spec defines at '{filePath}'."),
        };
        return (token, bytes);
    }

    // "attachment:offset:size" with positive integers; anything else (including inline/embedded
    // locations, which no supported producer emits for the primary image) fails fast.
    // Internal: shared with XisfChecksumVerifier, which locates the same primary-image block.
    internal static (long Offset, long Size) ParseLocation(string? location, string filePath)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new InvalidDataException(
                $"XISF <Image> has no 'location' attribute (expected \"attachment:offset:size\") at '{filePath}'.");
        }

        string[] parts = location.Split(':');
        if (parts.Length != 3
            || !string.Equals(parts[0].Trim(), "attachment", StringComparison.OrdinalIgnoreCase)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long offset)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size)
            || offset <= 0 || size <= 0 || size > int.MaxValue)
        {
            throw new InvalidDataException(
                $"XISF <Image> location '{location}' is not \"attachment:offset:size\" with positive "
                + $"in-range values at '{filePath}'.");
        }

        return (offset, size);
    }
}
