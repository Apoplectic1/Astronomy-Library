using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Astronomy.XISF;

/// <summary>
/// Reads FITS-keyword headers from XISF files. Header-only — never touches image
/// data, never mutates the file. Pure managed (System.Xml.Linq), no native deps.
/// </summary>
/// <remarks>
/// <para>
/// Ported from XisfFileManager's <c>XisfXmlReader.cs</c> read path, stripped of
/// write/mutation logic and XFM-specific UI concerns. Reads only XFM-processed
/// files in practice; pre-XFM files with malformed XML will throw
/// <see cref="InvalidDataException"/> — callers should catch + skip.
/// </para>
/// <para>
/// The XISF file layout this reader assumes:
/// <list type="number">
///   <item>Bytes 0–7: ASCII signature <c>"XISF0100"</c>.</item>
///   <item>Bytes 8–11: little-endian unsigned 32-bit XML section byte length.</item>
///   <item>Bytes 12–15: reserved (skipped).</item>
///   <item>Bytes 16..16+xmlLen: UTF-8 XML header (the FITS keyword bag lives here).</item>
///   <item>After that: image attachment blocks (NOT read by this class).</item>
/// </list>
/// </para>
/// </remarks>
public static class XisfHeaderReader
{
    private const int SignatureSize = 16;
    private const string XisfSignature = "XISF0100";
    private const int MaxXmlSize = 16 * 1024 * 1024;  // 16 MiB — sanity bound on XML header

    /// <summary>
    /// Reads the XISF header at <paramref name="filePath"/> and returns a
    /// <see cref="XisfHeader"/> populated from the embedded FITS keywords.
    /// </summary>
    /// <param name="filePath">Absolute path to a .xisf file.</param>
    /// <param name="ct">Cancellation token; observed at the file I/O boundary.</param>
    /// <exception cref="InvalidDataException">
    /// File is not a valid XISF (bad signature, malformed XML, or XML header too large).
    /// </exception>
    public static async Task<XisfHeader> ReadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        await using FileStream fs = new(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);

        // Signature + XML length header (first 16 bytes).
        byte[] sigBuf = new byte[SignatureSize];
        await fs.ReadExactlyAsync(sigBuf, 0, SignatureSize, ct).ConfigureAwait(false);

        string sig = Encoding.ASCII.GetString(sigBuf, 0, 8);
        if (sig != XisfSignature)
        {
            throw new InvalidDataException(
                $"Not an XISF file: signature '{sig}' (expected '{XisfSignature}') at '{filePath}'.");
        }

        // Bytes 8–11: little-endian uint32. XFM's reader comment claims "big-endian"
        // but the actual XISF spec + XFM's own bit-shift order is little-endian.
        int xmlSize = sigBuf[8]
                    | (sigBuf[9] << 8)
                    | (sigBuf[10] << 16)
                    | (sigBuf[11] << 24);

        if (xmlSize <= 0 || xmlSize > MaxXmlSize)
        {
            throw new InvalidDataException(
                $"Invalid XISF XML section size {xmlSize} at '{filePath}'.");
        }

        byte[] xmlBuf = new byte[xmlSize];
        await fs.ReadExactlyAsync(xmlBuf, 0, xmlSize, ct).ConfigureAwait(false);
        string xmlString = Encoding.UTF8.GetString(xmlBuf);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlString, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                $"Failed to parse XISF XML at '{filePath}': {ex.Message}", ex);
        }

        XElement? root = doc.Root
            ?? throw new InvalidDataException($"XISF XML has no root element at '{filePath}'.");
        XNamespace ns = root.GetDefaultNamespace();

        // The image's pixel dimensions live on the <Image> element's `geometry` attribute
        // ("width:height:channels"), NOT in the FITS keyword bag below: NAXIS1/NAXIS2 are optional
        // duplicates a writer may omit (measured absent on 63 of 18,650 real frames, while geometry was
        // present on all of them and never disagreed). geometry is mandatory per the XISF spec, so its
        // absence means a malformed file — the same category as bad XML, and reported the same way.
        XElement imageEl = doc.Descendants(ns + "Image").FirstOrDefault()
            ?? throw new InvalidDataException(
                $"XISF has no <Image> element (expected the mandatory image geometry) at '{filePath}'.");

        (int width, int height) = ParseGeometry(imageEl.Attribute("geometry")?.Value, filePath);

        Dictionary<string, XisfHeader.KeywordEntry> raw = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement kw in doc.Descendants(ns + "FITSKeyword"))
        {
            string? name = kw.Attribute("name")?.Value;
            string? value = kw.Attribute("value")?.Value;
            string? comment = kw.Attribute("comment")?.Value;
            if (name is null || value is null) continue;

            // FITS string values arrive as single-quoted with FITS-pad whitespace:
            //   value="'M51     '"  → "M51"
            // Numeric values are unquoted and pass through unchanged.
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1].Trim();
            }

            // Comment: trim; treat empty/whitespace as absent (null) rather than empty string.
            string? normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            raw[name] = new XisfHeader.KeywordEntry(value, normalizedComment);
        }

        return new XisfHeader(raw, width, height);
    }

    // "width:height:channels" -> (width, height). Anything else is a malformed XISF: the attribute is
    // mandatory, so there is nothing to fall back to and no dimensions worth guessing.
    private static (int Width, int Height) ParseGeometry(string? geometry, string filePath)
    {
        if (string.IsNullOrWhiteSpace(geometry))
        {
            throw new InvalidDataException(
                $"XISF <Image> has no 'geometry' attribute (expected \"width:height:channels\") at '{filePath}'.");
        }

        string[] parts = geometry.Split(':');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            || width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                $"XISF <Image> geometry '{geometry}' is not \"width:height:channels\" with positive "
                + $"dimensions at '{filePath}'.");
        }

        return (width, height);
    }
}
