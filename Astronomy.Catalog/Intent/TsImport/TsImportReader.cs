using Astronomy.Catalog.Data;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Intent.TsImport;

// Source rows for the one-time lift, mirroring TS's schedulerdb.sqlite columns (casing drift and
// all). Deliberately separate from the shipped TargetSchedulerReader records: the lift needs the
// FULL in-scope field set (lifecycle dates, meridian window, twilight level, the moon-avoidance
// block, ...) and widening those shipped public records would ripple to their consumers. Values
// are read as-nullable; the importer enforces requiredness with rule-#16 diagnostics.

internal sealed record TsSourceProject(
    long Id, string? ProfileId, string? Name, string? Description, long? State, long? Priority,
    long? CreateDate, long? ActiveDate, long? InactiveDate, long? MinimumTime,
    double? MinimumAltitude, double? MaximumAltitude, long? UseCustomHorizon, double? HorizonOffset,
    long? MeridianWindow, long? FilterSwitchFrequency, long? DitherEvery, long? SmartExposureOrder,
    long? IsMosaic, string? Guid);

internal sealed record TsSourceTarget(
    long Id, string? Name, long? Active, double? Ra, double? Dec, long? EpochCode, double? Rotation,
    long? ProjectId, long? Priority, string? Guid);

internal sealed record TsSourceTemplate(
    long Id, string? ProfileId, string? Name, string? FilterName, long? Gain, long? Offset,
    long? Bin, long? ReadoutMode, double? DefaultExposure, long? TwilightLevel,
    long? MoonAvoidanceEnabled, double? MoonAvoidanceSeparation, long? MoonAvoidanceWidth,
    double? MoonRelaxScale, double? MoonRelaxMaxAltitude, double? MoonRelaxMinAltitude, string? Guid);

internal sealed record TsSourceExposurePlan(
    long Id, double? Exposure, long? Desired, long? Enabled, long? TargetId,
    long? ExposureTemplateId, string? Guid);

/// <summary>
/// Read-only access to the TS database for the lift: <c>Mode=ReadOnly</c> (the source is never
/// modified, even on failure), explicit column lists (never <c>SELECT *</c>), busy timeout for
/// polite coexistence with a TS writer. Dispose to close.
/// </summary>
internal sealed class TsImportReader : IDisposable
{
    private readonly SqliteConnection _connection;

    internal TsImportReader(string schedulerDbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerDbPath);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = schedulerDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,   // release the source file handle on Dispose (the source is never held open)
        }.ToString());
        _connection.Open();

        using SqliteCommand pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 2000;";
        pragma.ExecuteNonQuery();
    }

    internal IReadOnlyList<TsSourceProject> ReadProjects() => Query(
        "SELECT Id, profileId, name, description, state, priority, createdate, activedate, inactivedate, " +
        "minimumtime, minimumaltitude, maximumAltitude, usecustomhorizon, horizonoffset, meridianwindow, " +
        "filterswitchfrequency, ditherevery, smartexposureorder, isMosaic, guid FROM project;",
        r => new TsSourceProject(
            r.GetInt64("Id"), r.GetStringOrNull("profileId"), r.GetStringOrNull("name"),
            r.GetStringOrNull("description"), r.GetInt64OrNull("state"), r.GetInt64OrNull("priority"),
            r.GetInt64OrNull("createdate"), r.GetInt64OrNull("activedate"), r.GetInt64OrNull("inactivedate"),
            r.GetInt64OrNull("minimumtime"), r.GetDoubleOrNull("minimumaltitude"), r.GetDoubleOrNull("maximumAltitude"),
            r.GetInt64OrNull("usecustomhorizon"), r.GetDoubleOrNull("horizonoffset"), r.GetInt64OrNull("meridianwindow"),
            r.GetInt64OrNull("filterswitchfrequency"), r.GetInt64OrNull("ditherevery"), r.GetInt64OrNull("smartexposureorder"),
            r.GetInt64OrNull("isMosaic"), r.GetStringOrNull("guid")));

    internal IReadOnlyList<TsSourceTarget> ReadTargets() => Query(
        "SELECT Id, name, active, ra, dec, epochcode, rotation, projectid, priority, guid FROM target;",
        r => new TsSourceTarget(
            r.GetInt64("Id"), r.GetStringOrNull("name"), r.GetInt64OrNull("active"),
            r.GetDoubleOrNull("ra"), r.GetDoubleOrNull("dec"), r.GetInt64OrNull("epochcode"),
            r.GetDoubleOrNull("rotation"), r.GetInt64OrNull("projectid"), r.GetInt64OrNull("priority"),
            r.GetStringOrNull("guid")));

    internal IReadOnlyList<TsSourceTemplate> ReadExposureTemplates() => Query(
        "SELECT Id, profileId, name, filtername, gain, offset, bin, readoutmode, defaultexposure, twilightlevel, " +
        "moonavoidanceenabled, moonavoidanceseparation, moonavoidancewidth, moonrelaxscale, moonrelaxmaxaltitude, " +
        "moonrelaxminaltitude, guid FROM exposuretemplate;",
        r => new TsSourceTemplate(
            r.GetInt64("Id"), r.GetStringOrNull("profileId"), r.GetStringOrNull("name"),
            r.GetStringOrNull("filtername"), r.GetInt64OrNull("gain"), r.GetInt64OrNull("offset"),
            r.GetInt64OrNull("bin"), r.GetInt64OrNull("readoutmode"), r.GetDoubleOrNull("defaultexposure"),
            r.GetInt64OrNull("twilightlevel"), r.GetInt64OrNull("moonavoidanceenabled"),
            r.GetDoubleOrNull("moonavoidanceseparation"), r.GetInt64OrNull("moonavoidancewidth"),
            r.GetDoubleOrNull("moonrelaxscale"), r.GetDoubleOrNull("moonrelaxmaxaltitude"),
            r.GetDoubleOrNull("moonrelaxminaltitude"), r.GetStringOrNull("guid")));

    internal IReadOnlyList<TsSourceExposurePlan> ReadExposurePlans() => Query(
        "SELECT Id, exposure, desired, enabled, targetid, exposureTemplateId, guid FROM exposureplan;",
        r => new TsSourceExposurePlan(
            r.GetInt64("Id"), r.GetDoubleOrNull("exposure"), r.GetInt64OrNull("desired"),
            r.GetInt64OrNull("enabled"), r.GetInt64OrNull("targetid"), r.GetInt64OrNull("exposureTemplateId"),
            r.GetStringOrNull("guid")));

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<T> rows = [];
        while (reader.Read())
            rows.Add(map(reader));
        return rows;
    }

    public void Dispose() => _connection.Dispose();
}
