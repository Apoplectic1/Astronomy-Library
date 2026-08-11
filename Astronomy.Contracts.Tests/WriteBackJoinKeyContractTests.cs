using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #27 — write-back's join key and pairing-credited
/// counting. Deliberately overlaps Astronomy.Catalog.Tests' planner suite (bench scope rule:
/// the label and the failure message are the point).
/// </summary>
public sealed class WriteBackJoinKeyContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #27:
    //   "Write-back's join key is (target, filter, purpose, whole-second exposure),
    //    credited by pairing." A plan receives the disk count at exactly its
    //    round(ExposureSeconds ?? template default) bucket (filter compared
    //    ordinal-ignore-case); within the bucket only frames whose capture
    //    configuration PAIRS with the plan's template credit; a disk bucket with no
    //    plan at that duration surfaces as an UnplannedFrames note and is never
    //    folded into a neighbouring plan; and no pairing frames at the plan's spec
    //    is a REAL DiskCount = 0 write, not a skip. A silent-wrong-result surface:
    //    a duration or configuration mismatch writes 0 to a live TS plan.
    // ---------------------------------------------------------------------------

    [Fact]
    public void SamePurpose_DifferentDurations_ResolveToSeparateWrites_NotManual()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [
                Plan(t, tpl, tsId: 500, seconds: 120.0),   // explicit 120 s bucket
                Plan(t, tpl, tsId: 501),                   // template default 300 s bucket
            ],
            [Tpl(tpl, "H", "H")],
            [
                Inv(t, "H", FilterPurpose.Light, 11, seconds: 120.0),
                Inv(t, "H", FilterPurpose.Light, 7, seconds: 300.0),
            ],
            Report());

        Assert.Equal(2, plan.Writes.Count);
        Assert.Empty(plan.Manual);   // separate durations are separate writes, never a manual hold
        Assert.Equal(11, Assert.Single(plan.Writes, w => w.TsExposurePlanId == 500).DiskCount);
        Assert.Equal(7, Assert.Single(plan.Writes, w => w.TsExposurePlanId == 501).DiskCount);
    }

    [Fact]
    public void DiskBucketWithNoPlanAtThatDuration_SurfacesAsNote_NeverFoldsIn()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 500)],                                  // 300 s (template default)
            [Tpl(tpl, "H", "H")],
            [
                Inv(t, "H", FilterPurpose.Light, 9, seconds: 120.0),    // no plan at 120 s
                Inv(t, "H", FilterPurpose.Light, 7, seconds: 300.0),
            ],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(7, w.DiskCount);   // the 120 s frames are NOT folded into the 300 s plan
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal(ReconcileNote.UnplannedFramesKind, n.Kind);
    }

    [Fact]
    public void NoPairingFramesAtThePlansSpec_IsARealZeroWrite_NotASkip()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 500, desired: 18, acquired: 18, accepted: 18)],
            [Tpl(tpl, "H", "H", gain: 0)],
            [Inv(t, "H", FilterPurpose.Light, 18, gain: 53)],           // same bucket, non-pairing config
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);   // the write EXISTS ("spec unmet") ...
        Assert.Equal(0, w.DiskCount);                  // ... and stamps a real zero
    }

    [Fact]
    public void Filter_ComparedOrdinalIgnoreCase()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 500)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "h", FilterPurpose.Light, 47)],    // lowercase disk filter still joins
            Report());

        Assert.Equal(47, Assert.Single(plan.Writes).DiskCount);
    }

    // ---- minimal fixture builders (mirror Astronomy.Catalog.Tests' planner suite) ------------------

    private static Target Both(Guid id, string name) => new(
        id, TargetSource.Both, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: name, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: 0, CreatedAt: 0, ImportedFromTsGuid: null);

    private static ExposureTemplate Tpl(
        Guid id, string name, string filter, double? defExp = 300.0,
        int? gain = 100, int? offset = 50, int? bin = 1) =>
        new(id, Guid.NewGuid(), name, filter, Gain: gain, OffsetAdu: offset, Binning: bin, ReadoutMode: null,
            DefaultExposureSeconds: defExp, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(
        Guid target, Guid template, long tsId, int desired = 0, int acquired = 0, int accepted = 0,
        double? seconds = null) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: seconds, desired, acquired, accepted,
            Enabled: true, ImportedFromTsGuid: tsId.ToString(CultureInfo.InvariantCulture));

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds = 300.0,
        int gain = 100, int offset = 50) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: gain, TypicalOffset: offset, TypicalSetTempC: -10.0, TypicalBinningX: 1,
            TypicalBinningY: 1, ExposureSeconds: seconds, Camera: "Z533",
            FramingOrdinal: 0, RotationExpression: RotationExpression.Sky, RotationFoldDeg: 20.0);

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);
}
