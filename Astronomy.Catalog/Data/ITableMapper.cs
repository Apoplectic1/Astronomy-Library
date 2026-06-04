using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Data;

/// <summary>
/// Maps a single SQLite result row to an entity of type <typeparamref name="T"/>. One concrete mapper per
/// table; the column-name-based <see cref="SqliteReaderExtensions"/> keep the mappers tolerant of column
/// reordering. Ported from the XISF File Manager pattern.
/// </summary>
/// <typeparam name="T">The entity type produced from a row.</typeparam>
public interface ITableMapper<out T>
{
    /// <summary>The source table name.</summary>
    string TableName { get; }

    /// <summary>Maps the current row of <paramref name="reader"/> to a <typeparamref name="T"/>.</summary>
    T Map(SqliteDataReader reader);
}
