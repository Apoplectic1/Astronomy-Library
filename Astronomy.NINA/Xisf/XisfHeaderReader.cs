using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Astronomy.NINA.Xisf;

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

        Dictionary<string, string> raw = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement kw in doc.Descendants(ns + "FITSKeyword"))
        {
            string? name = kw.Attribute("name")?.Value;
            string? value = kw.Attribute("value")?.Value;
            if (name is null || value is null) continue;

            // FITS string values arrive as single-quoted with FITS-pad whitespace:
            //   value="'M51     '"  → "M51"
            // Numeric values are unquoted and pass through unchanged.
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1].Trim();
            }

            raw[name] = value;
        }

        return new XisfHeader(raw);
    }
}
