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

/// <summary>A sky target. RA is decimal hours [0,24); Dec is signed decimal degrees [-90,90].</summary>
public sealed record Target(
    Guid Id,
    Guid ProjectId,
    string Name,
    bool Enabled,
    double? RaHours,
    double? DecDegreesSigned,
    Epoch Epoch,
    double? RotationDeg,
    double? RoiPercent,
    ProjectPriority? Priority,
    long CreatedAt,
    string? ImportedFromTsGuid);

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

/// <summary>A scanned target's identity + coordinates (from <c>TargetReport</c>).</summary>
public sealed record InventoryTarget(
    string DirectoryName,
    string Catalog,
    string? CommonName,
    string ObjectName,
    double RaHours,
    double DecDegreesSigned,
    long ScannedAt);

/// <summary>Per-(target, filter, purpose) imaging totals/history (from <c>FilterAggregate</c> + <c>TypicalSettings</c>).</summary>
public sealed record InventoryFilter(
    string DirectoryName,
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
    double TypicalExposureSeconds,
    string Cameras);
