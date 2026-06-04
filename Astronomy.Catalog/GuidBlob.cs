namespace Astronomy.Catalog;

/// <summary>
/// Converts between <see cref="Guid"/> and the 16-byte BLOB form used for primary keys in the catalog
/// database. Big-endian (RFC 4122) byte order is used deliberately and consistently across every consumer,
/// so the stored bytes do not depend on .NET's mixed-endian <see cref="Guid.ToByteArray()"/> layout
/// (the cross-consumer footgun called out in the IS schema brief, decision 2).
/// </summary>
public static class GuidBlob
{
    /// <summary>Number of bytes in a GUID BLOB.</summary>
    public const int Size = 16;

    /// <summary>Returns the 16-byte big-endian representation of <paramref name="value"/>.</summary>
    public static byte[] ToBlob(Guid value)
    {
        byte[] bytes = new byte[Size];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    /// <summary>Reconstructs a <see cref="Guid"/> from its 16-byte big-endian representation.</summary>
    /// <exception cref="ArgumentException"><paramref name="blob"/> is not exactly 16 bytes.</exception>
    public static Guid FromBlob(ReadOnlySpan<byte> blob)
    {
        if (blob.Length != Size)
            throw new ArgumentException($"GUID blob must be {Size} bytes, got {blob.Length}.", nameof(blob));

        return new Guid(blob, bigEndian: true);
    }
}
