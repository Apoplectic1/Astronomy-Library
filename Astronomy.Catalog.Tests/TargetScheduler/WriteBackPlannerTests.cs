using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class WriteBackPlannerTests
{
    [Fact]
    public void SinglePlan_WritesDiskCount()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 500)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 47)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(500, w.TsExposurePlanId);
        Assert.Equal("H", w.Filter);
        Assert.Equal(FilterPurpose.Light, w.Purpose);
        Assert.Equal(47, w.DiskCount);
        Assert.Empty(plan.Manual);
    }

    [Fact]
    public void MainAndStars_RouteByPurpose()
    {
        Guid t = Guid.NewGuid(), main = Guid.NewGuid(), stars = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, main, tsId: 1), Plan(t, stars, tsId: 2)],
            [Tpl(main, "H", "H"), Tpl(stars, "Stars H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 47), Inv(t, "H", FilterPurpose.Stars, 12)],
            Report());

        Assert.Empty(plan.Manual);
        Assert.Equal(2, plan.Writes.Count);
        PlannedWrite light = plan.Writes.Single(w => w.Purpose == FilterPurpose.Light);
        PlannedWrite starsW = plan.Writes.Single(w => w.Purpose == FilterPurpose.Stars);
        Assert.Equal(1, light.TsExposurePlanId);
        Assert.Equal(47, light.DiskCount);
        Assert.Equal(2, starsW.TsExposurePlanId);
        Assert.Equal(12, starsW.DiskCount);   // routed per purpose — never the 59 sum
    }

    [Fact]
    public void ExposureSplitInventoryRows_RouteBySeconds()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        // The plan's duration is its spec: a 300s plan receives only the 300s frames; the 120s bucket has
        // no plan and surfaces as an informational note (write-back never creates plans).
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 500)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 28, seconds: 120.0),
             Inv(t, "H", FilterPurpose.Light, 47, seconds: 300.0)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(47, w.DiskCount);
        Assert.Equal(300, w.PlanSeconds);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal(ReconcileNote.UnplannedFramesKind, n.Kind);
        Assert.Contains("28 frames @120s", n.Detail);
    }

    [Fact]
    public void SamePurposeMultiPlan_IsManual()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, a, tsId: 1, desired: 50, acquired: 20, accepted: 18), Plan(t, b, tsId: 2, desired: 30)],
            [Tpl(a, "H", "H"), Tpl(b, "H fast", "H")],   // both Light-purpose, same filter — disk can't split
            [Inv(t, "H", FilterPurpose.Light, 30)],
            Report());

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.MultiPlan, g.Reason);
        Assert.Equal("H", g.Filter);
        Assert.Equal(FilterPurpose.Light, g.Purpose);
        Assert.Equal(300, g.Seconds);   // both plans at the template-default duration: same key → manual
        Assert.Equal(30, g.DiskCount);
        Assert.Equal(2, g.Plans.Count);
        Assert.Contains(g.Plans, p =>
            p.TsExposurePlanId == 1 && p.CatalogAcquired == 20 && p.CatalogAccepted == 18 && p.Desired == 50);
    }

    [Fact]
    public void DuplicateFold_IsManualWithDupReason()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M27", dir: "M27")],
            [Plan(t, a, tsId: 1), Plan(t, b, tsId: 2)],   // two TS targets' plans folded onto one disk target
            [Tpl(a, "H", "H"), Tpl(b, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 100)],
            Report(dups: [new DuplicateTsTarget("M27", ["M27", "Dumbbell"])]));

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.DuplicateFold, g.Reason);
        Assert.Contains(plan.NeedsReconciliation, n => n.Kind == "Duplicate");
    }

    [Fact]
    public void AliasFold_WritesDiskCountToEveryMember()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M27 - Dumbell", dir: "M27 - Dumbell")],
            [Plan(t, a, tsId: 1, desired: 60), Plan(t, b, tsId: 2, desired: 60)],   // one plan per alias member
            [Tpl(a, "H", "H"), Tpl(b, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 140)],
            Report(aliases: [new AliasTsTarget("M27 - Dumbell", ["M27", "Dumbell"])]));

        Assert.Empty(plan.Manual);
        Assert.Equal(2, plan.Writes.Count);                  // same object → disk truth to both TS plans
        Assert.All(plan.Writes, w => Assert.Equal(140, w.DiskCount));
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 1);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 2);
    }

    [Fact]
    public void AliasFold_UnexpectedPlanCount_IsManual()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M27 - Dumbell", dir: "M27 - Dumbell")],
            // Three plans on one cell of a two-member alias: one member has a genuine same-purpose multi-plan.
            [Plan(t, a, tsId: 1), Plan(t, b, tsId: 2), Plan(t, c, tsId: 3)],
            [Tpl(a, "H", "H"), Tpl(b, "H", "H"), Tpl(c, "H fast", "H")],
            [Inv(t, "H", FilterPurpose.Light, 140)],
            Report(aliases: [new AliasTsTarget("M27 - Dumbell", ["M27", "Dumbell"])]));

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.MultiPlan, g.Reason);
        Assert.Equal(3, g.Plans.Count);
    }

    [Fact]
    public void DiskWins_OverwriteDown()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 7, desired: 100, acquired: 200, accepted: 200)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 10)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(10, w.DiskCount);   // disk wins; no clamp to desired, no never-decrease
    }

    [Fact]
    public void PlannedOnlyTarget_IsIgnored()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Planned(t, "Future")],
            [Plan(t, tpl, tsId: 1, desired: 50)],
            [Tpl(tpl, "H", "H")],
            [],                              // not yet on disk
            Report(plannedOnly: 1));

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Manual);
        Assert.Equal(1, plan.IgnoredMissing);
    }

    [Fact]
    public void ReportIssues_FlattenToNotes()
    {
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [], [], [], [],
            Report(
                mismatches: [new NameMismatch("g", "CygnusLoop P3", "NGC6995", "NGC 6995", 0.12)],
                unanchored: [new UnanchoredTsTarget("g2", "NoCoords")]));

        Assert.Equal(2, plan.NeedsReconciliation.Count);
        Assert.Contains(plan.NeedsReconciliation, n => n.Kind == "NameMismatch" && n.TargetName == "CygnusLoop P3");
        Assert.Contains(plan.NeedsReconciliation, n => n.Kind == "Unanchored" && n.TargetName == "NoCoords");
    }

    [Fact]
    public void NameMismatchTarget_HeldForManual_NotWritten()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        // CygnusLoop P3 (TS) coord-matched onto disk "NGC 6995 - Eastern Veil" but that disk target has 0 S frames
        // — a likely false-positive match. Auto-writing would zero the real TS counts; hold it for manual instead.
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "NGC 6995", dir: "NGC 6995 - Eastern Veil")],
            [Plan(t, tpl, tsId: 9, desired: 32, acquired: 23, accepted: 23)],
            [Tpl(tpl, "S", "S")],
            [],   // disk has 0 S frames under this (mis-anchored) target
            Report(mismatches: [new NameMismatch("g", "CygnusLoop P3", "NGC 6995 - Eastern Veil", "NGC 6995", 0.309)]));

        Assert.Empty(plan.Writes);                 // NOT auto-written (would have zeroed it)
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.IdentityConflict, g.Reason);
        Assert.Equal("S", g.Filter);
        Assert.Contains(plan.NeedsReconciliation, n => n.Kind == "NameMismatch");
    }

    [Fact]
    public void AmbiguousTarget_HeldForManual_NotWritten()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "Veil", dir: "Veil A")],
            [Plan(t, tpl, tsId: 3)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 50)],
            Report(ambiguous: [new AmbiguousMatch("g", "Veil", ["Veil A", "Veil B"], 0.40)]));

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.IdentityConflict, g.Reason);
    }

    [Fact]
    public void MosaicTarget_AlwaysManual_WithMosaicReason()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        // A single-plan cell would normally auto-write; a mosaic target never does — panels resolve in TS.
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "Cygnus Loop", dir: "Mosaic - Cygnus Loop")],
            [Plan(t, tpl, tsId: 1)],
            [Tpl(tpl, "H", "H")],
            [Inv(t, "H", FilterPurpose.Light, 100)],
            Report());

        Assert.Empty(plan.Writes);
        ManualGroup g = Assert.Single(plan.Manual);
        Assert.Equal(ManualReason.Mosaic, g.Reason);
    }

    [Fact]
    public void PlanWithNoDiskBucket_WritesZero()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        // The plan's 600s spec has no frames at all — its counts go to 0 (a flagged decrease at apply time);
        // the 300s frames are an unplanned bucket, reported but never written.
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "Medusa")],
            [Plan(t, tpl, tsId: 1005, desired: 34, acquired: 34, accepted: 34)],
            [Tpl(tpl, "H900", "H", defExp: 600.0)],
            [Inv(t, "H", FilterPurpose.Light, 34, seconds: 300.0)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(0, w.DiskCount);
        Assert.Equal(600, w.PlanSeconds);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal(ReconcileNote.UnplannedFramesKind, n.Kind);
        Assert.Contains("34 frames @300s", n.Detail);
    }

    [Fact]
    public void DifferentSecondsMultiPlans_AutoResolveIntoSeparateWrites()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();

        // Two same-purpose plans at different durations are different cells, not a multi-plan conflict —
        // each receives its own bucket's count.
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M17")],
            [Plan(t, a, tsId: 299, desired: 64), Plan(t, b, tsId: 1040, desired: 1)],
            [Tpl(a, "H", "H", defExp: 300.0), Tpl(b, "H short", "H", defExp: 600.0)],
            [Inv(t, "H", FilterPurpose.Light, 47, seconds: 300.0),
             Inv(t, "H", FilterPurpose.Light, 12, seconds: 600.0)],
            Report());

        Assert.Empty(plan.Manual);
        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 299 && w.DiskCount == 47 && w.PlanSeconds == 300);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 1040 && w.DiskCount == 12 && w.PlanSeconds == 600);
        Assert.Empty(plan.NeedsReconciliation);
    }

    [Fact]
    public void PlanLevelSeconds_OverridesTemplateDefault()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M1")],
            [Plan(t, tpl, tsId: 5, seconds: 600.0)],   // plan-level value wins over the 300s template default
            [Tpl(tpl, "H", "H", defExp: 300.0)],
            [Inv(t, "H", FilterPurpose.Light, 21, seconds: 600.0)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(21, w.DiskCount);
        Assert.Equal(600, w.PlanSeconds);
    }

    [Fact]
    public void UnmatchedDiskBucket_EmitsUnplannedNote_NotManual()
    {
        Guid t = Guid.NewGuid(), tpl = Guid.NewGuid();

        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "Medusa")],
            [Plan(t, tpl, tsId: 1008, desired: 35)],
            [Tpl(tpl, "Stars R", "R", defExp: 60.0)],
            [Inv(t, "R", FilterPurpose.Stars, 40, seconds: 60.0),
             Inv(t, "R", FilterPurpose.Stars, 35, seconds: 5.0)],
            Report());

        PlannedWrite w = Assert.Single(plan.Writes);
        Assert.Equal(40, w.DiskCount);
        Assert.Empty(plan.Manual);
        ReconcileNote n = Assert.Single(plan.NeedsReconciliation);
        Assert.Equal(ReconcileNote.UnplannedFramesKind, n.Kind);
        Assert.Contains("35 frames @5s", n.Detail);
    }

    [Fact]
    public void AliasFold_DifferentSeconds_EachMemberWritesItsOwnBucket()
    {
        Guid t = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();

        // Alias members whose plans differ in duration land in separate single-plan groups and auto-write
        // individually — the member-count fold only applies within one duration.
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Both(t, "M27 - Dumbell", dir: "M27 - Dumbell")],
            [Plan(t, a, tsId: 1), Plan(t, b, tsId: 2)],
            [Tpl(a, "H", "H", defExp: 300.0), Tpl(b, "H", "H", defExp: 600.0)],
            [Inv(t, "H", FilterPurpose.Light, 100, seconds: 300.0),
             Inv(t, "H", FilterPurpose.Light, 20, seconds: 600.0)],
            Report(aliases: [new AliasTsTarget("M27 - Dumbell", ["M27", "Dumbell"])]));

        Assert.Empty(plan.Manual);
        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 1 && w.DiskCount == 100);
        Assert.Contains(plan.Writes, w => w.TsExposurePlanId == 2 && w.DiskCount == 20);
    }

    [Fact]
    public void ActualOnlyInventory_NoUnplannedNote()
    {
        Guid t = Guid.NewGuid();

        // One-sided targets stay in IgnoredMissing — their buckets are not "unplanned frames".
        WriteBackPlan plan = WriteBackPlanner.Plan(
            [Actual(t, "Shot Only")],
            [],
            [],
            [Inv(t, "H", FilterPurpose.Light, 50, seconds: 300.0)],
            Report(actualOnly: 1));

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Manual);
        Assert.Empty(plan.NeedsReconciliation);
        Assert.Equal(1, plan.IgnoredMissing);
    }

    // ---- builders -----------------------------------------------------------

    private static Target Actual(Guid id, string name) => new(
        id, TargetSource.Actual, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: name, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: 0, CreatedAt: 0, ImportedFromTsGuid: null);

    private static Target Both(Guid id, string name, string? dir = null) => new(
        id, TargetSource.Both, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: dir ?? name, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: 0, CreatedAt: 0, ImportedFromTsGuid: null);

    private static Target Planned(Guid id, string name) => new(
        id, TargetSource.Planned, ProjectId: null, name, Enabled: true, RaHours: null, DecDegreesSigned: null,
        Epoch.J2000, RotationDeg: null, RoiPercent: null, Priority: null, DirectoryName: null, Catalog: null,
        CommonName: null, ObjectName: null, ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: "guid");

    private static ExposureTemplate Tpl(Guid id, string name, string filter, double? defExp = 300.0) =>
        new(id, Guid.NewGuid(), name, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: defExp, ImportedFromTsGuid: null);

    private static ExposurePlan Plan(
        Guid target, Guid template, long tsId, int desired = 0, int acquired = 0, int accepted = 0,
        double? seconds = null) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: seconds, desired, acquired, accepted,
            Enabled: true, ImportedFromTsGuid: tsId.ToString(CultureInfo.InvariantCulture));

    private static InventoryFilter Inv(
        Guid target, string filter, FilterPurpose purpose, int count, double seconds = 300.0) =>
        new(target, filter, purpose, filter, count, count * seconds, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: 100, TypicalOffset: 50, TypicalSetTempC: -10.0, TypicalBinningX: 1, TypicalBinningY: 1,
            ExposureSeconds: seconds, Cameras: "Z533");

    private static CatalogBuildReport Report(
        int plannedOnly = 0,
        int actualOnly = 0,
        IReadOnlyList<DuplicateTsTarget>? dups = null,
        IReadOnlyList<AliasTsTarget>? aliases = null,
        IReadOnlyList<NameMismatch>? mismatches = null,
        IReadOnlyList<AmbiguousMatch>? ambiguous = null,
        IReadOnlyList<UnanchoredTsTarget>? unanchored = null) => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: plannedOnly, ActualOnlyCount: actualOnly,
        NameMismatches: mismatches ?? [], AmbiguousMatches: ambiguous ?? [], DuplicateTsTargets: dups ?? [],
        AliasTsTargets: aliases ?? [], UnanchoredTsTargets: unanchored ?? [], InvalidTsTargets: []);
}
