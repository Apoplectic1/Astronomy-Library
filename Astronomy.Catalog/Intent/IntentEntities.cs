namespace Astronomy.Catalog.Intent;

// Schema-mirror parameter records for IntentWriter — one property per writable column of the
// intent-plane tables, names 1:1 with the migration DDL (the documented schema is the doc; CS1591
// is suppressed project-wide for DB-mirror POCOs). Conventions, per the intent-store spec:
//   - `required` marks columns the DDL demands with no default; properties with initializers
//     mirror the DDL default (restating authored schema truth, not inventing a value).
//   - Nullable properties write verbatim — NULL means unset, never coalesced (R3).
//   - Timestamps are raw UNIX seconds UTC (R12); the caller owns the creation instant, and
//     upserts never rewrite CreatedAt on update.

/// <summary>A <c>project</c> row for <see cref="IntentWriter.UpsertProject"/> (full-value; see file header).</summary>
public sealed record ProjectIntent
{
    public required Guid Id { get; init; }
    public required Guid ProfileId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required int StateId { get; init; }
    public required int PriorityId { get; init; }
    public long? MinimumTimeMinutes { get; init; }
    public double? MinimumAltitudeDeg { get; init; }
    public double? MaximumAltitudeDeg { get; init; }
    public bool UseCustomHorizon { get; init; }
    public double HorizonOffsetDeg { get; init; }
    public long? MeridianWindowMinutes { get; init; }
    public long? FilterSwitchFrequency { get; init; }
    public long? DitherEvery { get; init; }
    public bool SmartExposureOrder { get; init; }
    public bool IsMosaic { get; init; }
    public required long CreatedAt { get; init; }
    public long? ActiveAt { get; init; }
    public long? InactiveAt { get; init; }
    public string? ImportedFromTsGuid { get; init; }
}

/// <summary>A <c>target</c> row for <see cref="IntentWriter.UpsertTarget"/> (full-value; see file header).</summary>
public sealed record TargetIntent
{
    public required Guid Id { get; init; }
    public required Guid ProjectId { get; init; }
    public Guid? ParentTargetId { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public double? RaHours { get; init; }
    public double? DecDegreesSigned { get; init; }
    public int EpochId { get; init; } = 2;          // DDL default: J2000
    public double? RotationDeg { get; init; }
    public int? PriorityId { get; init; }           // NULL = inherit project
    public required long CreatedAt { get; init; }
    public string? ImportedFromTsGuid { get; init; }
}

/// <summary>An <c>exposure_template</c> row for <see cref="IntentWriter.UpsertExposureTemplate"/> (full-value; see file header).</summary>
public sealed record ExposureTemplateIntent
{
    public required Guid Id { get; init; }
    public required Guid ProfileId { get; init; }
    public required string Name { get; init; }
    public required string FilterName { get; init; }
    public long? Gain { get; init; }
    public long? OffsetAdu { get; init; }
    public required long Binning { get; init; }
    public long? ReadoutMode { get; init; }         // NULL = camera default
    public required double DefaultExposureSeconds { get; init; }
    public required int TwilightLevelId { get; init; }
    public bool MoonAvoidanceEnabled { get; init; }
    public double? MoonAvoidanceSeparationDeg { get; init; }
    public long? MoonAvoidanceWidthDays { get; init; }
    public double? MoonRelaxScale { get; init; }
    public double? MoonRelaxMaxAltitudeDeg { get; init; }
    public double? MoonRelaxMinAltitudeDeg { get; init; }
    public string? ImportedFromTsGuid { get; init; }
}

/// <summary>An <c>exposure_plan</c> row for <see cref="IntentWriter.UpsertExposurePlan"/> (full-value; see file header).</summary>
public sealed record ExposurePlanIntent
{
    public required Guid Id { get; init; }
    public required Guid TargetId { get; init; }
    public required Guid ExposureTemplateId { get; init; }
    public double? ExposureSeconds { get; init; }   // NULL = inherit the template default
    public required long DesiredCount { get; init; }
    public bool Enabled { get; init; } = true;
    public string? ImportedFromTsGuid { get; init; }
}
