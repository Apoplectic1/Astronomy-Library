using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Data;

/// <summary>
/// Null-safe, column-name-based readers over <see cref="SqliteDataReader"/>. Column-name lookups (rather than
/// ordinals) keep mappers robust against additive schema changes and column reordering.
/// </summary>
public static class SqliteReaderExtensions
{
    /// <summary>Reads a non-null 32-bit integer by column name.</summary>
    public static int GetInt32(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column));

    /// <summary>Reads a 32-bit integer by column name, or null when the column is NULL.</summary>
    public static int? GetInt32OrNull(this SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    /// <summary>Reads a non-null 64-bit integer by column name.</summary>
    public static long GetInt64(this SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column));

    /// <summary>Reads a 64-bit integer by column name, or null when the column is NULL.</summary>
    public static long? GetInt64OrNull(this SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    /// <summary>Reads a non-null double by column name.</summary>
    public static double GetDouble(this SqliteDataReader reader, string column) =>
        reader.GetDouble(reader.GetOrdinal(column));

    /// <summary>Reads a double by column name, or null when the column is NULL.</summary>
    public static double? GetDoubleOrNull(this SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    /// <summary>Reads a non-null string by column name.</summary>
    public static string GetString(this SqliteDataReader reader, string column) =>
        reader.GetString(reader.GetOrdinal(column));

    /// <summary>Reads a string by column name, or null when the column is NULL.</summary>
    public static string? GetStringOrNull(this SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>Reads a boolean stored as INTEGER 0/1 by column name.</summary>
    public static bool GetBoolean(this SqliteDataReader reader, string column) =>
        reader.GetInt64(reader.GetOrdinal(column)) != 0;

    /// <summary>Reads a <see cref="Guid"/> from a 16-byte BLOB column (big-endian, see <see cref="GuidBlob"/>).</summary>
    public static Guid GetGuid(this SqliteDataReader reader, string column) =>
        GuidBlob.FromBlob((byte[])reader.GetValue(reader.GetOrdinal(column)));

    /// <summary>Reads a <see cref="Guid"/> from a 16-byte BLOB column, or null when the column is NULL.</summary>
    public static Guid? GetGuidOrNull(this SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : GuidBlob.FromBlob((byte[])reader.GetValue(ordinal));
    }
}
