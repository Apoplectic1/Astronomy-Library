namespace Astronomy.Catalog.TargetScheduler;

/// <summary>The four N.I.N.A. Target Scheduler tables this editor can surface and write.</summary>
public enum TsTable
{
    /// <summary>An observing project (scheduling constraints over its targets). SQLite <c>project</c>.</summary>
    Project,

    /// <summary>A sky target under a project. SQLite <c>target</c>.</summary>
    Target,

    /// <summary>A per-(target, template) imaging goal. SQLite <c>exposureplan</c>.</summary>
    ExposurePlan,

    /// <summary>A reusable filter/camera configuration shared across plans. SQLite <c>exposuretemplate</c>.</summary>
    ExposureTemplate,
}

/// <summary>The value shape of a TS column — lets a consumer pick the right input control without hard-coding
/// per-field knowledge: <see cref="Bool"/>→checkbox, <see cref="Enum"/>→dropdown, numeric→spinner, <see cref="Text"/>→text box.</summary>
public enum TsFieldType
{
    /// <summary>0/1 integer flag.</summary>
    Bool,

    /// <summary>A whole number (integer).</summary>
    Whole,

    /// <summary>Floating-point.</summary>
    Real,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>Integer code from a named enumeration (see <see cref="TsField.EnumName"/>).</summary>
    Enum,
}

/// <summary>
/// One user-editable TS column: its <see cref="Table"/> + exact SQLite <see cref="Column"/> (which is both the
/// write whitelist and — since the column name is interpolated into the UPDATE — the SQL-injection guard), a
/// neutral <see cref="Label"/>, its value <see cref="Type"/>, whether changing it is <see cref="CadenceSafe"/>,
/// and optional enum/range/unit metadata a consumer uses to choose and bound an input control.
/// <para>
/// Consumer-neutral by design (shared-library discipline): this describes the abstract TS contract, not how any
/// one app presents it. <see cref="CadenceSafe"/> is <c>false</c> for the handful of columns whose change clears
/// the TS scheduling cadence (<c>FilterCadenceItem</c>); a consumer must warn or defer rather than do a plain
/// UPDATE for those — the plain editor write does not perform the cadence clear.
/// </para>
/// </summary>
public sealed record TsField(
    TsTable Table,
    string Column,
    string Label,
    TsFieldType Type,
    bool CadenceSafe = true,
    string? EnumName = null,
    double? Min = null,
    double? Max = null,
    string? Unit = null,
    string? Notes = null);

/// <summary>
/// The single declarative reference to the user-editable TS columns — the data dictionary that drives the editor
/// (the whitelist <em>is</em> this table), value-surfacing, and a consumer's generic edit UI. Authored from the
/// TS plugin schema rather than discovered at runtime: a db's <c>PRAGMA table_info</c> yields column names and
/// types but not <em>semantics</em> (which columns are user-editable vs. stats/keys, and which break cadence) —
/// that knowledge lives in the TS source, so it is encoded here, once. Runtime reflection is used only to
/// <em>validate</em> this reference against a given db (see <c>TargetSchedulerEditor.IsFieldAvailable</c>),
/// catching TS schema drift.
/// <para>
/// Intentionally practical, not exhaustive: the fields a user tunes on an existing plan. Deliberately omitted —
/// identity/match-bearing columns (<c>target.name</c>/<c>ra</c>/<c>dec</c>/<c>epochcode</c>, which must round-trip
/// the resolver) and stat/key columns (<c>acquired</c>/<c>accepted</c>/<c>guid</c>/FKs). Add a field by adding one
/// row here; the editor and any reference-driven UI pick it up with no further change.
/// </para>
/// </summary>
public static class TsEditableSchema
{
    /// <summary>Every editable column, grouped logically by table. Order within a table is a sensible UI order.</summary>
    public static IReadOnlyList<TsField> Fields { get; } =
    [
        // ---- project ----------------------------------------------------------------------------------------
        new(TsTable.Project, "state", "State", TsFieldType.Enum, EnumName: "ProjectState"),
        new(TsTable.Project, "priority", "Priority", TsFieldType.Enum, EnumName: "ProjectPriority"),
        new(TsTable.Project, "minimumtime", "Min time", TsFieldType.Whole, Min: 0, Max: 999, Unit: "min"),
        new(TsTable.Project, "minimumaltitude", "Min altitude", TsFieldType.Real, Min: 0, Max: 90, Unit: "°"),
        new(TsTable.Project, "maximumaltitude", "Max altitude", TsFieldType.Real, Min: 0, Max: 90, Unit: "°"),
        new(TsTable.Project, "usecustomhorizon", "Use custom horizon", TsFieldType.Bool),
        new(TsTable.Project, "horizonoffset", "Horizon offset", TsFieldType.Real, Min: 0, Max: 90, Unit: "°"),
        new(TsTable.Project, "meridianwindow", "Meridian window", TsFieldType.Whole, Min: 0, Max: 720, Unit: "min"),
        new(TsTable.Project, "ditherevery", "Dither every", TsFieldType.Whole, Min: 0, Max: 999),
        new(TsTable.Project, "enablegrader", "Enable grader", TsFieldType.Bool),
        new(TsTable.Project, "smartexposureorder", "Smart exposure order", TsFieldType.Bool,
            Notes: "Reorders the cadence on next generation but does not clear it."),
        new(TsTable.Project, "flatshandling", "Flats handling", TsFieldType.Whole,
            Notes: "TS flats-handling code (0=off, 100=target complete, 200=immediate, else a frame count)."),
        new(TsTable.Project, "filterswitchfrequency", "Filter switch frequency", TsFieldType.Whole,
            CadenceSafe: false, Min: 0, Max: 999,
            Notes: "Cadence-breaking: TS clears FilterCadenceItem when this changes."),

        // ---- target -----------------------------------------------------------------------------------------
        new(TsTable.Target, "active", "Enabled", TsFieldType.Bool),
        new(TsTable.Target, "priority", "Priority", TsFieldType.Enum, EnumName: "TargetPriority"),
        new(TsTable.Target, "rotation", "Rotation", TsFieldType.Real, Min: 0, Max: 360, Unit: "°"),
        new(TsTable.Target, "roi", "ROI", TsFieldType.Real, Min: 0, Max: 100, Unit: "%"),

        // ---- exposureplan -----------------------------------------------------------------------------------
        new(TsTable.ExposurePlan, "desired", "Desired", TsFieldType.Whole, Min: 0, Max: 99999),
        new(TsTable.ExposurePlan, "exposure", "Exposure", TsFieldType.Real, Min: 0, Unit: "s"),
        new(TsTable.ExposurePlan, "enabled", "Enabled", TsFieldType.Bool,
            CadenceSafe: false,
            Notes: "Cadence-breaking: TS ToggleExposurePlan clears FilterCadenceItem (vs. target.active, which is safe)."),

        // ---- exposuretemplate -------------------------------------------------------------------------------
        new(TsTable.ExposureTemplate, "name", "Template name", TsFieldType.Text),
        new(TsTable.ExposureTemplate, "filtername", "Filter", TsFieldType.Text),
        new(TsTable.ExposureTemplate, "gain", "Gain", TsFieldType.Whole, Notes: "-1 = camera default."),
        new(TsTable.ExposureTemplate, "offset", "Offset", TsFieldType.Whole, Notes: "-1 = camera default."),
        new(TsTable.ExposureTemplate, "bin", "Binning", TsFieldType.Whole, Min: 1, Max: 4),
        new(TsTable.ExposureTemplate, "readoutmode", "Readout mode", TsFieldType.Whole, Notes: "-1 = camera default."),
        new(TsTable.ExposureTemplate, "defaultexposure", "Default exposure", TsFieldType.Real, Min: 0, Unit: "s"),
    ];

    private static readonly Dictionary<TsTable, IReadOnlyList<TsField>> ByTable =
        Fields.GroupBy(f => f.Table).ToDictionary(g => g.Key, g => (IReadOnlyList<TsField>)[.. g]);

    private static readonly Dictionary<TsTable, Dictionary<string, TsField>> ByColumn =
        Fields.GroupBy(f => f.Table).ToDictionary(
            g => g.Key,
            g => g.ToDictionary(f => f.Column, StringComparer.OrdinalIgnoreCase));

    /// <summary>The editable fields of one table, in UI order (empty if none).</summary>
    public static IReadOnlyList<TsField> For(TsTable table) =>
        ByTable.TryGetValue(table, out IReadOnlyList<TsField>? fields) ? fields : [];

    /// <summary>The field for <paramref name="table"/>.<paramref name="column"/> (case-insensitive), or <c>null</c>
    /// if that column is not an editable field — the lookup the editor uses as its write whitelist.</summary>
    public static TsField? Find(TsTable table, string column) =>
        ByColumn.TryGetValue(table, out Dictionary<string, TsField>? cols)
        && cols.TryGetValue(column, out TsField? field) ? field : null;

    /// <summary>True when <paramref name="column"/> is editable but changing it clears the TS scheduling cadence
    /// (so a plain UPDATE is insufficient — the consumer must warn or defer). False for unknown or cadence-safe columns.</summary>
    public static bool IsCadenceBreaking(TsTable table, string column) =>
        Find(table, column) is { CadenceSafe: false };

    /// <summary>The exact SQLite table name for <paramref name="table"/>.</summary>
    public static string TableName(TsTable table) => table switch
    {
        TsTable.Project => "project",
        TsTable.Target => "target",
        TsTable.ExposurePlan => "exposureplan",
        TsTable.ExposureTemplate => "exposuretemplate",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };
}
