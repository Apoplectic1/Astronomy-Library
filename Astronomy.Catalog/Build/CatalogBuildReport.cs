namespace Astronomy.Catalog.Build;

/// <summary>
/// Summary of a catalog rebuild: how many targets came from disk vs TS and how they resolved, plus the TS
/// "problems and errors" that resolution surfaced for the user to reconcile. Counts: <see cref="BothCount"/> +
/// <see cref="PlannedOnlyCount"/> + <see cref="ActualOnlyCount"/> = total TOP-LEVEL canonical targets written
/// (mosaic panel children are counted separately via the <c>Panels*</c> counters).
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
    int PanelsMatched = 0,
    int PanelsPlannedOnly = 0,
    int PanelsActualOnly = 0)
{
    // Lazy lookup indexes over the issue lists. NOT thread-safe (plain ??=) — the report is built and
    // consumed on one thread; a consumer sharing one instance across threads must index it first. They
    // also sit outside record equality (value semantics cover the positional lists only).
    private Dictionary<string, TargetMatchIssues>? _issuesByDirectory;
    private Dictionary<string, int>? _aliasMembersByDirectory;
    private HashSet<string>? _unanchoredNames;

    /// <summary>
    /// Match-state issues for one disk directory (composite panel names included) — THE definition every
    /// consumer derives from, whether routing write-back's manual bucket or badging a display row.
    /// </summary>
    public TargetMatchIssues IssuesFor(string? directoryName)
    {
        if (directoryName is null) return TargetMatchIssues.None;
        _issuesByDirectory ??= BuildIssueIndex();
        return _issuesByDirectory.GetValueOrDefault(directoryName, TargetMatchIssues.None);
    }

    /// <summary>True when the directory's target identity is in question (name mismatch or ambiguous
    /// coordinate match) — write-back holds these for manual resolution, never auto-writes.</summary>
    public bool IsIdentityFlagged(string? directoryName) =>
        (IssuesFor(directoryName) & (TargetMatchIssues.NameMismatch | TargetMatchIssues.AmbiguousMatch)) != 0;

    /// <summary>Number of TS names folded onto this directory as aliases (0 when not an alias fold).</summary>
    public int AliasMemberCount(string? directoryName)
    {
        if (directoryName is null) return 0;
        _aliasMembersByDirectory ??= AliasTsTargets.ToDictionary(
            a => a.DiskDirectory, a => a.TsTargetNames.Count, StringComparer.OrdinalIgnoreCase);
        return _aliasMembersByDirectory.GetValueOrDefault(directoryName);
    }

    /// <summary>True when the named TS target could not be anchored (no usable coordinates).</summary>
    public bool IsUnanchoredName(string tsName)
    {
        _unanchoredNames ??= new HashSet<string>(
            UnanchoredTsTargets.Select(u => u.TsName), StringComparer.OrdinalIgnoreCase);
        return _unanchoredNames.Contains(tsName);
    }

    private Dictionary<string, TargetMatchIssues> BuildIssueIndex()
    {
        Dictionary<string, TargetMatchIssues> flags = new(StringComparer.OrdinalIgnoreCase);
        void Add(string dir, TargetMatchIssues f) => flags[dir] = flags.GetValueOrDefault(dir) | f;

        foreach (AliasTsTarget a in AliasTsTargets) Add(a.DiskDirectory, TargetMatchIssues.Alias);
        foreach (DuplicateTsTarget d in DuplicateTsTargets) Add(d.DiskDirectory, TargetMatchIssues.Duplicate);
        foreach (NameMismatch m in NameMismatches) Add(m.DiskDirectory, TargetMatchIssues.NameMismatch);
        foreach (AmbiguousMatch a in AmbiguousMatches)
            foreach (string d in a.CandidateDirectories)
                Add(d, TargetMatchIssues.AmbiguousMatch);
        return flags;
    }
}

/// <summary>Match-state classification of one disk directory, derived from the build report's issue lists.</summary>
[Flags]
public enum TargetMatchIssues
{
    /// <summary>Clean match (or unknown directory).</summary>
    None = 0,

    /// <summary>Member of an alias fold — multiple TS names for the same object; counts write to all.</summary>
    Alias = 1,

    /// <summary>Two or more TS targets resolved here — a duplicate to clean up in TS.</summary>
    Duplicate = 2,

    /// <summary>Anchored by coordinates but the names disagree.</summary>
    NameMismatch = 4,

    /// <summary>A candidate in an ambiguous coordinate match (nearest was chosen).</summary>
    AmbiguousMatch = 8,
}

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
