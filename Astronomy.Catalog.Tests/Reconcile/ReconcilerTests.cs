using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class ReconcilerTests
{
    [Fact]
    public void Combined_StarsCountTowardGoal_LightOnlyDoesNot()
    {
        Guid target = Guid.NewGuid();
        Guid tplH = Guid.NewGuid(), tplB = Guid.NewGuid();
        Target[] targets = [T(target, "Wizard", TargetSource.Both)];
        ExposureTemplate[] templates = [Tpl(tplH, "H"), Tpl(tplB, "B")];
        ExposurePlan[] plans = [Plan(target, tplH, desired: 64), Plan(target, tplB, desired: 32)];
        // H shot deep as Light; B shot only as Stars (the SHO/RGB-stars case).
        InventoryFilter[] inv =
        [
            Inv(target, "H", FilterPurpose.Light, 140),
            Inv(target, "B", FilterPurpose.Stars, 22),
        ];

        // Combined: B's 22 Stars count → in progress, not zero.
        TargetReconciliation combined = Assert.Single(
            Reconciler.Reconcile(targets, plans, templates, inv, ReconcilePolicy.Combined));
        FilterReconciliation cH = combined.Filters.Single(f => f.Filter == "H");
        FilterReconciliation cB = combined.Filters.Single(f => f.Filter == "B");
        Assert.Equal(ReconcileStatus.Complete, cH.Status);       // 140 ≥ 64
        Assert.Equal(140, cH.AcquiredCount);
        Assert.Equal(ReconcileStatus.InProgress, cB.Status);     // 22 of 32 via Stars
        Assert.Equal(22, cB.AcquiredCount);
        Assert.Equal(0, cB.LightCount);
        Assert.Equal(22, cB.StarsCount);
        Assert.Equal(10, cB.RemainingCount);
        Assert.Equal(ReconcileStatus.InProgress, combined.Status);
        Assert.Equal(10, combined.TotalRemaining);               // only B short

        // LightOnly: B has 0 Light → not started.
        TargetReconciliation light = Assert.Single(
            Reconciler.Reconcile(targets, plans, templates, inv, ReconcilePolicy.LightOnly));
        FilterReconciliation lB = light.Filters.Single(f => f.Filter == "B");
        Assert.Equal(0, lB.AcquiredCount);
        Assert.Equal(ReconcileStatus.NotStarted, lB.Status);
        Assert.Equal(22, lB.StarsCount);                          // breakdown still carried
    }

    [Fact]
    public void Complete_WhenActualMeetsOrExceedsGoal()
    {
        Guid target = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Done", TargetSource.Both)], [Plan(target, tpl, 60)], [Tpl(tpl, "L")],
            [Inv(target, "L", FilterPurpose.Light, 60)]));
        Assert.Equal(ReconcileStatus.Complete, r.Status);
        Assert.Equal(0, r.TotalRemaining);
        Assert.Equal(1.0, r.FractionComplete);
    }

    [Fact]
    public void Unplanned_WhenShotWithNoGoal()
    {
        Guid target = Guid.NewGuid();
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Actual only", TargetSource.Actual)], [], [],
            [Inv(target, "L", FilterPurpose.Light, 50)]));
        FilterReconciliation f = Assert.Single(r.Filters);
        Assert.Equal(ReconcileStatus.Unplanned, f.Status);
        Assert.Equal(0, f.DesiredCount);
        Assert.Equal(50, f.AcquiredCount);
        Assert.Equal(ReconcileStatus.Unplanned, r.Status);
    }

    [Fact]
    public void NotStarted_WhenGoalButNoFrames()
    {
        Guid target = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Planned only", TargetSource.Planned)], [Plan(target, tpl, 30)], [Tpl(tpl, "R")], []));
        FilterReconciliation f = Assert.Single(r.Filters);
        Assert.Equal(ReconcileStatus.NotStarted, f.Status);
        Assert.Equal(30, f.RemainingCount);
        Assert.Equal(ReconcileStatus.NotStarted, r.Status);
    }

    [Fact]
    public void FullOuterJoin_GoalOnlyAndActualOnlyFiltersBothAppear()
    {
        Guid target = Guid.NewGuid(), tpl = Guid.NewGuid();
        // Goal for H, but only O was actually shot.
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Mismatch", TargetSource.Both)], [Plan(target, tpl, 64)], [Tpl(tpl, "H")],
            [Inv(target, "O", FilterPurpose.Light, 40)]));
        Assert.Equal(2, r.Filters.Count);
        Assert.Equal(ReconcileStatus.NotStarted, r.Filters.Single(f => f.Filter == "H").Status);
        Assert.Equal(ReconcileStatus.Unplanned, r.Filters.Single(f => f.Filter == "O").Status);
    }

    [Fact]
    public void OverShotFilter_DoesNotMaskAnotherFiltersGap()
    {
        Guid target = Guid.NewGuid(), tH = Guid.NewGuid(), tS = Guid.NewGuid();
        // H overshot (200/64), S barely started (5/64). Target must not read complete.
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Lopsided", TargetSource.Both)],
            [Plan(target, tH, 64), Plan(target, tS, 64)], [Tpl(tH, "H"), Tpl(tS, "S")],
            [Inv(target, "H", FilterPurpose.Light, 200), Inv(target, "S", FilterPurpose.Light, 5)]));
        Assert.Equal(ReconcileStatus.InProgress, r.Status);
        Assert.Equal(59, r.TotalRemaining);                      // 0 (H met) + 59 (S short)
    }

    [Fact]
    public void ExposureSplitInventoryRows_SumIntoOneFilterActual()
    {
        Guid target = Guid.NewGuid(), tH = Guid.NewGuid();
        // The scanner emits one inventory row per (filter, purpose, exposure); the reconciler must sum
        // them — 28×120s + 47×300s reads as 75 H frames against the goal.
        TargetReconciliation r = Assert.Single(Reconciler.Reconcile(
            [T(target, "Split", TargetSource.Both)],
            [Plan(target, tH, 80)], [Tpl(tH, "H")],
            [Inv(target, "H", FilterPurpose.Light, 28, seconds: 120.0),
             Inv(target, "H", FilterPurpose.Light, 47, seconds: 300.0)]));
        FilterReconciliation f = Assert.Single(r.Filters);
        Assert.Equal(75, f.AcquiredCount);
        Assert.Equal(5, r.TotalRemaining);
    }

    // ---- builders -----------------------------------------------------------

    private static Target T(Guid id, string name, TargetSource source) => new(
        id, source, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null, Epoch.J2000,
        RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: null, Catalog: null, CommonName: null,
        ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: null);

    private static ExposureTemplate Tpl(Guid id, string filter) =>
        new(id, Guid.NewGuid(), filter, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: null, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(Guid target, Guid template, int desired) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: null, desired, AcquiredCount: 0, AcceptedCount: 0,
            Enabled: true, ImportedFromTsGuid: null);

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds = 300.0) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1,
            ExposureSeconds: seconds, Cameras: "Z533");
}
