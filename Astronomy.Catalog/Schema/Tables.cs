using Astronomy.Catalog.Scan;

namespace Astronomy.Catalog.Schema;

// Immutable POCOs mirroring the Catalog.db tables (see Schema/schema.sql for column docs).
// Sealed records per the Astronomy Library convention; GUID keys are stored as 16-byte big-endian BLOBs.

/// <summary>An observing profile (site/equipment scope). Normalizes TS's scattered <c>profileId</c> string.</summary>
public sealed record Profile(Guid Id, string Name, string? NinaProfileGuid, long CreatedAt);

/// <summary>An observing project grouping targets under shared scheduling constraints.</summary>
public sealed record Project(
    Guid Id,
    Guid ProfileId,
    string Name,
    string? Description,
    ProjectState State,
    ProjectPriority Priority,
    double? MinimumAltitudeDeg,
    double? MaximumAltitudeDeg,
    int? MinimumTimeMinutes,
    bool UseCustomHorizon,
    double? HorizonOffsetDeg,
    int? MeridianWindowMinutes,
    bool IsMosaic,
    bool EnableGrader,
    long CreatedAt,
    long? ActiveAt,
    long? InactiveAt,
    string? ImportedFromTsGuid);

/// <summary>
/// A canonical sky target carrying both facets of one object: disk identity (the actuals it was shot under) and
/// plan attributes (its TS project/goal). <see cref="Source"/> says which facets are present. RA is decimal hours
/// [0,24); Dec is signed decimal degrees [-90,90]. When a target is <see cref="TargetSource.Both"/>, the disk
/// (plate-solved) coordinates are canonical and <see cref="ImportedFromTsGuid"/> is retained for write-back to TS.
/// A mosaic is one parent row plus one child row per panel (<see cref="ParentTargetId"/> set); the children
/// carry the plans and inventory while the parent carries neither.
/// </summary>
public sealed record Target(
    Guid Id,
    TargetSource Source,
    Guid? ProjectId,
    string Name,
    bool Enabled,
    double? RaHours,
    double? DecDegreesSigned,
    Epoch Epoch,
    double? RotationDeg,
    double? RoiPercent,
    ProjectPriority? Priority,
    string? DirectoryName,
    string? Catalog,
    string? CommonName,
    string? ObjectName,
    long? ScannedAt,
    long CreatedAt,
    string? ImportedFromTsGuid,
    Guid? ParentTargetId = null);

/// <summary>A reusable camera/filter configuration for exposures.</summary>
public sealed record ExposureTemplate(
    Guid Id,
    Guid ProfileId,
    string Name,
    string FilterName,
    int? Gain,
    int? OffsetAdu,
    int? Binning,
    int? ReadoutMode,
    double? DefaultExposureSeconds,
    string? ImportedFromTsGuid);

/// <summary>A per-target/filter plan: the goal (<see cref="DesiredCount"/>) and progress counts.</summary>
public sealed record ExposurePlan(
    Guid Id,
    Guid TargetId,
    Guid ExposureTemplateId,
    double? ExposureSeconds,
    int DesiredCount,
    int AcquiredCount,
    int AcceptedCount,
    bool Enabled,
    string? ImportedFromTsGuid);

// ----- Inventory plane (persisted ImageLibraryScanner aggregates) -----------
// A target's identity + coordinates live on the canonical Target (source Actual/Both); only the per-filter
// actuals remain a separate table, keyed to that target.

/// <summary>Per-(target, filter, purpose, exposure) imaging totals/history (from <c>FilterAggregate</c> +
/// <c>TypicalSettings</c>). <see cref="ExposureSeconds"/> is part of the row identity: the same filter shot
/// at different sub lengths yields separate rows; consumers wanting per-filter totals sum across them.</summary>
public sealed record InventoryFilter(
    Guid TargetId,
    string FilterCode,
    FilterPurpose Purpose,
    string FilterName,
    int ExposureCount,
    double TotalIntegrationSeconds,
    long FirstImagedAt,
    long LastImagedAt,
    int TypicalGain,
    int TypicalOffset,
    double TypicalSetTempC,
    int TypicalBinningX,
    int TypicalBinningY,
    double ExposureSeconds,
    string Camera,
    bool CameraDisagrees = false);
