namespace Astronomy.Catalog.Build;

/// <summary>
/// Summary of a catalog rebuild: how many targets came from disk vs TS and how they resolved, plus the TS
/// "problems and errors" that resolution surfaced for the user to reconcile. Counts: <see cref="BothCount"/> +
/// <see cref="PlannedOnlyCount"/> + <see cref="ActualOnlyCount"/> = total canonical targets written.
/// </summary>
public sealed record CatalogBuildReport(
    int DiskTargetCount,
    int TsTargetCount,
    int BothCount,
    int PlannedOnlyCount,
    int ActualOnlyCount,
    IReadOnlyList<NameMismatch> NameMismatches,
    IReadOnlyList<AmbiguousMatch> AmbiguousMatches,
    IReadOnlyList<DuplicateTsTarget> DuplicateTsTargets,
    IReadOnlyList<AliasTsTarget> AliasTsTargets,
    IReadOnlyList<UnanchoredTsTarget> UnanchoredTsTargets,
    IReadOnlyList<InvalidTsTarget> InvalidTsTargets,
    int MosaicsResolved = 0,
    int PanelsFolded = 0);

/// <summary>A TS target whose coordinates matched a disk target but whose name disagrees (coords win; flagged).</summary>
public sealed record NameMismatch(
    string? TsGuid, string TsName, string DiskDirectory, string? DiskObjectName, double SeparationDegrees);

/// <summary>A TS target with more than one disk target inside the match tolerance (nearest was chosen).</summary>
public sealed record AmbiguousMatch(
    string? TsGuid, string TsName, IReadOnlyList<string> CandidateDirectories, double NearestSeparationDegrees);

/// <summary>One disk target that two or more TS targets resolved onto — a duplicate in TS to clean up.
/// (When every colliding name exactly matches a disk identity facet, the fold is an <see cref="AliasTsTarget"/> instead.)</summary>
public sealed record DuplicateTsTarget(string DiskDirectory, IReadOnlyList<string> TsTargetNames);

/// <summary>
/// One disk target that two or more TS targets resolved onto where every TS name exactly equals a disk identity
/// facet (directory / catalog / common / object name) — aliases naming the same object, not a duplicate to clean
/// up. Write-back treats the members as one object and writes the disk count to every member's plans.
/// </summary>
public sealed record AliasTsTarget(string DiskDirectory, IReadOnlyList<string> TsTargetNames);

/// <summary>A TS target with no usable coordinates; it could not be anchored to disk and became planned-only.</summary>
public sealed record UnanchoredTsTarget(string? TsGuid, string TsName);

/// <summary>A TS target whose RA/Dec/epoch were out of range; the values were coerced to valid ones (row kept).</summary>
public sealed record InvalidTsTarget(string? TsGuid, string TsName, string Reason);
