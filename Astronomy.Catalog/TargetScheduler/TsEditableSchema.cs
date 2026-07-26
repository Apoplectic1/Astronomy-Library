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

/// <summary>
/// Which derived <c>filtercadenceitem</c> rows an edit of a column invalidates. TS persists per-target filter
/// rotation in that table and restores it <b>verbatim</b> on every planning pass (regenerating only when a
/// target has none — structural in its <c>FilterCadenceFactory.Generate</c>); every TS code path that changes
/// a plan set clears the affected rows in the same breath (<c>SchedulerDatabaseContext.ToggleExposurePlan</c>;
/// <c>SaveProject</c> on a filter-switch-frequency change). An editor honoring this scope keeps the db in a
/// state TS itself could have produced: the column write and the scoped delete are one transaction, and an
/// empty cadence is always safe.
/// </summary>
public enum TsCadenceClear
{
    /// <summary>The column does not invalidate cadence rows (a plain UPDATE suffices).</summary>
    None,

    /// <summary>Clears the edited row's target's cadence rows (the row's table carries <c>targetid</c>).</summary>
    Target,

    /// <summary>Clears the cadence rows of every target belonging to the edited project row.</summary>
    Project,
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
/// neutral <see cref="Label"/>, its value <see cref="Type"/>, its cadence clear scope <see cref="Clears"/>,
/// and optional enum/range/unit metadata a consumer uses to choose and bound an input control.
/// <para>
/// <see cref="Sentinel"/>/<see cref="SentinelLabel"/> describe TS's defer-to-default convention on some numeric
/// columns: one reserved impossible value (always −1 in TS) stored in the column means "no explicit value —
/// resolve it elsewhere" (<see cref="SentinelLabel"/> names where: the exposure template, the camera). A consumer
/// should render the sentinel as that meaning (e.g. a "use default" checkbox), never as the raw number, and must
/// exempt it from <see cref="Min"/>/<see cref="Max"/> bounds — the sentinel is legal to write back.
/// </para>
/// <para>
/// <see cref="Guarded"/> marks a field whose accidental change breaks acquisition against existing data (e.g.
/// <c>target.rotation</c> — a changed angle misaligns every future frame with the stack). A consumer's edit UI
/// should require an explicit arm gesture (e.g. an enable checkbox) before the field's input accepts changes.
/// </para>
/// <para>
/// Consumer-neutral by design (shared-library discipline): this describes the abstract TS contract, not how any
/// one app presents it. <see cref="Clears"/> names the scope of derived <c>filtercadenceitem</c>
/// rows an edit invalidates (see <see cref="TsCadenceClear"/>); the editor deletes that scope in the same
/// transaction as the column write, so a consumer's only duty is to warn/confirm before committing.
/// </para>
/// </summary>
public sealed record TsField(
    TsTable Table,
    string Column,
    string Label,
    TsFieldType Type,
    TsCadenceClear Clears = TsCadenceClear.None,
    string? EnumName = null,
    double? Min = null,
    double? Max = null,
    string? Unit = null,
    string? Notes = null,
    double? Sentinel = null,
    string? SentinelLabel = null,
    bool Guarded = false);

/// <summary>One selectable value of a named TS enumeration: the integer <see cref="Code"/> stored in the
/// column and a display <see cref="Label"/> (the TS source enum member name).</summary>
public sealed record TsEnumValue(int Code, string Label);

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
            Clears: TsCadenceClear.Project, Min: 0, Max: 999,
            Notes: "Resets the filter rotation of every target in the project (cleared atomically with the write; TS regenerates)."),

        // ---- target -----------------------------------------------------------------------------------------
        new(TsTable.Target, "active", "Enabled", TsFieldType.Bool),
        new(TsTable.Target, "priority", "Priority", TsFieldType.Enum, EnumName: "TargetPriority"),
        new(TsTable.Target, "rotation", "Rotation", TsFieldType.Real, Min: 0, Max: 360, Unit: "°",
            Guarded: true, Notes: "Guarded: an accidental rotation change misaligns future frames with the existing stack."),
        // target.roi deliberately omitted (2026-07-06): the user never adjusts ROI — not part of the editable surface.

        // ---- exposureplan -----------------------------------------------------------------------------------
        new(TsTable.ExposurePlan, "desired", "Desired", TsFieldType.Whole, Min: 0, Max: 99999),
        new(TsTable.ExposurePlan, "exposure", "Exposure", TsFieldType.Real, Min: 0, Unit: "s",
            Sentinel: -1, SentinelLabel: "template default",
            Notes: "-1 = use the exposure template's default exposure."),
        new(TsTable.ExposurePlan, "enabled", "Enabled", TsFieldType.Bool,
            Clears: TsCadenceClear.Target,
            Notes: "Resets the target's filter rotation (cleared atomically with the write; TS regenerates). target.active stays scope-free."),

        // ---- exposuretemplate -------------------------------------------------------------------------------
        new(TsTable.ExposureTemplate, "name", "Template name", TsFieldType.Text),
        new(TsTable.ExposureTemplate, "filtername", "Filter", TsFieldType.Text),
        new(TsTable.ExposureTemplate, "gain", "Gain", TsFieldType.Whole, Min: 0, Notes: "-1 = camera default.",
            Sentinel: -1, SentinelLabel: "camera default"),
        new(TsTable.ExposureTemplate, "offset", "Offset", TsFieldType.Whole, Min: 0, Notes: "-1 = camera default.",
            Sentinel: -1, SentinelLabel: "camera default"),
        new(TsTable.ExposureTemplate, "bin", "Binning", TsFieldType.Whole, Min: 1, Max: 4),
        new(TsTable.ExposureTemplate, "readoutmode", "Readout mode", TsFieldType.Whole, Min: 0, Notes: "-1 = camera default.",
            Sentinel: -1, SentinelLabel: "camera default"),
        new(TsTable.ExposureTemplate, "defaultexposure", "Default exposure", TsFieldType.Real, Min: 0, Unit: "s"),
        // Scheduling-condition columns (all scoring/filter inputs — none clear the TS cadence). The column
        // spelling "twilightlevel" is TS's own EF rename (property twilightlevel_col); the rest use the
        // property names, matched case-insensitively like every other row.
        new(TsTable.ExposureTemplate, "twilightlevel", "Twilight level", TsFieldType.Enum, EnumName: "TwilightLevel"),
        new(TsTable.ExposureTemplate, "minutesoffset", "Minutes offset", TsFieldType.Whole, Min: -720, Max: 720, Unit: "min",
            Notes: "Shifts the twilight window; negative is legal. ±720 is a sanity clamp, not a TS bound."),
        new(TsTable.ExposureTemplate, "moonavoidanceenabled", "Moon avoidance", TsFieldType.Bool),
        new(TsTable.ExposureTemplate, "moonavoidanceseparation", "Moon separation", TsFieldType.Real, Min: 0, Max: 180, Unit: "°"),
        new(TsTable.ExposureTemplate, "moonavoidancewidth", "Moon width", TsFieldType.Whole, Min: 0, Max: 30, Unit: "days"),
        new(TsTable.ExposureTemplate, "moonrelaxscale", "Moon relax scale", TsFieldType.Real, Min: 0, Max: 10,
            Notes: "0 disables relax."),
        new(TsTable.ExposureTemplate, "moonrelaxmaxaltitude", "Moon relax max alt", TsFieldType.Real, Min: -90, Max: 90, Unit: "°"),
        new(TsTable.ExposureTemplate, "moonrelaxminaltitude", "Moon relax min alt", TsFieldType.Real, Min: -90, Max: 90, Unit: "°",
            Notes: "Negative is normal (TS ships -15)."),
        new(TsTable.ExposureTemplate, "moondownenabled", "Moon down only", TsFieldType.Bool),
        new(TsTable.ExposureTemplate, "ditherevery", "Dither every", TsFieldType.Whole, Min: 0, Max: 999,
            Sentinel: -1, SentinelLabel: "project default",
            Notes: "-1 = use the project's dither setting (TS's planner tests >= 0)."),
        new(TsTable.ExposureTemplate, "maximumhumidity", "Max humidity", TsFieldType.Real, Min: 0, Max: 100, Unit: "%",
            Notes: "0 = disabled."),
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

    // The code/label sets behind every TsField.EnumName — authored from the TS source enums (ProjectState /
    // ProjectPriority / TargetPriority in the TS plugin's Database/Schema/Project.cs; TwilightLevel in its
    // Astrometry/TwilightCircumstances.cs), like the field rows above.
    private static readonly Dictionary<string, IReadOnlyList<TsEnumValue>> EnumMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProjectState"] = [new(0, "Draft"), new(1, "Active"), new(2, "Inactive"), new(3, "Closed")],
        ["ProjectPriority"] = [new(0, "Low"), new(1, "Normal"), new(2, "High")],
        ["TargetPriority"] = [new(-1, "Default"), new(0, "Low"), new(1, "Normal"), new(2, "High")],
        ["TwilightLevel"] = [new(0, "Nighttime"), new(1, "Astronomical"), new(2, "Nautical"), new(3, "Civil")],
    };

    /// <summary>The ordered code/label values of the named enumeration (a <see cref="TsField.EnumName"/>,
    /// case-insensitive) — what a consumer binds to a selection control instead of hard-coding TS codes.
    /// Empty for an unknown or null name, never a throw.</summary>
    public static IReadOnlyList<TsEnumValue> EnumValues(string? enumName) =>
        enumName is not null && EnumMaps.TryGetValue(enumName, out IReadOnlyList<TsEnumValue>? values) ? values : [];

    /// <summary>The field for <paramref name="table"/>.<paramref name="column"/> (case-insensitive), or <c>null</c>
    /// if that column is not an editable field — the lookup the editor uses as its write whitelist.</summary>
    public static TsField? Find(TsTable table, string column) =>
        ByColumn.TryGetValue(table, out Dictionary<string, TsField>? cols)
        && cols.TryGetValue(column, out TsField? field) ? field : null;

    /// <summary>True when <paramref name="column"/> is editable and its edit clears TS cadence rows
    /// (<see cref="TsField.Clears"/> is not <see cref="TsCadenceClear.None"/> — the editor performs the scoped
    /// clear atomically; a consumer should confirm before committing). False for unknown or scope-free columns.</summary>
    public static bool IsCadenceBreaking(TsTable table, string column) =>
        Find(table, column) is { Clears: not TsCadenceClear.None };

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
