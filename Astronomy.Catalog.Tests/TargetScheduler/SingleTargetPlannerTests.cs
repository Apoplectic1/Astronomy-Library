using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The surgical single-target write-back planner: anchor each scanned unit to a TS target by coordinates, then route
// each (filter, purpose, binning) cell to its TS plan. Hermetic — synthetic TargetReport units + TsPlanData.
public sealed class SingleTargetPlannerTests
{
    [Fact]
    public void Normal_SingleUnit_WritesMatchingPlan()
    {
        TargetReport[] units = [Unit("Sh2-174 - Valentine", 23.0, 80.0, Cell("H", FilterPurpose.Light, 40, bin: 1))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Sh2-174", 23.001, 80.0, project: 10)],
            [Tpl(1000, "Ha", "H", bin: 1)],
            [TsP(500, target: 1, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Sh2-174 - Valentine", ts);

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(500, w.TsExposurePlanId);
        Assert.Equal("H", w.Filter);
        Assert.Equal(40, w.DiskCount);
        Assert.Empty(plan.Manual);
        Assert.Empty(plan.NeedsReconciliation);
    }

    [Fact]
    public void NonServingFraming_DoesNotStamp_AndSaysWhy()
    {
        // The surgical path honours the shared serving rule (openspec rotation-framing-key): a cell whose
        // sky framing fails the anchored target's rotation must not credit the plan — it is surfaced as a
        // FramingMismatch note (a count that visibly did not move deserves its stated reason). A mechanical
        // cell is not comparable and still stamps.
        FramingCluster oldFraming = new(1, RotationExpression.Sky, 20.0, null, null, 199);
        FramingCluster mech = new(2, RotationExpression.Mechanical, 97.3, null, null, 40);
        TargetReport[] units = [Unit("Sh2-101 - Tulip", 20.0, 35.0,
            Cell("H", FilterPurpose.Light, 199, bin: 1, framing: oldFraming),
            Cell("O", FilterPurpose.Light, 40, bin: 1, framing: mech))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Tulip", 20.001, 35.0, project: 10, rotation: 160.01)],
            [Tpl(1000, "Ha", "H", bin: 1), Tpl(1001, "O3", "O", bin: 1)],
            [TsP(500, target: 1, template: 1000), TsP(501, target: 1, template: 1001)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Sh2-101 - Tulip", ts);

        PlannedWrite w = Assert.Single(plan.Writes);           // only the mechanical cell stamps
        Assert.Equal(501, w.TsExposurePlanId);
        Assert.Equal(40, w.DiskCount);
        ReconcileNote note = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal("FramingMismatch", note.Kind);
        Assert.Empty(plan.Manual);
    }

    [Fact]
    public void ExposureSplitCells_RouteToTheirOwnSecondsPlans()
    {
        // Disk: H Light at two sub lengths; TS: one plan per duration. Each cell writes its own plan —
        // the plan's duration is its spec.
        TargetReport[] units = [Unit("Sh2-174 - Valentine", 23.0, 80.0,
            Cell("H", FilterPurpose.Light, 28, bin: 1, seconds: 120.0),
            Cell("H", FilterPurpose.Light, 47, bin: 1, seconds: 300.0))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Sh2-174", 23.001, 80.0, project: 10)],
            [Tpl(1000, "Ha fast", "H", bin: 1, defExp: 120.0), Tpl(1001, "Ha", "H", bin: 1, defExp: 300.0)],
            [TsP(500, target: 1, template: 1000, exposure: -1), TsP(501, target: 1, template: 1001, exposure: -1)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Sh2-174 - Valentine", ts);

        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 500 && w.DiskCount == 28 && w.PlanSeconds == 120);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 501 && w.DiskCount == 47 && w.PlanSeconds == 300);
        Assert.Empty(plan.Manual);
        Assert.Empty(plan.NeedsReconciliation);
    }

    [Fact]
    public void SecondsMismatch_NoSameSecondsPlan_EmitsUnplannedNote_NotManual()
    {
        // 120s frames against a target whose only plan is 300s: no plan at this duration exists at any
        // binning — informational note, never manual (write-back doesn't create plans).
        TargetReport[] units = [Unit("Medusa", 7.48, 13.29, Cell("H", FilterPurpose.Light, 25, bin: 1, seconds: 120.0))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Medusa", 7.48, 13.29, project: 10)],
            [Tpl(1000, "Ha", "H", bin: 1, defExp: 300.0)],
            [TsP(500, target: 1, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Medusa", ts);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal(ReconcileNote.UnplannedFramesKind, n.Kind);
        Assert.Contains("25 frames @120s", n.Detail);
    }

    [Fact]
    public void SecondsMismatch_SameSecondsOtherBinExists_IsManualNoMatchingPlan()
    {
        // A same-duration plan exists at a different binning — an equipment-identity question, shown as
        // context; the 300s plan at the right binning is irrelevant to this 120s cell.
        TargetReport[] units = [Unit("Wide", 5.0, 10.0, Cell("H", FilterPurpose.Light, 25, bin: 2, seconds: 120.0))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Wide", 5.0, 10.0, project: 10)],
            [Tpl(1000, "Ha 1x1 fast", "H", bin: 1, defExp: 120.0), Tpl(1001, "Ha 2x2", "H", bin: 2, defExp: 300.0)],
            [TsP(500, target: 1, template: 1000, exposure: 120.0), TsP(501, target: 1, template: 1001)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Wide", ts);

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.NoMatchingPlan, g.Reason);
        Assert.Equal(120, g.Seconds);
        ManualPlan ctx = Assert.Single(g.Plans);
        Assert.Equal(500, ctx.TsExposurePlanId);   // only the same-seconds 1x1 plan shown as context
        Assert.Equal(120, ctx.PlanSeconds);
    }

    [Fact]
    public void PlanExposureMinusOne_UsesTemplateDefault()
    {
        // Raw TS sentinel: exposure -1 means "use the template default".
        TargetReport[] units = [Unit("Wide", 5.0, 10.0, Cell("H", FilterPurpose.Light, 30, bin: 1, seconds: 600.0))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Wide", 5.0, 10.0, project: 10)],
            [Tpl(1000, "Ha", "H", bin: 1, defExp: 600.0)],
            [TsP(500, target: 1, template: 1000, exposure: -1)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Wide", ts);

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(30, w.DiskCount);
        Assert.Equal(600, w.PlanSeconds);
    }

    [Fact]
    public void Mosaic_PanelsWriteTheirOwnPanelPlans()
    {
        TargetReport[] units =
        [
            Unit("Panel 01of02", 20.50, 30.50, Cell("H", FilterPurpose.Light, 30, bin: 2)),
            Unit("Panel 02of02", 21.30, 31.30, Cell("H", FilterPurpose.Light, 20, bin: 2)),   // >0.5° from panel 1
        ];
        TsPlanData ts = new(
            [Proj(20, "Mosaic - Cygnus Loop", mosaic: true)],
            [TsT(1, "CygnusLoop P1", 20.50, 30.50, project: 20),
             TsT(2, "CygnusLoop P2", 21.30, 31.30, project: 20)],
            [Tpl(1000, "Ha", "H", bin: 2)],
            [TsP(101, target: 1, template: 1000), TsP(102, target: 2, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: true, "Mosaic - Cygnus Loop", ts);

        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 101 && w.DiskCount == 30);   // panel 1 → its plan
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 102 && w.DiskCount == 20);   // panel 2 → its plan
        Assert.Empty(plan.Manual);
        Assert.Empty(plan.NeedsReconciliation);
    }

    [Fact]
    public void Binning_DisambiguatesTwoPlansOfTheSameFilter()
    {
        TargetReport[] units = [Unit("Wide", 5.0, 10.0, Cell("H", FilterPurpose.Light, 25, bin: 2))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Wide", 5.0, 10.0, project: 10)],
            [Tpl(1000, "Ha 1x1", "H", bin: 1), Tpl(1001, "Ha 2x2", "H", bin: 2)],   // same filter+purpose, two bins
            [TsP(500, target: 1, template: 1000), TsP(501, target: 1, template: 1001)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Wide", ts);

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(501, w.TsExposurePlanId);   // the 2x2 plan, never the 1x1
        Assert.Empty(plan.Manual);
    }

    [Fact]
    public void NoMatchingBinPlan_IsManual_NotWritten_AndShowsContext()
    {
        TargetReport[] units = [Unit("Wide", 5.0, 10.0, Cell("H", FilterPurpose.Light, 25, bin: 2))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Wide", 5.0, 10.0, project: 10)],
            [Tpl(1000, "Ha 1x1", "H", bin: 1)],   // only a 1x1 plan; the disk cell is 2x2
            [TsP(500, target: 1, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Wide", ts);

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.NoMatchingPlan, g.Reason);
        Assert.Equal("H", g.Filter);
        Assert.Equal(25, g.DiskCount);
        Assert.Equal(500, Assert.Single(g.Plans).TsExposurePlanId);   // the 1x1 plan shown as context
    }

    [Fact]
    public void SamePurposeMultiPlan_SameBin_IsManual()
    {
        TargetReport[] units = [Unit("Wide", 5.0, 10.0, Cell("H", FilterPurpose.Light, 25, bin: 1))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Wide", 5.0, 10.0, project: 10)],
            [Tpl(1000, "Ha", "H", bin: 1), Tpl(1001, "Ha fast", "H", bin: 1)],   // two Light 1x1 H plans — can't split
            [TsP(500, target: 1, template: 1000), TsP(501, target: 1, template: 1001)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Wide", ts);

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.MultiPlan, g.Reason);
        Assert.Equal(2, g.Plans.Count);
    }

    [Fact]
    public void UnitBeyondTolerance_IsReconcileNote_NotWritten()
    {
        TargetReport[] units = [Unit("Sh2-174 - Valentine", 23.0, 80.0, Cell("H", FilterPurpose.Light, 40, bin: 1))];
        TsPlanData ts = new(
            [Proj(10, "Proj", mosaic: false)],
            [TsT(1, "Sh2-174", 23.0, 79.0, project: 10)],   // 1.0° away in dec, beyond the 0.5° tolerance
            [Tpl(1000, "Ha", "H", bin: 1)],
            [TsP(500, target: 1, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "Sh2-174 - Valentine", ts);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal("UnitUnmatched", n.Kind);
        Assert.Equal("Sh2-174 - Valentine", n.TargetName);
    }

    [Fact]
    public void Mosaic_NoMatchingProject_IsReconcileNote_NotWritten()
    {
        TargetReport[] units = [Unit("Panel 01of02", 20.5, 30.5, Cell("H", FilterPurpose.Light, 30, bin: 2))];
        TsPlanData ts = new(
            [Proj(20, "Mosaic - Something Else", mosaic: true)],   // a mosaic project, but not the one we scanned
            [TsT(1, "SomethingElse P1", 20.5, 30.5, project: 20)],
            [Tpl(1000, "Ha", "H", bin: 2)],
            [TsP(101, target: 1, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: true, "Mosaic - Cygnus Loop", ts);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal("MosaicUnmatched", n.Kind);
    }

    [Fact]
    public void Normal_DoesNotAnchorToAMosaicPanel()
    {
        // A standalone target sitting exactly on a mosaic panel's coords must not write the panel's plan — panels
        // are name-matched, never coordinate-matched, on the normal path (mirrors TargetResolver).
        TargetReport[] units = [Unit("NGC 6995 - Eastern Veil", 20.9, 30.9, Cell("S", FilterPurpose.Light, 23, bin: 2))];
        TsPlanData ts = new(
            [Proj(20, "Mosaic - Cygnus Loop", mosaic: true)],
            [TsT(3, "CygnusLoop P3", 20.9, 30.9, project: 20)],   // same coords as the disk target
            [Tpl(1000, "SII", "S", bin: 2)],
            [TsP(102, target: 3, template: 1000)]);

        WriteBackPlan plan = SingleTargetPlanner.Plan(units, isMosaic: false, "NGC 6995 - Eastern Veil", ts);

        Assert.Empty(plan.Writes);                       // did NOT grab the panel's plan
        Assert.Empty(plan.Manual);
        Assert.Equal("UnitUnmatched", Assert.Single(plan.NeedsReconciliation).Kind);
    }

    // ---- synthetic builders -------------------------------------------------

    private static readonly FramingCluster TestFraming =
        new(0, RotationExpression.Sky, 20.0, null, null, 12);

    private static TargetReport Unit(string label, double raHours, double dec, params FilterAggregate[] cells)
    {
        (string cat, string? common) = TargetReport.SplitDirectoryName(label);
        return new TargetReport(label, cat, common, cat, raHours, dec, cells, [TestFraming]);
    }

    private static FilterAggregate Cell(
        string filter, FilterPurpose purpose, int count, int bin, double seconds = 300.0,
        FramingCluster? framing = null)
    {
        DateTime first = new(2024, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        return new FilterAggregate(filter, filter, purpose, count, TimeSpan.FromSeconds(count * seconds),
            first, first.AddHours(1), new TypicalSettings(100, 50, -10.0, (bin, bin), seconds), "Z533",
            framing ?? TestFraming);
    }

    private static TsProject Proj(long id, string name, bool mosaic) =>
        new(id, "profile-1", name, 1, 1, null, mosaic ? 1 : 0, "g-p" + id);

    private static TsTarget TsT(long id, string name, double ra, double dec, long project, double? rotation = null) =>
        new(id, name, 1, ra, dec, 2, rotation, null, project, -1, "g-t" + id);

    private static TsExposureTemplate Tpl(long id, string name, string filter, int bin, double defExp = 300.0) =>
        new(id, "profile-1", name, filter, 100, 50, bin, defExp);

    private static TsExposurePlan TsP(
        long id, long target, long template, int desired = 60, int acquired = 0, int accepted = 0,
        double exposure = 300.0) =>
        new(id, "profile-1", exposure, desired, acquired, accepted, target, template);
}
