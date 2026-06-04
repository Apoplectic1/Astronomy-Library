namespace Astronomy.Catalog.Schema;

// Immutable POCOs mirroring the Catalog.db tables (see Schema/Migrations/0001_init.sql for column docs).
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

/// <summary>One scanned image file on disk (the disk-derived inventory; populated by the Phase 2 scanner).</summary>
public sealed record ImageFile(
    Guid Id,
    string Path,
    Guid? TargetId,
    string? TargetName,
    string? FilterName,
    FrameType? FrameType,
    ProcessingStage? ProcessingStage,
    double? ExposureSeconds,
    long? CapturedAt,
    string? Camera,
    int? Gain,
    int? OffsetAdu,
    double? RaHours,
    double? DecDegreesSigned,
    long FileMtime,
    long FileSize,
    long ScannedAt);

/// <summary>Incremental-scan watermark for a target folder (or the scan root).</summary>
public sealed record ScanState(string Folder, long LastScannedAt, long MaxMtimeSeen, int FileCount);

/// <summary>A row of the <c>inventory_rollup</c> view: integration per target/filter/stage (lights only).</summary>
public sealed record InventoryRollupRow(
    Guid? TargetId,
    string? TargetName,
    string? FilterName,
    ProcessingStage? ProcessingStage,
    int FrameCount,
    double TotalIntegrationSeconds,
    long? FirstCapturedAt,
    long? LastCapturedAt);
