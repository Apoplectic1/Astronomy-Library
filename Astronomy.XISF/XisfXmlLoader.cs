using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Astronomy.XISF;

/// <summary>
/// Shared monolithic-XISF front matter loader: validates the 16-byte signature block and parses the
/// embedded XML header. Used by both the header reader and the image reader so the two never drift on
/// signature/XML handling. Never reads past the XML section.
/// </summary>
internal static class XisfXmlLoader
{
    private const int SignatureSize = 16;
    private const string XisfSignature = "XISF0100";
    private const int MaxXmlSize = 16 * 1024 * 1024;  // 16 MiB — sanity bound on XML header

    /// <summary>
    /// Reads signature + XML from <paramref name="fs"/> (positioned at 0) and returns the parsed document
    /// with its default namespace. Leaves <paramref name="fs"/> positioned at the end of the XML section.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Bad signature, implausible XML section size, or malformed XML.
    /// </exception>
    internal static async Task<(XDocument Doc, XNamespace Ns)> LoadAsync(
        FileStream fs, string filePath, CancellationToken ct)
    {
        (XDocument doc, XNamespace ns, _, _) = await LoadWithTextAsync(fs, filePath, ct).ConfigureAwait(false);
        return (doc, ns);
    }

    /// <summary>
    /// As <see cref="LoadAsync"/>, but also returns the XML section's exact text and its on-disk byte
    /// length — for callers that must edit the header without re-serializing it (byte-preserving rewrites).
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Bad signature, implausible XML section size, or malformed XML.
    /// </exception>
    internal static async Task<(XDocument Doc, XNamespace Ns, string XmlText, int XmlByteLength)> LoadWithTextAsync(
        FileStream fs, string filePath, CancellationToken ct)
    {
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

        XElement root = doc.Root
            ?? throw new InvalidDataException($"XISF XML has no root element at '{filePath}'.");

        return (doc, root.GetDefaultNamespace(), xmlString, xmlSize);
    }
}
