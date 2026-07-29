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

    [Theory]
    // A plan and captured frames pair only when the whole capture configuration agrees. Each row varies one
    // dimension of the disk side away from the plan's 100 / 50 / bin 1.
    [InlineData(53, 50, 1)]    // the 2024 broadband gain switch
    [InlineData(100, 10, 1)]   // the offset-50 frames scattered through every filter
    [InlineData(100, 50, 2)]   // bin 2 frames do not stack with bin 1
    public void PlanAndDisk_ConfigurationDiffers_DoNotPair(int diskGain, int diskOffset, int diskBin)
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, gain: diskGain, offset: diskOffset, bin: diskBin)]),
            Report()));

        // Two cells: the plan alone, and the captured frames alone — never one merged row asserting the
        // frames satisfy the plan.
        Assert.Equal(2, tc.Cells.Count);
        ReconciliationCell planOnly = Assert.Single(tc.Cells, c => c.PlanCount == 1);
        ReconciliationCell diskOnly = Assert.Single(tc.Cells, c => c.PlanCount == 0);
        Assert.Equal(0, planOnly.Disk);
        Assert.Equal(10, planOnly.Desired);
        Assert.Equal(4, diskOnly.Disk);
        Assert.Equal(0, diskOnly.Desired);
    }

    [Fact]
    public void PlanAndDisk_ConfigurationAgrees_PairIntoOneCellCarryingIt()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0)]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal(1, c.PlanCount);
        Assert.Equal(4, c.Disk);
        Assert.Equal(100, c.Gain);
        Assert.Equal(50, c.Offset);
        Assert.Equal(1, c.BinningX);
        Assert.Equal("Z533", c.Camera);
    }

    [Fact]
    public void Camera_IsDiskSideOnly_AndNeverPreventsPairing()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        // Same configuration, captured on a camera the plan cannot name: still one paired cell.
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, camera: "Z183")]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal(1, c.PlanCount);
        Assert.Equal("Z183", c.Camera);
    }

    [Fact]
    public void PlanWithNoDiskSide_CarriesNoCamera()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Planned)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")], []),
            Report()));

        Assert.Null(Assert.Single(tc.Cells).Camera);
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

    // ---- framing (openspec rotation-framing-key → framing-keys) --------------

    [Fact]
    public void Framing_PlanMatchingCluster_Pairs_WhileLargerClusterSeparates()
    {
        // The Barnard 202 shape: plan rotation 50°, disk clusters 50° (28 frames) and 60° (451 frames).
        // The plan pairs with the agreeing minority; the majority is history that no longer serves it.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Barnard 202", TargetSource.Both, dir: "Barnard 202", rotation: 50.0)],
                [Plan(t, tpl, desired: 100, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 28, 300.0, framingOrdinal: 1, rotationFold: 50.0),
                    Inv(t, "H", FilterPurpose.Light, 451, 300.0, framingOrdinal: 0, rotationFold: 60.0),
                ]),
            Report()));

        Assert.Equal(2, tc.Cells.Count);
        ReconciliationCell paired = Assert.Single(tc.Cells, c => c.PlanCount == 1);
        Assert.Equal(28, paired.Disk);                       // the minority pairs — the plan wins, not the majority
        Assert.Equal(50.0, paired.DiskRotationFoldDeg!.Value, 3);
        Assert.False(paired.FramingDisagrees);

        ReconciliationCell separated = Assert.Single(tc.Cells, c => c.PlanCount == 0);
        Assert.Equal(451, separated.Disk);
        Assert.True(separated.FramingDisagrees);             // the badge cue: sky rotation failing the plan
        Assert.Equal(50.0, tc.TargetRotationDeg!.Value, 3);
    }

    [Fact]
    public void Framing_FlippedPlan_StillPairs()
    {
        // The Bear Claw shape: plan rotation 0°, every frame captured at 180° — fold-180 agreement.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Bear Claw", TargetSource.Both, dir: "Sh2-200 - Bear Claw", rotation: 0.0)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 470, 300.0, rotationFold: 179.98)]),
            Report()));

        // 179.98 folds to within tolerance of 0 across the wrap: one paired cell, no disagreement.
        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal(1, c.PlanCount);
        Assert.Equal(470, c.Disk);
        Assert.False(c.FramingDisagrees);
    }

    [Fact]
    public void Framing_PlanWithoutRotation_PairsOnRemainingKeys()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 81", TargetSource.Both, dir: "M 81")],   // rotation: null
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 4, 300.0, rotationFold: 65.0)]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal(1, c.PlanCount);
        Assert.Equal(4, c.Disk);
        Assert.False(c.FramingDisagrees);                    // no rotation on the plan → nothing to disagree with
    }

    [Fact]
    public void Framing_MechanicalOnlyCluster_NeverFailsPairingOnRotation()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Leo Triplet", TargetSource.Both, dir: "Leo Triplet", rotation: 110.0)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 265, 300.0,
                    rotation: RotationExpression.Mechanical, rotationFold: 172.3)]),
            Report()));

        // Mechanical rotation cannot be compared to the plan's — it never prevents pairing and never
        // reads as a disagreement.
        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.Equal(1, c.PlanCount);
        Assert.Equal(265, c.Disk);
        Assert.Equal(RotationExpression.Mechanical, c.DiskRotation);
        Assert.False(c.FramingDisagrees);
    }

    [Fact]
    public void Framing_ReframedPlan_NoClusterServesIt_PlanStandsAlone()
    {
        // The Jellyfish shape: plan re-framed to a rotation neither captured framing matches — the plan
        // gets its own cell and BOTH disk clusters read as disagreeing.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Jellyfish", TargetSource.Both, dir: "IC 443 - Jellyfish", rotation: 345.43)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 133, 300.0, framingOrdinal: 0, rotationFold: 15.0),
                    Inv(t, "H", FilterPurpose.Light, 124, 300.0, framingOrdinal: 1, rotationFold: 0.0),
                ]),
            Report()));

        Assert.Equal(3, tc.Cells.Count);
        ReconciliationCell planOnly = Assert.Single(tc.Cells, c => c.PlanCount == 1);
        Assert.Equal(0, planOnly.Disk);
        Assert.Null(planOnly.DiskRotation);
        Assert.All(tc.Cells.Where(c => c.PlanCount == 0), c => Assert.True(c.FramingDisagrees));
    }

    [Fact]
    public void Framing_SameFoldDifferentCenters_StayTwoCells()
    {
        // The M97 shape: two clusters share a fold angle and differ only by field center — the ordinal
        // keys them apart, and the plan pairs with exactly one.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 97", TargetSource.Both, dir: "M97 - Owl", rotation: 125.0)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 211, 300.0, framingOrdinal: 0, rotationFold: 125.0),
                    Inv(t, "H", FilterPurpose.Light, 1, 300.0, framingOrdinal: 1, rotationFold: 125.0),
                ]),
            Report()));

        Assert.Equal(2, tc.Cells.Count);
        // Equal fold deltas — the larger cluster wins the tie for the plan.
        ReconciliationCell paired = Assert.Single(tc.Cells, c => c.PlanCount == 1);
        Assert.Equal(211, paired.Disk);
        ReconciliationCell stray = Assert.Single(tc.Cells, c => c.PlanCount == 0);
        Assert.Equal(1, stray.Disk);
        // The stray's rotation AGREES with the plan (it is a translation stray) — no framing disagreement;
        // its separation alone tells the story.
        Assert.False(stray.FramingDisagrees);
    }

    // ---- overlap fraction (openspec framing-overlap-column → framing-keys) ---
    //
    // Field sizes are the real rigs measured over the library: Z183 1.423° × 0.951° (3:2) and
    // Z533 1.220° × 1.220° (square), both at f=531.

    private const double Z183W = 1.423, Z183H = 0.951;
    private const double Z533Side = 1.220;
    private const double TargetRa = 19.5, TargetDec = 10.0;   // hours, degrees

    [Fact]
    public void Overlap_RotatedStray_IsPriced_WhileTheServingFramingPricesNothing()
    {
        // Barnard 202 again, now with geometry: the 60° stray sits on the target's coordinates, so its
        // shortfall is rotation alone — a 10° turn of a 3:2 field about its own centre.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Barnard 202", TargetSource.Both, dir: "Barnard 202", rotation: 50.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 100, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 28, 300.0, framingOrdinal: 1, rotationFold: 50.0,
                        centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                        fieldWidthDeg: Z183W, fieldHeightDeg: Z183H),
                    Inv(t, "H", FilterPurpose.Light, 451, 300.0, framingOrdinal: 0, rotationFold: 60.0,
                        centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                        fieldWidthDeg: Z183W, fieldHeightDeg: Z183H),
                ]),
            Report()));

        ReconciliationCell serving = Assert.Single(tc.Cells, c => c.PlanCount == 1);
        Assert.Null(serving.FramingOverlapFraction);         // on the plan's framing — nothing to price

        ReconciliationCell stray = Assert.Single(tc.Cells, c => c.PlanCount == 0);
        Assert.True(stray.FramingDisagrees);
        Assert.Equal(0.919, stray.FramingOverlapFraction!.Value, 3);
    }

    [Fact]
    public void Overlap_AJustOverToleranceStray_IsPricedEvenThoughItsFootprintIsNearlyFull()
    {
        // 5.5° past the plan — badged, but 95.2% of its footprint still lands where the plan asked, above
        // the on-footprint threshold. It reports anyway: the threshold only silences framings that SERVE the
        // plan, so a badge can never point at a row with nothing to read.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Sh2-174", TargetSource.Both, dir: "Sh2-174", rotation: 50.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 28, 300.0, rotationFold: 55.5,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H)]),
            Report()));

        ReconciliationCell stray = Assert.Single(tc.Cells, c => c.Disk > 0);
        Assert.True(stray.FramingDisagrees);
        Assert.Equal(0.952, stray.FramingOverlapFraction!.Value, 3);
        Assert.True(stray.FramingOverlapFraction!.Value > FramingCluster.OnFootprintFraction);
    }

    [Fact]
    public void Overlap_TranslatedFramingAtThePlansOwnAngle_IsStillPriced()
    {
        // Right angle, wrong pointing: 0.15° of declination off a 0.951°-tall field. It SERVES the plan
        // (rotation is the serve rule) and still prices, because the frames are not where the plan asked.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Markarian's Chain", TargetSource.Both, dir: "Markarian's Chain", rotation: 0.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 71, 300.0, rotationFold: 0.0,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec + 0.15,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H)]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells);
        Assert.False(c.FramingDisagrees);                    // no badge — the rotation is right
        Assert.Equal(0.842, c.FramingOverlapFraction!.Value, 3);
    }

    [Fact]
    public void Overlap_ServingFramingOnTheFootprint_PricesNothing()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "M 33", TargetSource.Both, dir: "M 33", rotation: 110.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 430, 300.0, rotationFold: 110.0,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H)]),
            Report()));

        // A full overlap is not reported as 1.0: an ordinary on-plan row has nothing to say, and restating
        // 100% on every row would bury the rows that do.
        Assert.Null(Assert.Single(tc.Cells).FramingOverlapFraction);
    }

    [Theory]
    [InlineData(RotationExpression.Mechanical, 172.3, 110.0)]   // mechanical is never placed against a sky angle
    [InlineData(RotationExpression.Unknown, null, 110.0)]       // no rotation recorded at all
    [InlineData(RotationExpression.Sky, 65.0, null)]            // the plan asks for no rotation
    public void Overlap_AnIncomparableRotation_PricesNothing(
        RotationExpression expression, double? fold, double? planRotation)
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Leo Triplet", TargetSource.Both, dir: "Leo Triplet", rotation: planRotation,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                // Displaced far enough that a fabricated orientation WOULD have produced a number.
                [Inv(t, "H", FilterPurpose.Light, 265, 300.0, rotation: expression, rotationFold: fold,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec + 0.30,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H)]),
            Report()));

        Assert.Null(Assert.Single(tc.Cells).FramingOverlapFraction);
    }

    [Fact]
    public void Overlap_MissingGeometry_ReadsAsAbsentNotZero()
    {
        // The frames disagree, but carry no footprint — the honest answer is "cannot say", not "no overlap".
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Tulip", TargetSource.Both, dir: "Tulip", rotation: 160.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 199, 300.0, rotationFold: 20.0,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec)]),   // no field size
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells, x => x.PlanCount == 0);
        Assert.True(c.FramingDisagrees);                     // still badged…
        Assert.Null(c.FramingOverlapFraction);               // …but unpriced, not priced at zero
    }

    [Fact]
    public void Overlap_TargetWhoseEveryFramingStrays_PricesAllOfThem()
    {
        // The Jellyfish shape: the comparand is the PLAN, so a target with no serving framing left still
        // prices every one of its strays — the reason the plan is the comparand and not a sibling cluster.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "Jellyfish", TargetSource.Both, dir: "IC 443 - Jellyfish", rotation: 345.43,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [
                    Inv(t, "H", FilterPurpose.Light, 133, 300.0, framingOrdinal: 0, rotationFold: 15.0,
                        centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                        fieldWidthDeg: Z183W, fieldHeightDeg: Z183H),
                    Inv(t, "H", FilterPurpose.Light, 124, 300.0, framingOrdinal: 1, rotationFold: 0.0,
                        centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                        fieldWidthDeg: Z183W, fieldHeightDeg: Z183H),
                ]),
            Report()));

        List<ReconciliationCell> disk = [.. tc.Cells.Where(c => c.Disk > 0)];
        Assert.Equal(2, disk.Count);
        Assert.All(disk, c => Assert.NotNull(c.FramingOverlapFraction));
        // The plan's own cell has no frames to price.
        Assert.Null(Assert.Single(tc.Cells, c => c.PlanCount == 1).FramingOverlapFraction);
    }

    [Fact]
    public void Overlap_IsMeasuredAgainstAFramingsOwnSensor_NotItsNeighbours()
    {
        // The M81 shape: a square-sensor stray beside 3:2 ones. Each framing is measured against a plan
        // rectangle of ITS OWN size, so a target's camera history cannot move any of their numbers — the
        // property the rejected serving-cluster comparand would have broken.
        static double? Z533StrayFraction(bool alongsideZ183Framings)
        {
            Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
            List<InventoryFilter> inv =
            [
                Inv(t, "H", FilterPurpose.Light, 30, 300.0, framingOrdinal: 0, rotationFold: 0.0,
                    camera: "Z533", centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z533Side, fieldHeightDeg: Z533Side),
            ];
            if (alongsideZ183Framings)
            {
                inv.Add(Inv(t, "H", FilterPurpose.Light, 314, 300.0, framingOrdinal: 1, rotationFold: 65.1,
                    camera: "Z183", centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H));
                inv.Add(Inv(t, "H", FilterPurpose.Light, 215, 300.0, framingOrdinal: 2, rotationFold: 114.9,
                    camera: "Z183", centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H));
            }
            TargetCells tc = Assert.Single(ReconciliationProjection.Project(
                Graph([T(t, "M 81", TargetSource.Both, dir: "M 81", rotation: 65.11,
                        raHours: TargetRa, decDeg: TargetDec)],
                    [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")], inv),
                Report()));
            return Assert.Single(tc.Cells, c => c.Camera == "Z533").FramingOverlapFraction;
        }

        Assert.Equal(Z533StrayFraction(false)!.Value, Z533StrayFraction(true)!.Value, 12);
    }

    [Fact]
    public void Overlap_MixedSensorFraming_IsMarkedOnTheCell()
    {
        // IC 405's shape: one framing holding both sensors. The footprint is the dominant sensor's (the
        // scanner's choice) and the cell says so, so a consumer can qualify the number it shows.
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();
        TargetCells tc = Assert.Single(ReconciliationProjection.Project(
            Graph([T(t, "IC 405", TargetSource.Both, dir: "IC 405 - Flaming Star", rotation: 20.0,
                    raHours: TargetRa, decDeg: TargetDec)],
                [Plan(t, tpl, desired: 10, seconds: 300.0)], [Tpl(tpl, "H", "H")],
                [Inv(t, "H", FilterPurpose.Light, 196, 300.0, rotationFold: 99.0,
                    centroidRaHours: TargetRa, centroidDecDeg: TargetDec,
                    fieldWidthDeg: Z183W, fieldHeightDeg: Z183H, spansSensors: true)]),
            Report()));

        ReconciliationCell c = Assert.Single(tc.Cells, x => x.Disk > 0);
        Assert.True(c.FramingSpansMultipleSensors);
        Assert.NotNull(c.FramingOverlapFraction);
    }

    [Fact]
    public void Overlap_SingleSensorFraming_CarriesNoMarking() =>
        Assert.False(Assert.Single(ReconciliationProjection.Project(
            Graph([T(Guid.Empty, "M 33", TargetSource.Actual, dir: "M 33")], [], [],
                [Inv(Guid.Empty, "H", FilterPurpose.Light, 10, 300.0)]),
            Report())).Cells.Single().FramingSpansMultipleSensors);

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
        new(0, 0, 0, 0, 0, nameMismatches ?? [], [], [], unanchored ?? [], []);

    private static Target T(
        Guid id, string name, TargetSource source, string? dir = null, Guid? parent = null, Guid? projectId = null,
        bool enabled = true, string? tsKey = null, double? rotation = null,
        double? raHours = null, double? decDeg = null) =>
        new(id, source, projectId, name, Enabled: enabled, RaHours: raHours, DecDegreesSigned: decDeg, Epoch.J2000,
            RotationDeg: rotation, RoiPercent: null, Priority: null, DirectoryName: dir, Catalog: null,
            CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: tsKey,
            ParentTargetId: parent);

    private static Project Proj(Guid id, string name) =>
        new(id, Guid.NewGuid(), name, Description: null, ProjectState.Active, ProjectPriority.Normal,
            MinimumAltitudeDeg: null, MaximumAltitudeDeg: null, MinimumTimeMinutes: null, UseCustomHorizon: false,
            HorizonOffsetDeg: null, MeridianWindowMinutes: null, IsMosaic: false, EnableGrader: false,
            CreatedAt: 0, ActiveAt: null, InactiveAt: null, ImportedFromTsGuid: null);

    // Gain/offset/binning default to the same configuration Inv() writes, so a plan and a disk aggregate pair
    // unless a test deliberately varies one — the capture configuration is part of the cell key.
    private static ExposureTemplate Tpl(
        Guid id, string name, string filter, double? defaultSeconds = 300.0,
        int? gain = 100, int? offset = 50, int? bin = 1) =>
        new(id, Guid.NewGuid(), name, filter, Gain: gain, OffsetAdu: offset, Binning: bin, ReadoutMode: null,
            DefaultExposureSeconds: defaultSeconds, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(
        Guid target, Guid template, int desired, double? seconds, int acquired = 0, int accepted = 0) =>
        new(Guid.NewGuid(), target, template, seconds, desired, acquired, accepted,
            Enabled: true, ImportedFromTsGuid: null);

    // The framing centroid/footprint default to absent, as they are for a frame that carries no coordinates
    // or no optics — tests that price an overlap opt in by passing them, so every other test keeps proving
    // that a missing geometry reads as absent rather than as a fabricated number.
    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds,
        int gain = 100, int offset = 50, int bin = 1, string camera = "Z533",
        int framingOrdinal = 0, RotationExpression rotation = RotationExpression.Sky,
        double? rotationFold = 20.0,
        double? centroidRaHours = null, double? centroidDecDeg = null,
        double? fieldWidthDeg = null, double? fieldHeightDeg = null, bool spansSensors = false) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: gain, TypicalOffset: offset, TypicalSetTempC: -10.0, TypicalBinningX: bin,
            TypicalBinningY: bin, ExposureSeconds: seconds, Camera: camera,
            FramingOrdinal: framingOrdinal, RotationExpression: rotation, RotationFoldDeg: rotationFold,
            FramingCentroidRaHours: centroidRaHours, FramingCentroidDecDeg: centroidDecDeg,
            FramingFieldWidthDeg: fieldWidthDeg, FramingFieldHeightDeg: fieldHeightDeg,
            FramingSpansMultipleSensors: spansSensors);
}
