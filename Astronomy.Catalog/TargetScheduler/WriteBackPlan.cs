using Astronomy.Catalog.Scan;

namespace Astronomy.Catalog.TargetScheduler;

// Result models for write-back planning (Phase 4). Pure data — produced by WriteBackPlanner, consumed by
// TargetSchedulerWriter (to apply) and the driving consumer (to present). Transitional: TS interop retires at
// the IS/ISP cutover, so this surface is intentionally small.

/// <summary>
/// The plan for one write-back run: the auto-resolvable counts to push, the groups that need manual
/// reconciliation, the TS target-level issues surfaced by the build, and how many disk-only targets were
/// ignored (no TS plan rows exist for them, and write-back never creates plans).
/// </summary>
public sealed record WriteBackPlan(
    IReadOnlyList<PlannedWrite> Writes,
    IReadOnlyList<ManualGroup> Manual,
    IReadOnlyList<ReconcileNote> NeedsReconciliation,
    int IgnoredMissing);

/// <summary>An auto-resolved write: set TS exposure plan <see cref="TsExposurePlanId"/>'s acquired and accepted
/// to <see cref="DiskCount"/> (and ratchet its desired up to ≥ that count, never lower) — the disk frames whose
/// whole-second exposure bucket equals <see cref="PlanSeconds"/>, the plan's effective sub length (its own value,
/// else its template default). 0 when no frames match: the plan's spec is unmet regardless of frames at other
/// durations.</summary>
public sealed record PlannedWrite(
    long TsExposurePlanId,
    Guid TargetId,
    string TargetName,
    string Filter,
    FilterPurpose Purpose,
    int PlanSeconds,
    int DiskCount);

/// <summary>Why a (target, filter, purpose, seconds) cell cannot be auto-written and needs a human.</summary>
public enum ManualReason
{
    /// <summary>One TS target has two or more plans on the same filter+purpose+seconds; disk inventory can't split them.</summary>
    MultiPlan,

    /// <summary>Two or more TS targets folded onto one disk target; their plans collide on this cell.</summary>
    DuplicateFold,

    /// <summary>The target's identity is in question (name mismatch, ambiguous coordinate match, or an
    /// ambiguously anchored mosaic panel); resolve before trusting the disk count.</summary>
    IdentityConflict,

    /// <summary>A same-seconds TS plan exists only at a different binning (the surgical <c>--target</c> path) —
    /// an equipment-identity question a human must resolve. A cell with no plan at its duration at all is an
    /// <see cref="ReconcileNote.UnplannedFramesKind"/> note instead, never manual.</summary>
    NoMatchingPlan,
}

/// <summary>A (target, filter, purpose, seconds) cell with 2+ competing TS plans — reported with everything needed to resolve it by hand.</summary>
public sealed record ManualGroup(
    Guid TargetId,
    string TargetName,
    string Filter,
    FilterPurpose Purpose,
    int Seconds,
    int DiskCount,
    ManualReason Reason,
    IReadOnlyList<ManualPlan> Plans);

/// <summary>One competing TS plan inside a <see cref="ManualGroup"/>: its TS id, its own effective whole-second
/// sub length (context plans may differ from the group's), and current catalog-side counts.</summary>
public sealed record ManualPlan(long TsExposurePlanId, int PlanSeconds, int CatalogAcquired, int CatalogAccepted, int Desired);

/// <summary>A TS target-level issue surfaced by the build (name mismatch / ambiguous / duplicate / unanchored /
/// invalid), or an informational <see cref="UnplannedFramesKind"/> entry.</summary>
public sealed record ReconcileNote(string Kind, string TargetName, string Detail)
{
    /// <summary>Kind for disk frames at an exposure duration no plan targets — informational only: write-back
    /// updates existing plan rows and never creates or deletes plans, so these are reported, not acted on.</summary>
    public const string UnplannedFramesKind = "UnplannedFrames";
}
