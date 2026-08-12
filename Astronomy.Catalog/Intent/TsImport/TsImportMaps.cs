namespace Astronomy.Catalog.Intent.TsImport;

/// <summary>
/// The explicit TS→store translation maps (rule R13: enum codes cross a system boundary through a
/// map, never a cast — the SafeEpoch silent-swap is the precedent). Every map covers the full
/// TS-defined domain; an unmapped value aborts the import via <see cref="TsImportException"/>.
/// Pinned by tests name-by-name against both endpoints' conventions.
/// </summary>
internal static class TsImportMaps
{
    /// <summary>TS <c>ProjectState</c> (Draft=0, Active=1, Inactive=2, Closed=3) → store <c>project_state</c> (same names).</summary>
    internal static readonly IReadOnlyDictionary<long, long> ProjectState = new Dictionary<long, long>
    {
        [0] = 0, // Draft
        [1] = 1, // Active
        [2] = 2, // Inactive
        [3] = 3, // Closed
    };

    /// <summary>TS <c>ProjectPriority</c> (Low=0, Normal=1, High=2) → store <c>project_priority</c> (same names).
    /// Target-side, TS's <c>Default=-1</c> is a sentinel translated to NULL (inherit project) before this map.</summary>
    internal static readonly IReadOnlyDictionary<long, long> Priority = new Dictionary<long, long>
    {
        [0] = 0, // Low
        [1] = 1, // Normal
        [2] = 2, // High
    };

    /// <summary>
    /// NINA <c>Epoch</c> (JNOW=0, B1950=1, J2000=2, J2050=3) → store <c>epoch</c> (B1950=0, JNow=1,
    /// J2000=2 — the library's canonical ordering). Only J2000 agrees by coincidence; JNow and
    /// B1950 are swapped between the conventions, and J2050 has no store value — it aborts.
    /// </summary>
    internal static readonly IReadOnlyDictionary<long, long> Epoch = new Dictionary<long, long>
    {
        [0] = 1, // NINA JNOW  -> store JNow
        [1] = 0, // NINA B1950 -> store B1950
        [2] = 2, // J2000      -> J2000
    };

    /// <summary>TS <c>TwilightLevel</c> (Nighttime=0, Astronomical=1, Nautical=2, Civil=3) → store <c>twilight_level</c> (same names).</summary>
    internal static readonly IReadOnlyDictionary<long, long> TwilightLevel = new Dictionary<long, long>
    {
        [0] = 0, // Nighttime
        [1] = 1, // Astronomical
        [2] = 2, // Nautical
        [3] = 3, // Civil
    };

    /// <summary>Maps <paramref name="sourceValue"/> through <paramref name="map"/>, aborting the import on an unmapped code.</summary>
    internal static long Map(IReadOnlyDictionary<long, long> map, long sourceValue, string table, string column, string sourceRow)
    {
        if (map.TryGetValue(sourceValue, out long mapped))
            return mapped;
        throw new TsImportException(
            $"TS import: {table}.{column} value {sourceValue} on {sourceRow} has no translation map entry; " +
            "aborting rather than casting or guessing (R13).");
    }
}
