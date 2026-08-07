using System.Xml.Linq;
using Astronomy.XISF.Compression;

namespace Astronomy.XISF;

/// <summary>Verdict of a block-integrity check: the three states a caller reports distinctly.</summary>
public enum XisfChecksumVerdict
{
    /// <summary>The stored block's recomputed digest matches the declared checksum.</summary>
    Verified,

    /// <summary>The block declares no checksum — integrity cannot be confirmed or denied.</summary>
    NoChecksum,

    /// <summary>The recomputed digest disagrees with the declared checksum — the stored bytes are corrupt
    /// (or the declaration is wrong).</summary>
    Mismatch,
}

/// <summary>Outcome of <see cref="XisfChecksumVerifier.VerifyAsync"/> for one file.</summary>
public sealed class XisfChecksumResult
{
    /// <summary>The verdict; see <see cref="XisfChecksumVerdict"/>.</summary>
    public required XisfChecksumVerdict Verdict { get; init; }

    /// <summary>The block's declared storage metadata (codec, sizes, checksum algorithm/hex).</summary>
    public required BlockCompressionInfo Compression { get; init; }

    /// <summary>On <see cref="XisfChecksumVerdict.Mismatch"/>: declared vs computed digests; otherwise null.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Integrity check for a monolithic XISF file's primary image block: locate the attachment, hash the
/// stored bytes, compare against the declared checksum. Deliberately does <b>not</b> decompress or
/// allocate pixels — strictly cheaper than <see cref="XisfImageReader.ReadImageAsync"/>, whose read path
/// verifies as a side effect. Read-only; never mutates the file.
///
/// Verification is a detection operation, so a digest disagreement is a <em>result</em>
/// (<see cref="XisfChecksumVerdict.Mismatch"/>), not an exception — callers inventorying a library
/// report it and continue. Structural violations (bad signature/XML, missing attributes, attachment past
/// end of file) still throw: those files cannot be meaningfully verified at all.
/// </summary>
public static class XisfChecksumVerifier
{
    /// <summary>
    /// Verifies the primary image block of the XISF file at <paramref name="filePath"/> against its
    /// declared checksum.
    /// </summary>
    /// <param name="filePath">Absolute path to a .xisf file.</param>
    /// <param name="ct">Cancellation token; observed at the file I/O boundaries.</param>
    /// <exception cref="InvalidDataException">
    /// The file violates the XISF structural contract (bad signature/XML, no image, malformed
    /// location/compression metadata, attachment out of file range) — distinct from a checksum
    /// mismatch, which is reported as a verdict.
    /// </exception>
    public static async Task<XisfChecksumResult> VerifyAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        await using FileStream fs = new(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        (XDocument doc, XNamespace ns) = await XisfXmlLoader.LoadAsync(fs, filePath, ct).ConfigureAwait(false);

        XElement imageEl = doc.Descendants(ns + "Image").FirstOrDefault()
            ?? throw new InvalidDataException(
                $"XISF has no <Image> element (expected the mandatory primary image) at '{filePath}'.");

        (long offset, long size) = XisfImageReader.ParseLocation(imageEl.Attribute("location")?.Value, filePath);

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

        if (!compression.HasChecksum)
        {
            return new XisfChecksumResult { Verdict = XisfChecksumVerdict.NoChecksum, Compression = compression };
        }

        byte[] stored = new byte[size];
        fs.Seek(offset, SeekOrigin.Begin);
        await fs.ReadExactlyAsync(stored, 0, (int)size, ct).ConfigureAwait(false);

        string computed = XisfBlockCompression.ComputeChecksumHex(compression.ChecksumName, stored);
        if (string.Equals(computed, compression.ChecksumHex, StringComparison.OrdinalIgnoreCase))
        {
            return new XisfChecksumResult { Verdict = XisfChecksumVerdict.Verified, Compression = compression };
        }

        return new XisfChecksumResult
        {
            Verdict = XisfChecksumVerdict.Mismatch,
            Compression = compression,
            Detail = $"declared {compression.ChecksumName}:{compression.ChecksumHex}, computed {computed}",
        };
    }
}
