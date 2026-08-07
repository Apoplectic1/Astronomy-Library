using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Astronomy.XISF.Compression;

namespace Astronomy.XISF;

/// <summary>
/// Result of <see cref="XisfBlockRewriter.RewriteAsync"/>: how and where the primary image block is
/// stored in the written file. Callers holding cached geometry for the source file must refresh it from
/// here after an in-place rewrite — the block's offset, size, and compression state all change.
/// </summary>
public sealed class XisfBlockRewriteResult
{
    /// <summary>Storage descriptor of the written primary block (codec, sizes, checksum); <see cref="BlockCompressionInfo.None"/> when written uncompressed.</summary>
    public required BlockCompressionInfo Compression { get; init; }

    /// <summary>Absolute file offset of the written primary image block.</summary>
    public required long AttachmentOffset { get; init; }

    /// <summary>Stored byte length of the written primary image block.</summary>
    public required long AttachmentSize { get; init; }

    /// <summary>Byte length of the written XML header section.</summary>
    public required int XmlLength { get; init; }
}

/// <summary>
/// Surgical block rewrite for a monolithic XISF file: re-store the primary image block under a different
/// codec while preserving the XML header byte-for-byte except the attributes the block change forces —
/// the primary image's <c>compression</c>/<c>checksum</c>, every shifted attachment's <c>location</c>,
/// and the signature's XML-length field. No element is added, removed, or re-serialized; all other
/// attachments are copied verbatim at shifted offsets. The write goes to a temporary file finalized by
/// an atomic replace, so the target never exists partially written; when
/// <c>targetPath == sourcePath</c> this is an in-place rewrite.
///
/// A declared source checksum is verified before the block is re-encoded — a corrupt block must fail the
/// rewrite, not be re-certified under a fresh digest. Structural violations (bad signature/XML, missing
/// attributes, out-of-range attachments, unlocatable attribute text) throw; nothing is ever written on
/// failure.
/// </summary>
public static class XisfBlockRewriter
{
    private const int SignatureSize = 16;
    private const int MaxLayoutIterations = 10;

    /// <summary>
    /// Rewrites the XISF file at <paramref name="sourcePath"/> to <paramref name="targetPath"/> with its
    /// primary image block stored under <paramref name="codec"/>.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the source .xisf file.</param>
    /// <param name="targetPath">
    /// Absolute path to write; equal to <paramref name="sourcePath"/> for an in-place rewrite. The write
    /// is temp-file + atomic replace either way.
    /// </param>
    /// <param name="codec">
    /// Target storage: <see cref="BlockCodec.None"/> writes the block uncompressed with no
    /// <c>compression</c>/<c>checksum</c> attributes; a base codec family (Zlib/Lz4/Lz4Hc/Zstd)
    /// compresses via <see cref="XisfBlockCompression.Compress"/> (shuffle chosen from the sample
    /// format's item size) and records a SHA-1 checksum. Passing a "+sh" variant or
    /// <see cref="BlockCodec.Other"/> throws.
    /// </param>
    /// <param name="zstdLevel">Optional zstd encoder level (1–22); only valid with <see cref="BlockCodec.Zstd"/>.</param>
    /// <param name="ct">Cancellation token; observed at the file I/O boundaries.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="codec"/> is not None or a base family, or the level is misused.</exception>
    /// <exception cref="InvalidDataException">
    /// The source violates the XISF structural contract, its declared checksum does not match the stored
    /// bytes, or the header text cannot be edited unambiguously.
    /// </exception>
    public static async Task<XisfBlockRewriteResult> RewriteAsync(
        string sourcePath, string targetPath, BlockCodec codec, int? zstdLevel = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);

        if (codec is not (BlockCodec.None or BlockCodec.Zlib or BlockCodec.Lz4 or BlockCodec.Lz4Hc or BlockCodec.Zstd))
        {
            throw new ArgumentOutOfRangeException(nameof(codec), codec,
                "Pass BlockCodec.None or a base codec family (Zlib/Lz4/Lz4Hc/Zstd); the +sh variant is selected from the sample format.");
        }
        if (zstdLevel is not null && codec is not BlockCodec.Zstd)
        {
            throw new ArgumentOutOfRangeException(nameof(zstdLevel), zstdLevel,
                $"A compression level is only meaningful for {nameof(BlockCodec.Zstd)}.");
        }

        string targetDir = Path.GetDirectoryName(Path.GetFullPath(targetPath))
            ?? throw new ArgumentException($"Target path '{targetPath}' has no directory.", nameof(targetPath));
        string tempPath = Path.Combine(targetDir,
            $".{Path.GetFileName(targetPath)}.rwtmp-{Guid.NewGuid():N}");

        try
        {
            XisfBlockRewriteResult result;

            await using (FileStream source = new(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true))
            {
                (XDocument doc, XNamespace ns, string xmlText, _) =
                    await XisfXmlLoader.LoadWithTextAsync(source, sourcePath, ct).ConfigureAwait(false);

                result = await RewriteCoreAsync(
                    source, doc, ns, xmlText, sourcePath, tempPath, codec, zstdLevel, ct).ConfigureAwait(false);
            }

            // Source handle is closed; atomically replace (or create) the target.
            File.Move(tempPath, targetPath, overwrite: true);
            return result;
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }
            throw;
        }
    }

    private sealed class Attachment
    {
        public required string OriginalLocationValue { get; init; }
        public required long Offset { get; init; }
        public required long Size { get; init; }
        public required bool IsPrimary { get; init; }
        public long NewOffset { get; set; }
        public long NewSize { get; set; }
    }

    private static async Task<XisfBlockRewriteResult> RewriteCoreAsync(
        FileStream source, XDocument doc, XNamespace ns, string xmlText,
        string sourcePath, string tempPath, BlockCodec codec, int? zstdLevel, CancellationToken ct)
    {
        XElement imageEl = doc.Descendants(ns + "Image").FirstOrDefault()
            ?? throw new InvalidDataException(
                $"XISF has no <Image> element (expected the mandatory primary image) at '{sourcePath}'.");

        string primaryLocationValue = imageEl.Attribute("location")?.Value
            ?? throw new InvalidDataException(
                $"XISF <Image> has no 'location' attribute (expected \"attachment:offset:size\") at '{sourcePath}'.");

        (int width, int height, int channels) =
            XisfImageReader.ParseGeometry(imageEl.Attribute("geometry")?.Value, sourcePath);
        (string sampleFormat, int bytesPerSample) =
            XisfImageReader.ParseSampleFormat(imageEl.Attribute("sampleFormat")?.Value, sourcePath);

        BlockCompressionInfo sourceInfo;
        try
        {
            sourceInfo = BlockCompressionInfo.Parse(
                imageEl.Attribute("compression")?.Value, imageEl.Attribute("checksum")?.Value);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{ex.Message} (XISF <Image> at '{sourcePath}')", ex);
        }

        // Every attached data block in the file, in file order. Elements are matched back to the header
        // text by their location attribute value, which is unique per attachment (distinct offsets).
        List<Attachment> attachments = CollectAttachments(doc, primaryLocationValue, source.Length, sourcePath);
        Attachment primary = attachments.Single(a => a.IsPrimary);

        // Read and re-encode the primary block.
        byte[] stored = new byte[primary.Size];
        source.Seek(primary.Offset, SeekOrigin.Begin);
        await source.ReadExactlyAsync(stored, 0, stored.Length, ct).ConfigureAwait(false);

        try
        {
            XisfBlockCompression.VerifyChecksum(stored, in sourceInfo);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{ex.Message} (XISF <Image> attachment at '{sourcePath}')", ex);
        }

        byte[] raw = sourceInfo.IsCompressed ? XisfBlockCompression.Decompress(stored, sourceInfo) : stored;

        long expectedBytes = (long)width * height * channels * bytesPerSample;
        if (raw.LongLength != expectedBytes)
        {
            throw new InvalidDataException(
                $"XISF <Image> pixel data is {raw.LongLength} bytes but geometry {width}:{height}:{channels} × "
                + $"{bytesPerSample}-byte {sampleFormat} samples requires {expectedBytes} at '{sourcePath}'.");
        }

        byte[] newBlock;
        BlockCompressionInfo newInfo;
        if (codec == BlockCodec.None)
        {
            newBlock = raw;
            newInfo = BlockCompressionInfo.None;
        }
        else
        {
            BlockCompressionResult compressed = XisfBlockCompression.Compress(raw, bytesPerSample, codec, zstdLevel);
            newBlock = compressed.CompressedBytes;
            newInfo = compressed.Info;
        }
        primary.NewSize = newBlock.Length;

        // Converge the header text and the attachment layout: location digit counts feed the XML length,
        // which feeds the offsets, which feed the digit counts.
        int alignment = ReadBlockAlignment(doc, ns);
        string newXmlText = LayoutAndEditHeader(
            xmlText, attachments, primary, newInfo, alignment, sourcePath);
        int newXmlLength = Encoding.UTF8.GetByteCount(newXmlText);

        await WriteFileAsync(source, tempPath, newXmlText, newXmlLength, attachments, newBlock, ct)
            .ConfigureAwait(false);

        return new XisfBlockRewriteResult
        {
            Compression = newInfo,
            AttachmentOffset = primary.NewOffset,
            AttachmentSize = primary.NewSize,
            XmlLength = newXmlLength,
        };
    }

    private static List<Attachment> CollectAttachments(
        XDocument doc, string primaryLocationValue, long fileLength, string sourcePath)
    {
        List<Attachment> attachments = [];
        foreach (XElement el in doc.Descendants())
        {
            string? value = el.Attribute("location")?.Value;
            if (value is null || !value.TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase))
                continue;

            (long offset, long size) = XisfImageReader.ParseLocation(value, sourcePath);
            if (offset + size > fileLength)
            {
                throw new InvalidDataException(
                    $"XISF attachment (offset {offset}, size {size}) extends past the end of the "
                    + $"{fileLength}-byte file at '{sourcePath}' — truncated or corrupt.");
            }

            attachments.Add(new Attachment
            {
                OriginalLocationValue = value,
                Offset = offset,
                Size = size,
                IsPrimary = ReferenceEquals(value, primaryLocationValue) || value == primaryLocationValue,
                NewOffset = offset,
                NewSize = size,
            });
        }

        if (attachments.Count(a => a.IsPrimary) != 1)
        {
            throw new InvalidDataException(
                $"Cannot rewrite XISF at '{sourcePath}': the primary image's location "
                + $"'{primaryLocationValue}' does not identify exactly one attachment.");
        }

        attachments.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return attachments;
    }

    // The optional XISF:BlockAlignmentSize property declares the writer's attachment alignment;
    // absent (or unusable) means attachments pack immediately after the header.
    private static int ReadBlockAlignment(XDocument doc, XNamespace ns)
    {
        string? value = doc.Descendants(ns + "Property")
            .FirstOrDefault(p => (string?)p.Attribute("id") == "XISF:BlockAlignmentSize")
            ?.Attribute("value")?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int alignment)
               && alignment > 0
            ? alignment
            : 1;
    }

    /// <summary>
    /// Produces the edited header text with converged attachment offsets, mutating each attachment's
    /// NewOffset. Each iteration re-derives the text from the original, so edits never compound.
    /// </summary>
    private static string LayoutAndEditHeader(
        string xmlText, List<Attachment> attachments, Attachment primary,
        BlockCompressionInfo newInfo, int alignment, string sourcePath)
    {
        for (int iteration = 0; iteration < MaxLayoutIterations; iteration++)
        {
            string candidate = BuildHeaderText(xmlText, attachments, primary, newInfo, sourcePath);
            long position = SignatureSize + Encoding.UTF8.GetByteCount(candidate);

            bool stable = true;
            foreach (Attachment attachment in attachments)
            {
                long newOffset = AlignUp(position, alignment);
                if (newOffset != attachment.NewOffset)
                {
                    attachment.NewOffset = newOffset;
                    stable = false;
                }
                position = newOffset + attachment.NewSize;
            }

            if (stable)
                return candidate;
        }

        throw new InvalidDataException(
            $"XISF header layout did not converge after {MaxLayoutIterations} iterations at '{sourcePath}'.");
    }

    private static long AlignUp(long position, int alignment) =>
        position % alignment == 0 ? position : position + alignment - position % alignment;

    private static string BuildHeaderText(
        string xmlText, List<Attachment> attachments, Attachment primary,
        BlockCompressionInfo newInfo, string sourcePath)
    {
        string text = xmlText;

        // 1. Swap every attachment's location value (unique per attachment — distinct offsets).
        foreach (Attachment attachment in attachments)
        {
            string newValue = string.Create(CultureInfo.InvariantCulture,
                $"attachment:{attachment.NewOffset}:{attachment.NewSize}");
            text = ReplaceUniqueAttributeValue(text, "location", attachment.OriginalLocationValue, newValue, sourcePath);
        }

        // 2. Set or remove compression/checksum inside the primary <Image> start tag, found via its
        //    (now rewritten) unique location value.
        string primaryLocation = string.Create(CultureInfo.InvariantCulture,
            $"attachment:{primary.NewOffset}:{primary.NewSize}");
        (int tagStart, int tagEnd) = FindTagSpanContaining(text, "location", primaryLocation, sourcePath);

        text = SetAttributeInTagSpan(text, ref tagStart, ref tagEnd, "compression", newInfo.ToCompressionAttribute());
        text = SetAttributeInTagSpan(text, ref tagStart, ref tagEnd, "checksum", newInfo.ToChecksumAttribute());

        return text;
    }

    /// <summary>
    /// Replaces the exactly-once occurrence of <c>name="oldValue"</c> (either quote style) with the new
    /// value. Zero or multiple occurrences mean the header text cannot be edited unambiguously — throw
    /// rather than guess.
    /// </summary>
    private static string ReplaceUniqueAttributeValue(
        string text, string name, string oldValue, string newValue, string sourcePath)
    {
        foreach (char quote in "\"'")
        {
            string token = $"{name}={quote}{oldValue}{quote}";
            int first = text.IndexOf(token, StringComparison.Ordinal);
            if (first < 0)
                continue;

            if (text.IndexOf(token, first + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException(
                    $"Cannot rewrite XISF at '{sourcePath}': attribute {token} occurs more than once in the header.");
            }

            return string.Concat(
                text.AsSpan(0, first), $"{name}={quote}{newValue}{quote}", text.AsSpan(first + token.Length));
        }

        throw new InvalidDataException(
            $"Cannot rewrite XISF at '{sourcePath}': attribute {name}=\"{oldValue}\" was not found verbatim "
            + "in the header text (entity-encoded or re-formatted attributes are not supported).");
    }

    /// <summary>
    /// Finds the start-tag span (from '&lt;' through its closing '&gt;', quote-aware) containing the
    /// given attribute value.
    /// </summary>
    private static (int TagStart, int TagEnd) FindTagSpanContaining(
        string text, string name, string value, string sourcePath)
    {
        int anchor = -1;
        foreach (char quote in "\"'")
        {
            anchor = text.IndexOf($"{name}={quote}{value}{quote}", StringComparison.Ordinal);
            if (anchor >= 0)
                break;
        }
        if (anchor < 0)
        {
            throw new InvalidDataException(
                $"Cannot rewrite XISF at '{sourcePath}': lost track of the primary image tag while editing the header.");
        }

        int tagStart = text.LastIndexOf('<', anchor);
        if (tagStart < 0)
        {
            throw new InvalidDataException(
                $"Cannot rewrite XISF at '{sourcePath}': the primary image's location attribute is not inside a tag.");
        }

        // Scan for the tag-closing '>' outside quoted attribute values.
        char inQuote = '\0';
        for (int i = tagStart + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuote != '\0')
            {
                if (c == inQuote) inQuote = '\0';
            }
            else if (c is '"' or '\'')
            {
                inQuote = c;
            }
            else if (c == '>')
            {
                return (tagStart, i);
            }
        }

        throw new InvalidDataException(
            $"Cannot rewrite XISF at '{sourcePath}': the primary image tag never closes.");
    }

    /// <summary>
    /// Sets, replaces, or removes (value null) one attribute within the given start-tag span, keeping the
    /// span bounds current for subsequent edits. Only the target attribute's text changes.
    /// </summary>
    private static string SetAttributeInTagSpan(
        string text, ref int tagStart, ref int tagEnd, string name, string? value)
    {
        string tag = text[tagStart..(tagEnd + 1)];

        int valueStart = -1, valueEnd = -1;
        char quote = '\0';
        foreach (char q in "\"'")
        {
            string prefix = $"{name}={q}";
            int at = FindAttributeStart(tag, prefix);
            if (at < 0)
                continue;
            int close = tag.IndexOf(q, at + prefix.Length);
            if (close < 0)
                continue;
            valueStart = at;
            valueEnd = close;
            quote = q;
            break;
        }

        string newTag;
        if (value is null)
        {
            if (valueStart < 0)
                return text;  // absent already
            // Remove the attribute together with the whitespace that precedes it.
            int removeStart = valueStart;
            while (removeStart > 0 && char.IsWhiteSpace(tag[removeStart - 1]))
                removeStart--;
            newTag = string.Concat(tag.AsSpan(0, removeStart), tag.AsSpan(valueEnd + 1));
        }
        else if (valueStart >= 0)
        {
            newTag = string.Concat(
                tag.AsSpan(0, valueStart), $"{name}={quote}{value}{quote}", tag.AsSpan(valueEnd + 1));
        }
        else
        {
            // Insert before the tag close ('/>' or '>'), space-separated, preserving any whitespace the
            // tag already carries before its close.
            int insertAt = tag.EndsWith("/>", StringComparison.Ordinal) ? tag.Length - 2 : tag.Length - 1;
            while (insertAt > 0 && char.IsWhiteSpace(tag[insertAt - 1]))
                insertAt--;
            newTag = string.Concat(tag.AsSpan(0, insertAt), $" {name}=\"{value}\"", tag.AsSpan(insertAt));
        }

        string result = string.Concat(text.AsSpan(0, tagStart), newTag, text.AsSpan(tagEnd + 1));
        tagEnd = tagStart + newTag.Length - 1;
        return result;
    }

    // Locates "name=<quote>" as a whole attribute name (preceded by whitespace), not a suffix of a
    // longer name (e.g. "checksum=" must not match inside an hypothetical "xchecksum=").
    private static int FindAttributeStart(string tag, string prefix)
    {
        int from = 0;
        while (true)
        {
            int at = tag.IndexOf(prefix, from, StringComparison.Ordinal);
            if (at < 0)
                return -1;
            if (at > 0 && char.IsWhiteSpace(tag[at - 1]))
                return at;
            from = at + 1;
        }
    }

    private static async Task WriteFileAsync(
        FileStream source, string tempPath, string xmlText, int xmlLength,
        List<Attachment> attachments, byte[] primaryBlock, CancellationToken ct)
    {
        await using FileStream target = new(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, useAsync: true);

        byte[] signature = new byte[SignatureSize];
        Encoding.ASCII.GetBytes("XISF0100", 0, 8, signature, 0);
        signature[8] = (byte)(xmlLength & 0xFF);
        signature[9] = (byte)((xmlLength >> 8) & 0xFF);
        signature[10] = (byte)((xmlLength >> 16) & 0xFF);
        signature[11] = (byte)((xmlLength >> 24) & 0xFF);
        await target.WriteAsync(signature, ct).ConfigureAwait(false);

        byte[] xmlBytes = Encoding.UTF8.GetBytes(xmlText);
        await target.WriteAsync(xmlBytes, ct).ConfigureAwait(false);

        long position = SignatureSize + xmlBytes.Length;
        byte[] copyBuffer = new byte[1024 * 1024];

        foreach (Attachment attachment in attachments)
        {
            // Zero-pad up to the attachment's aligned offset.
            long padding = attachment.NewOffset - position;
            if (padding < 0)
            {
                throw new InvalidOperationException(
                    $"Internal layout error: attachment offset {attachment.NewOffset} precedes write position {position}.");
            }
            while (padding > 0)
            {
                int chunk = (int)Math.Min(padding, copyBuffer.Length);
                Array.Clear(copyBuffer, 0, chunk);
                await target.WriteAsync(copyBuffer.AsMemory(0, chunk), ct).ConfigureAwait(false);
                padding -= chunk;
            }

            if (attachment.IsPrimary)
            {
                await target.WriteAsync(primaryBlock, ct).ConfigureAwait(false);
            }
            else
            {
                source.Seek(attachment.Offset, SeekOrigin.Begin);
                long remaining = attachment.Size;
                while (remaining > 0)
                {
                    int chunk = (int)Math.Min(remaining, copyBuffer.Length);
                    await source.ReadExactlyAsync(copyBuffer.AsMemory(0, chunk), ct).ConfigureAwait(false);
                    await target.WriteAsync(copyBuffer.AsMemory(0, chunk), ct).ConfigureAwait(false);
                    remaining -= chunk;
                }
            }

            position = attachment.NewOffset + attachment.NewSize;
        }
    }
}
