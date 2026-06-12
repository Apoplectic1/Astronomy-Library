using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Xunit;

namespace Astronomy.Catalog.Tests;

/// <summary>
/// The cell join lifted out of a consumer's grid loader (R1): plan commitments + disk actuals aggregated per
/// (target, filter, purpose, seconds), tagged with match-state. These pin the domain core; the consuming app
/// keeps its own tests for the grid shaping (planes/rollups/hours) over these cells.
/// </summary>
public sealed class ReconciliationProjectionTests
{
    [Fact]
    public void PlanAndDisk_SameBucket_MergeIntoOneCell()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, acquired: 6, accepted: 5, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal("H", c.Filter);
        Assert.Equal(FilterPurpose.Light, c.Purpose);
        Assert.Equal(300, c.Seconds);
        Assert.Equal(10, c.Desired);
        Assert.Equal(6, c.Acquired);
        Assert.Equal(5, c.Accepted);
        Assert.Equal(4, c.Disk);
        Assert.Equal(1, c.PlanCount);
    }

    [Fact]
    public void TwoPlans_SameBucket_SumDesiredAndCountPlans()
    {
        Guid t = Guid.NewGuid(), tpl1 = Guid.NewGuid(), tpl2 = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 17", TargetSource.Planned)],
                [Plan(t, tpl1, desired: 10, seconds: 900.0), Plan(t, tpl2, desired: 5, seconds: 900.0)],
                [Tpl(tpl1, "H", "H"), Tpl(tpl2, "H", "H")], []),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);   // same (filter, purpose, seconds) → one bucket
        Assert.Equal(15, c.Desired);
        Assert.Equal(2, c.PlanCount);
        Assert.Equal(0, c.Disk);
    }

    [Fact]
    public void PlanAndDisk_DifferentSeconds_StayDistinctCells()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 600.0)]),     // frames at a different sub length
            Report()));

        Assert.Equal(2, tc.Cells.Count);
        ReconciliationCell plan = tc.Cells.Single(c => c.Seconds == 300);
        ReconciliationCell disk = tc.Cells.Single(c => c.Seconds == 600);
        Assert.Equal(10, plan.Desired);
        Assert.Equal(0, plan.Disk);
        Assert.Equal(0, disk.PlanCount);
        Assert.Equal(4, disk.Disk);
    }

    [Fact]
    public void Purpose_DerivedFromTemplateName_SplitsLightFromStars()
    {
        Guid t = Guid.NewGuid(), light = Guid.NewGuid(), stars = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "NGC 7000", TargetSource.Planned)],
                [Plan(t, light, desired: 30, seconds: 300.0), Plan(t, stars, desired: 12, seconds: 30.0)],
                [Tpl(light, "H", "H"), Tpl(stars, "Stars H", "H")], []),   // "Stars " prefix → Stars purpose
            Report()));

        Assert.Equal(2, tc.Cells.Count);
        Assert.Equal(FilterPurpose.Light, tc.Cells.Single(c => c.Seconds == 300).Purpose);
        Assert.Equal(FilterPurpose.Stars, tc.Cells.Single(c => c.Seconds == 30).Purpose);
    }

    [Fact]
    public void IssueFlags_AndProjectName_FlowFromReportAndGraph()
    {
        Guid t = Guid.NewGuid();
        Guid proj = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "NGC 6995", TargetSource.Both, dir: "CygnusLoop P3", projectId: proj)], [], [], [],
                projects: [Proj(proj, "Cygnus")]),
            Report(nameMismatches: [new NameMismatch(null, "NGC 6995", "CygnusLoop P3", null, 0.2)])));

        Assert.True(tc.Issues.HasFlag(TargetMatchIssues.NameMismatch));
        Assert.Equal("Cygnus", tc.ProjectName);
        Assert.False(tc.IsUnanchored);
    }

    [Fact]
    public void UnanchoredPlannedTarget_FlaggedWithEmptyCells()
    {
        Guid t = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "LBN 437", TargetSource.Planned)], [], [], []),
            Report(unanchored: [new UnanchoredTsTarget(null, "LBN 437")])));

        Assert.True(tc.IsUnanchored);
        Assert.Empty(tc.Cells);
        Assert.Equal("—", tc.ProjectName);            // no project association
    }

    [Fact]
    public void Mosaic_ParentHasNoCells_ChildrenCarryCellsAndParentId()
    {
        Guid parent = Guid.NewGuid(), p1 = Guid.NewGuid(), p2 = Guid.NewGuid(), tpl = Guid.NewGuid();
        const string dir1 = "Mosaic - X/Panel 01of16", dir2 = "Mosaic - X/Panel 02of16";
        IReadOnlyList<TargetCells> projected = ReconciliationProjection.Project(
            Graph(
                [
                    T(parent, "Mosaic - X", TargetSource.Both, dir: "Mosaic - X"),
                    T(p1, "X P1", TargetSource.Both, dir: dir1, parent: parent),
                    T(p2, "X P2", TargetSource.Actual, dir: dir2, parent: parent),
                ],
                [Plan(p1, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(p1, "H", FilterPurpose.Light, 4, 300.0), Inv(p2, "H", FilterPurpose.Light, 7, 300.0)]),
            Report());

        Assert.Equal(3, projected.Count);
        TargetCells top = projected.Single(p => p.TargetId == parent);
        Assert.Null(top.ParentTargetId);
        Assert.True(top.IsMosaicDirectory);
        Assert.Empty(top.Cells);                       // grouping node: no plans, no inventory

        TargetCells child1 = projected.Single(p => p.TargetId == p1);
        Assert.Equal(parent, child1.ParentTargetId);
        Assert.True(child1.IsMosaicDirectory);
        ReconciliationCell c = Assert.Single(child1.Cells);
        Assert.Equal(10, c.Desired);
        Assert.Equal(4, c.Disk);

        Assert.Equal(7, Assert.Single(projected.Single(p => p.TargetId == p2).Cells).Disk);
    }

    [Fact]
    public void PlanWithoutExposure_FallsBackToTemplateDefaultSeconds()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Pelican", TargetSource.Planned)],
                [Plan(t, tpl, desired: 20, seconds: null)],     // plan has no exposure → template default
                [Tpl(tpl, "O", "O", defaultSeconds: 600.0)], []),
            Report()));

        Assert.Equal(600, Assert.Single(tc.Cells).Seconds);
    }

    [Fact]
    public void EnableState_AndProvenance_FlowFromTarget()
    {
        Guid both = Guid.NewGuid(), disk = Guid.NewGuid();
        IReadOnlyList<TargetCells> projected = ReconciliationProjection.Project(
            Graph(
                [
                    T(both, "M 81", TargetSource.Both, dir: "M 81", enabled: false, tsKey: "ts-guid-81"),
                    T(disk, "Stray", TargetSource.Actual, dir: "Stray"),   // disk-only: no TS target
                ], [], [], []),
            Report());

        TargetCells m81 = projected.Single(p => p.Name == "M 81");
        Assert.False(m81.Enabled);                 // TS active=0 flows through
        Assert.Equal("ts-guid-81", m81.TsTargetKey);

        TargetCells stray = projected.Single(p => p.Name == "Stray");
        Assert.True(stray.Enabled);                // disk-only defaults enabled
        Assert.Null(stray.TsTargetKey);            // no TS target behind it → no enable checkbox downstream
    }

    // ---- builders (mirroring the App's BuildRowsTests / ReconcilerTests) -----

    private static CatalogGraph Graph(
        IReadOnlyList<Target> targets,
        IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates,
        IReadOnlyList<InventoryFilter> inventory,
        IReadOnlyList<Project>? projects = null) =>
        new(Profiles: [], projects ?? [], templates, targets, plans, inventory);

    private static CatalogBuildReport Report(
        IReadOnlyList<NameMismatch>? nameMismatches = null,
        IReadOnlyList<UnanchoredTsTarget>? unanchored = null) =>
        new(0, 0, 0, 0, 0, nameMismatches ?? [], [], [], [], unanchored ?? [], []);

    private static Target T(
        Guid id, string name, TargetSource source, string? dir = null, Guid? parent = null, Guid? projectId = null,
        bool enabled = true, string? tsKey = null) =>
        new(id, source, projectId, name, Enabled: enabled, RaHours: null, DecDegreesSigned: null, Epoch.J2000,
            RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: dir, Catalog: null,
            CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: tsKey,
            ParentTargetId: parent);

    private static Project Proj(Guid id, string name) =>
        new(id, Guid.NewGuid(), name, Description: null, ProjectState.Active, ProjectPriority.Normal,
            MinimumAltitudeDeg: null, MaximumAltitudeDeg: null, MinimumTimeMinutes: null, UseCustomHorizon: false,
            HorizonOffsetDeg: null, MeridianWindowMinutes: null, IsMosaic: false, EnableGrader: false,
            CreatedAt: 0, ActiveAt: null, InactiveAt: null, ImportedFromTsGuid: null);

    private static ExposureTemplate Tpl(Guid id, string name, string filter, double? defaultSeconds = 300.0) =>
        new(id, Guid.NewGuid(), name, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: defaultSeconds, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(
        Guid target, Guid template, int desired, double? seconds, int acquired = 0, int accepted = 0) =>
        new(Guid.NewGuid(), target, template, seconds, desired, acquired, accepted,
            Enabled: true, ImportedFromTsGuid: null);

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1,
            ExposureSeconds: seconds, Cameras: "Z533");
}
