using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

// The surgical `tcm writeback --target` planner: anchor each scanned unit to a TS target by coordinates, then route
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

    private static TargetReport Unit(string label, double raHours, double dec, params FilterAggregate[] cells)
    {
        (string cat, string? common) = TargetReport.SplitDirectoryName(label);
        return new TargetReport(label, cat, common, cat, raHours, dec, cells);
    }

    private static FilterAggregate Cell(string filter, FilterPurpose purpose, int count, int bin)
    {
        DateTime first = new(2024, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        return new FilterAggregate(filter, filter, purpose, count, TimeSpan.FromSeconds(count * 300.0),
            first, first.AddHours(1), new TypicalSettings(100, 50, -10.0, (bin, bin), 300.0), ["Z533"]);
    }

    private static TsProject Proj(long id, string name, bool mosaic) =>
        new(id, "profile-1", name, 1, 1, null, mosaic ? 1 : 0, "g-p" + id);

    private static TsTarget TsT(long id, string name, double ra, double dec, long project) =>
        new(id, name, 1, ra, dec, 2, null, null, project, -1, "g-t" + id);

    private static TsExposureTemplate Tpl(long id, string name, string filter, int bin) =>
        new(id, "profile-1", name, filter, 100, 50, bin, 300.0);

    private static TsExposurePlan TsP(long id, long target, long template, int desired = 60, int acquired = 0, int accepted = 0) =>
        new(id, "profile-1", 300.0, desired, acquired, accepted, target, template);
}
