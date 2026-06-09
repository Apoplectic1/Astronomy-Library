using Astronomy.Catalog.Scan;

namespace Astronomy.Catalog.TargetScheduler;

// Result models for write-back planning (Phase 4). Pure data — produced by WriteBackPlanner, consumed by
// TargetSchedulerWriter (to apply) and the TCM host (to print). Transitional: TS interop retires at the IS/ISP
// cutover, so this surface is intentionally small.

/// <summary>
/// The plan for one write-back run: the auto-resolvable counts to push, the groups that need manual
/// reconciliation, the TS target-level issues surfaced by the build, and how many targets were ignored because
/// they exist on only one side (disk xor TS).
/// </summary>
public sealed record WriteBackPlan(
    IReadOnlyList<PlannedWrite> Writes,
    IReadOnlyList<ManualGroup> Manual,
    IReadOnlyList<ReconcileNote> NeedsReconciliation,
    int IgnoredMissing);

/// <summary>An auto-resolved write: set TS exposure plan <see cref="TsExposurePlanId"/>'s acquired and accepted to <see cref="DiskCount"/>.</summary>
public sealed record PlannedWrite(
    long TsExposurePlanId,
    Guid TargetId,
    string TargetName,
    string Filter,
    FilterPurpose Purpose,
    int DiskCount);

/// <summary>Why a (target, filter, purpose) cell cannot be auto-written and needs a human.</summary>
public enum ManualReason
{
    /// <summary>One TS target has two or more plans on the same filter+purpose; disk inventory can't split them.</summary>
    MultiPlan,

    /// <summary>Two or more TS targets folded onto one disk target; their plans collide on this filter+purpose.</summary>
    DuplicateFold,

    /// <summary>The target's identity is in question (name mismatch or ambiguous coordinate match); resolve before trusting the disk count.</summary>
    IdentityConflict,

    /// <summary>A mosaic target (panels folded from a TS isMosaic project); panel-level counts are resolved in TS, not auto-written.</summary>
    Mosaic,
}

/// <summary>A (target, filter, purpose) cell with 2+ competing TS plans — reported with everything needed to resolve it by hand.</summary>
public sealed record ManualGroup(
    Guid TargetId,
    string TargetName,
    string Filter,
    FilterPurpose Purpose,
    int DiskCount,
    ManualReason Reason,
    IReadOnlyList<ManualPlan> Plans);

/// <summary>One competing TS plan inside a <see cref="ManualGroup"/>: its TS id and current catalog-side counts.</summary>
public sealed record ManualPlan(long TsExposurePlanId, int CatalogAcquired, int CatalogAccepted, int Desired);

/// <summary>A TS target-level issue surfaced by the build (name mismatch / ambiguous / duplicate / unanchored / invalid).</summary>
public sealed record ReconcileNote(string Kind, string TargetName, string Detail);
