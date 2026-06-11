using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class TargetResolverTests
{
    private const long Now = 1_700_000_000;

    [Fact]
    public void Resolve_CoordinateMatch_ProducesBoth_WithProvenanceAndInventory()
    {
        TargetReport[] disk = [Disk("M42 - Orion", "M42", "Orion", 5.590, -5.39, withFilter: true)];
        TsPlanData ts = Plan(
            targets: [TsT(1, "M42", 5.591, -5.39, project: 10, guid: "g-m42")],
            plans: [TsP(100, target: 1, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target t = Assert.Single(g.Targets);
        Assert.Equal(TargetSource.Both, t.Source);
        Assert.Equal("M42 - Orion", t.DirectoryName);
        Assert.Equal(5.590, t.RaHours!.Value); // disk (plate-solved) coords win
        Assert.Equal("g-m42", t.ImportedFromTsGuid);
        Assert.NotNull(t.ProjectId);
        Assert.Single(g.InventoryFilters);
        ExposurePlan p = Assert.Single(g.Plans);
        Assert.Equal(t.Id, p.TargetId);
        Assert.Equal(1, r.BothCount);
        Assert.Equal(0, r.PlannedOnlyCount);
        Assert.Equal(0, r.ActualOnlyCount);
        Assert.Empty(r.NameMismatches);
    }

    [Fact]
    public void Resolve_NoDiskMatch_ProducesPlannedOnly()
    {
        TsPlanData ts = Plan(
            targets: [TsT(1, "NGC 7000", 20.97, 44.5, project: 10, guid: "g-na")],
            plans: [TsP(100, target: 1, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve([], ts, Now);

        Target t = Assert.Single(g.Targets);
        Assert.Equal(TargetSource.Planned, t.Source);
        Assert.Null(t.DirectoryName);
        Assert.Equal(20.97, t.RaHours!.Value);
        Assert.Empty(g.InventoryFilters);
        Assert.Single(g.Plans);
        Assert.Equal(1, r.PlannedOnlyCount);
        Assert.Equal(0, r.BothCount);
    }

    [Fact]
    public void Resolve_DiskWithoutTs_ProducesActualOnly()
    {
        TargetReport[] disk = [Disk("M51 - Whirlpool", "M51", "Whirlpool", 13.50, 47.2, withFilter: true)];

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, TsPlanData.Empty, Now);

        Target t = Assert.Single(g.Targets);
        Assert.Equal(TargetSource.Actual, t.Source);
        Assert.Null(t.ProjectId);
        Assert.Null(t.ImportedFromTsGuid);
        Assert.Single(g.InventoryFilters);
        Assert.Empty(g.Plans);
        Assert.Equal(1, r.ActualOnlyCount);
    }

    [Fact]
    public void Resolve_CoordinateMatchButNameDiffers_FlagsNameMismatch()
    {
        TargetReport[] disk = [Disk("M42 - Orion", "M42", "Orion", 5.59, -5.39, withFilter: false)];
        TsPlanData ts = Plan(targets: [TsT(1, "Sombrero", 5.59, -5.39, project: 10, guid: "g-x")]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Assert.Equal(TargetSource.Both, Assert.Single(g.Targets).Source);
        NameMismatch nm = Assert.Single(r.NameMismatches);
        Assert.Equal("Sombrero", nm.TsName);
        Assert.Equal("M42 - Orion", nm.DiskDirectory);
    }

    [Fact]
    public void Resolve_TwoTsTargetsOntoOneDisk_DedupesAndFlagsDuplicate()
    {
        TargetReport[] disk = [Disk("M42 - Orion", "M42", "Orion", 5.59, -5.39, withFilter: false)];
        TsPlanData ts = Plan(
            targets:
            [
                TsT(1, "M42", 5.591, -5.39, project: 10, guid: "g1"),
                TsT(2, "M42 core", 5.590, -5.40, project: 10, guid: "g2"),
            ],
            plans: [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target t = Assert.Single(g.Targets);    // both TS folded onto one canonical
        Assert.Equal(TargetSource.Both, t.Source);
        Assert.Equal(2, g.Plans.Count);         // both plans wired to it
        Assert.All(g.Plans, p => Assert.Equal(t.Id, p.TargetId));
        DuplicateTsTarget dup = Assert.Single(r.DuplicateTsTargets);
        Assert.Equal("M42 - Orion", dup.DiskDirectory);
        Assert.Equal(2, dup.TsTargetNames.Count);
        Assert.Empty(r.AliasTsTargets); // "M42 core" is a genuine variant, not an exact-facet alias
    }

    [Fact]
    public void Resolve_AliasTsTargets_ReportedAsAlias_NotDuplicate()
    {
        // Two TS targets whose names are exactly the two halves of the disk directory — same object, twice.
        TargetReport[] disk = [Disk("M27 - Dumbell", "M27", "Dumbell", 19.99, 22.72, withFilter: false)];
        TsPlanData ts = Plan(
            targets:
            [
                TsT(1, "M27", 19.991, 22.72, project: 10, guid: "g1"),
                TsT(2, "Dumbell", 19.99, 22.73, project: 10, guid: "g2"),
            ],
            plans: [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target t = Assert.Single(g.Targets);    // both fold onto the one canonical, like any dup
        Assert.Equal(TargetSource.Both, t.Source);
        Assert.Equal(2, g.Plans.Count);
        Assert.All(g.Plans, p => Assert.Equal(t.Id, p.TargetId));
        Assert.Empty(r.DuplicateTsTargets);     // ...but reported as an alias, not a duplicate
        AliasTsTarget alias = Assert.Single(r.AliasTsTargets);
        Assert.Equal("M27 - Dumbell", alias.DiskDirectory);
        Assert.Equal(2, alias.TsTargetNames.Count);
    }

    [Fact]
    public void Resolve_RespectsTolerance()
    {
        TargetReport[] disk = [Disk("M42 - Orion", "M42", "Orion", 5.59, -5.39, withFilter: false)];
        TsPlanData ts = Plan(targets: [TsT(1, "M42", 5.59, -4.79, project: 10, guid: "g1")]); // 0.6° away in dec

        // Default 0.5° tolerance: no match → disk stays Actual, TS becomes its own Planned target.
        CatalogGraph tight = TargetResolver.Resolve(disk, ts, Now).Graph;
        Assert.Equal(TargetSource.Actual, Assert.Single(tight.Targets, t => t.DirectoryName == "M42 - Orion").Source);

        // Widen to 1.0°: they merge into one Both target.
        CatalogGraph loose = TargetResolver.Resolve(disk, ts, Now, new ResolveOptions(1.0)).Graph;
        Assert.Equal(TargetSource.Both, Assert.Single(loose.Targets).Source);
    }

    [Fact]
    public void SeparationDegrees_OneDegreeInDeclination_IsOne()
    {
        Assert.Equal(1.0, TargetResolver.SeparationDegrees(5.0, 10.0, 5.0, 11.0), precision: 6);
    }

    [Fact]
    public void Resolve_PlannedTargetWithOutOfRangeValues_IsCoercedNotAborted_AndFlagged()
    {
        // RA 25h, Dec +95°, epoch code 7 would all violate the target CHECK/FK columns if passed through raw.
        TsPlanData ts = Plan(targets: [new TsTarget(1, "Bad Row", Active: 1, Ra: 25.0, Dec: 95.0, EpochCode: 7,
            Rotation: null, Roi: null, ProjectId: 10, Priority: 9, TsGuid: "g-bad")]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve([], ts, Now);

        Target t = Assert.Single(g.Targets);
        Assert.Equal(TargetSource.Planned, t.Source);
        Assert.Equal(1.0, t.RaHours!.Value, precision: 6);  // 25h wrapped into [0,24)
        Assert.Equal(90.0, t.DecDegreesSigned!.Value);       // clamped to +90
        Assert.Equal(Epoch.J2000, t.Epoch);                  // unknown code → default J2000
        Assert.Null(t.Priority);                             // unknown priority → inherit (NULL)
        InvalidTsTarget bad = Assert.Single(r.InvalidTsTargets);
        Assert.Equal("Bad Row", bad.TsName);
    }

    [Fact]
    public void Resolve_Mosaic_PanelsBecomeChildren_PlansRewireToChildren()
    {
        TargetReport[] disk = [DiskMosaic("Mosaic - Cygnus Loop",
            ("Panel 01of03", 20.5, 30.5), ("Panel 02of03", 20.7, 30.7), ("Panel 03of03", 20.9, 30.9))];
        TsPlanData ts = new(
            [new TsProject(20, "profile-1", "Mosaic - Cygnus Loop", 1, 1, null, 1, "g-mosaic")],  // isMosaic
            [TsT(1, "CygnusLoop P1", 20.5, 30.5, project: 20, guid: "g-p1"),
             TsT(2, "CygnusLoop P2", 20.7, 30.7, project: 20, guid: "g-p2"),
             TsT(3, "CygnusLoop P3", 20.9, 30.9, project: 20, guid: "g-p3")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000), TsP(102, target: 3, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Assert.Equal(4, g.Targets.Count);                    // one parent + one child per panel
        Target parent = Assert.Single(g.Targets, t => t.DirectoryName == "Mosaic - Cygnus Loop");
        Assert.Equal(TargetSource.Both, parent.Source);
        Assert.Null(parent.ImportedFromTsGuid);              // no single TS target (matched by project name)
        Assert.Null(parent.ParentTargetId);
        Assert.DoesNotContain(g.Plans, p => p.TargetId == parent.Id);            // parent carries no plans...
        Assert.DoesNotContain(g.InventoryFilters, i => i.TargetId == parent.Id); // ...and no inventory

        List<Target> children = [.. g.Targets.Where(t => t.ParentTargetId == parent.Id)];
        Assert.Equal(3, children.Count);
        Target p1 = Assert.Single(children, c => c.Name == "CygnusLoop P1");
        Assert.Equal(TargetSource.Both, p1.Source);
        Assert.Equal("Mosaic - Cygnus Loop/Panel 01of03", p1.DirectoryName);
        Assert.Equal("g-p1", p1.ImportedFromTsGuid);
        Assert.Equal(20.5, p1.RaHours!.Value, precision: 6);                  // disk panel centroid wins
        Assert.Equal(p1.Id, Assert.Single(g.Plans, p => p.ImportedFromTsGuid == "100").TargetId);
        Assert.Single(g.InventoryFilters, i => i.TargetId == p1.Id);

        List<Target> ordered = [.. g.Targets];
        Assert.True(ordered.IndexOf(parent) < children.Min(c => ordered.IndexOf(c)));  // FK order

        Assert.Empty(r.DuplicateTsTargets);                  // a mosaic is NOT a duplicate
        Assert.Empty(r.NameMismatches);                      // "CygnusLoop P1" aligns with token P1
        Assert.Equal(1, r.MosaicsResolved);
        Assert.Equal(3, r.PanelsMatched);
        Assert.Equal(0, r.PanelsPlannedOnly);
        Assert.Equal(1, r.BothCount);                        // top-level counts: the parent only
        Assert.Equal(0, r.PlannedOnlyCount);
    }

    [Fact]
    public void Resolve_Mosaic_PanelDoesNotMisAnchorToOverlappingStandalone()
    {
        TargetReport[] disk =
        [
            DiskMosaic("Mosaic - Cygnus Loop", ("Panel 01of03", 20.5, 30.5)),
            Disk("NGC 6995 - Eastern Veil", "NGC 6995", "Eastern Veil", 20.9, 30.9, withFilter: true),
        ];
        TsPlanData ts = new(
            [new TsProject(20, "profile-1", "Mosaic - Cygnus Loop", 1, 1, null, 1, "g-mosaic")],
            [TsT(3, "CygnusLoop P3", 20.9, 30.9, project: 20, guid: "g-p3")],   // sits exactly on NGC 6995's coords
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(102, target: 3, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target veil = Assert.Single(g.Targets, x => x.DirectoryName == "NGC 6995 - Eastern Veil");
        Assert.Equal(TargetSource.Actual, veil.Source);                 // panel did NOT anchor here
        // The TS panel is far from the only disk panel, so it becomes a PLANNED child of the mosaic
        // and its plan targets that child — never the overlapping standalone target.
        Target planned = Assert.Single(g.Targets, x => x.Name == "CygnusLoop P3");
        Assert.Equal(TargetSource.Planned, planned.Source);
        Assert.NotNull(planned.ParentTargetId);
        Assert.Equal(planned.Id, Assert.Single(g.Plans).TargetId);
        Assert.Empty(r.NameMismatches);                                 // no CygnusLoop P3 ↔ NGC 6995 mismatch
        Assert.Equal(1, r.PanelsActualOnly);                            // the unmatched disk panel
        Assert.Equal(1, r.PanelsPlannedOnly);
    }

    [Fact]
    public void Resolve_DiskOnlyMosaic_NoMatchingProject_IsActualOnly_WithActualChildren()
    {
        TargetReport[] disk = [DiskMosaic("Mosaic - Pinwheel", ("Panel 1of1", 14.0, 54.0))];
        TsPlanData ts = new(   // a mosaic project exists, but not named Pinwheel
            [new TsProject(20, "profile-1", "Mosaic - Cygnus Loop", 1, 1, null, 1, "g-mosaic")],
            [], [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)], []);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target parent = Assert.Single(g.Targets, t => t.ParentTargetId is null);
        Assert.Equal(TargetSource.Actual, parent.Source);
        Target child = Assert.Single(g.Targets, t => t.ParentTargetId == parent.Id);
        Assert.Equal(TargetSource.Actual, child.Source);
        Assert.Equal("Mosaic - Pinwheel/Panel 1of1", child.DirectoryName);
        Assert.Equal(child.Id, Assert.Single(g.InventoryFilters).TargetId);   // inventory on the child
        Assert.Equal(0, r.MosaicsResolved);
        Assert.Equal(1, r.ActualOnlyCount);
        Assert.Equal(1, r.PanelsActualOnly);
    }

    [Fact]
    public void Resolve_Mosaic_UnmatchedTsPanels_BecomePlannedChildren()
    {
        // The Witch Head shape: one panel shot of a four-panel plan.
        TargetReport[] disk = [DiskMosaic("Mosaic - Witch Head", ("Panel 1of4", 5.0, -7.0))];
        TsPlanData ts = new(
            [new TsProject(30, "profile-1", "Mosaic - Witch Head", 1, 1, null, 1, "g-wh")],
            [TsT(1, "WitchHead P1", 5.0, -7.0, project: 30, guid: "g-w1"),
             TsT(2, "WitchHead P2", 5.2, -7.0, project: 30, guid: "g-w2"),
             TsT(3, "WitchHead P3", 5.0, -9.5, project: 30, guid: "g-w3"),
             TsT(4, "WitchHead P4", 5.2, -9.5, project: 30, guid: "g-w4")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000),
             TsP(102, target: 3, template: 1000), TsP(103, target: 4, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Assert.Equal(5, g.Targets.Count);   // parent + 1 Both child + 3 Planned children
        Target parent = Assert.Single(g.Targets, t => t.ParentTargetId is null);
        Target both = Assert.Single(g.Targets, t => t.Source == TargetSource.Both && t.ParentTargetId is not null);
        Assert.Equal("WitchHead P1", both.Name);
        List<Target> planned = [.. g.Targets.Where(t => t.Source == TargetSource.Planned)];
        Assert.Equal(3, planned.Count);
        Assert.All(planned, p => Assert.Equal(parent.Id, p.ParentTargetId));
        Assert.Equal(1, r.PanelsMatched);
        Assert.Equal(3, r.PanelsPlannedOnly);
        Assert.Equal(0, r.PlannedOnlyCount);   // top-level counts exclude planned panel children
        // Every plan targets its own panel child — none on the parent.
        Assert.All(g.Plans, p => Assert.NotEqual(parent.Id, p.TargetId));
        Assert.Equal(4, g.Plans.Select(p => p.TargetId).Distinct().Count());
    }

    [Fact]
    public void Resolve_AlignedClaim_OutranksUnaligned_ReleasedClaimStaysPlanned()
    {
        // The real Witch Head shape: panels overlap so heavily that the UNSHOT P2 sits inside tolerance of
        // the shot panel. P1 corresponds to the directory by token and keeps it; P2 is merely close — it
        // releases back to planned instead of piling onto P1's panel as a false duplicate.
        TargetReport[] disk = [DiskMosaic("Mosaic - Tight", ("Panel 1of2", 20.5, 30.5))];
        TsPlanData ts = new(
            [new TsProject(40, "profile-1", "Mosaic - Tight", 1, 1, null, 1, "g-t")],
            [TsT(1, "Tight P1", 20.5, 30.5, project: 40, guid: "g-t1"),          // aligned (token P1), sep 0
             TsT(2, "Tight P2", 20.52, 30.5, project: 40, guid: "g-t2")],        // ~0.26° — close, unaligned
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target both = Assert.Single(g.Targets, t => t.Source == TargetSource.Both && t.ParentTargetId is not null);
        Assert.Equal("Tight P1", both.Name);
        Target planned = Assert.Single(g.Targets, t => t.Source == TargetSource.Planned);
        Assert.Equal("Tight P2", planned.Name);
        Assert.NotNull(planned.ParentTargetId);                                  // still a panel of the mosaic
        Assert.Equal(planned.Id, Assert.Single(g.Plans, p => p.ImportedFromTsGuid == "101").TargetId);
        Assert.Empty(r.DuplicateTsTargets);                                      // not a false duplicate
        Assert.Empty(r.NameMismatches);                                          // the released claim doesn't flag
        Assert.Equal(1, r.PanelsMatched);
        Assert.Equal(1, r.PanelsPlannedOnly);
    }

    [Fact]
    public void Resolve_AllClaimsUnaligned_NearestStands_NothingReleases()
    {
        // With no aligned claim at all, the rule never demotes: the nearest unaligned match stands and is
        // flagged (the Rosette "Panel Center" shape — coordinates succeed where the naming broke).
        TargetReport[] disk = [DiskMosaic("Mosaic - Rose", ("Panel Center", 6.5, 5.0))];
        TsPlanData ts = new(
            [new TsProject(50, "profile-1", "Mosaic - Rose", 1, 1, null, 1, "g-r")],
            [TsT(1, "Rose P4", 6.51, 5.0, project: 50, guid: "g-r4")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target both = Assert.Single(g.Targets, t => t.Source == TargetSource.Both && t.ParentTargetId is not null);
        Assert.Equal("Rose P4", both.Name);
        Assert.Single(r.NameMismatches);
        Assert.Empty(r.DuplicateTsTargets);
    }

    [Fact]
    public void Resolve_Mosaic_WrongNumberedPanel_FlagsNameMismatch()
    {
        // A misfiled panel directory (wrong number) still anchors by coordinates, but the token validation
        // ("P2" vs "...P1") flags it — the same name≠ report a standalone mismatch gets, which write-back
        // already holds for manual resolution.
        TargetReport[] disk = [DiskMosaic("Mosaic - Tight", ("Panel 2of2", 20.5, 30.5))];
        TsPlanData ts = new(
            [new TsProject(40, "profile-1", "Mosaic - Tight", 1, 1, null, 1, "g-t")],
            [TsT(1, "Tight P1", 20.5, 30.5, project: 40, guid: "g-t1")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        NameMismatch m = Assert.Single(r.NameMismatches);
        Assert.Equal("Tight P1", m.TsName);
        Assert.Equal("Mosaic - Tight/Panel 2of2", m.DiskDirectory);
    }

    [Theory]
    [InlineData("Panel 01of16", "P1")]
    [InlineData("Panel 16of16", "P16")]
    [InlineData("Panel 1of4", "P1")]
    [InlineData("Panel North", "North")]
    [InlineData("Oddball", "Oddball")]
    public void PanelToken_FollowsTheFilingConvention(string label, string expected) =>
        Assert.Equal(expected, TargetResolver.PanelToken(label));

    [Fact]
    public void Resolve_Mosaic_EmptyPanels_DegradesToParentInventory()
    {
        // Synthetic reports without per-panel detail keep today's shape: aggregate inventory on the parent,
        // the project's TS panels as planned children.
        TargetReport[] disk = [Disk("Mosaic - Cygnus Loop", "Mosaic", "Cygnus Loop", 20.7, 30.7, withFilter: true)];
        TsPlanData ts = new(
            [new TsProject(20, "profile-1", "Mosaic - Cygnus Loop", 1, 1, null, 1, "g-mosaic")],
            [TsT(1, "CygnusLoop P1", 20.5, 30.5, project: 20, guid: "g-p1")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000)]);

        (CatalogGraph g, CatalogBuildReport r) = TargetResolver.Resolve(disk, ts, Now);

        Target parent = Assert.Single(g.Targets, t => t.ParentTargetId is null);
        Assert.Equal(parent.Id, Assert.Single(g.InventoryFilters).TargetId);     // aggregate stays on parent
        Target planned = Assert.Single(g.Targets, t => t.ParentTargetId is not null);
        Assert.Equal(TargetSource.Planned, planned.Source);
        Assert.Equal(planned.Id, Assert.Single(g.Plans).TargetId);
        Assert.Equal(0, r.PanelsMatched);
        Assert.Equal(1, r.PanelsPlannedOnly);
    }

    // ---- synthetic builders -------------------------------------------------

    private static TargetReport Disk(string dir, string cat, string common, double raH, double dec, bool withFilter)
    {
        FilterAggregate[] filters = withFilter ? [SampleFilter()] : [];
        return new TargetReport(dir, cat, common, cat, raH, dec, filters);
    }

    private static TargetReport DiskMosaic(string dir, params (string Label, double RaH, double Dec)[] panels)
    {
        (string cat, string? common) = TargetReport.SplitDirectoryName(dir);
        TargetReport[] subs = [.. panels.Select(p =>
            new TargetReport(p.Label, p.Label, null, p.Label, p.RaH, p.Dec, new[] { SampleFilter() }))];
        double ra = panels.Length > 0 ? panels.Average(p => p.RaH) : 20.0;
        double dec = panels.Length > 0 ? panels.Average(p => p.Dec) : 30.0;
        return new TargetReport(dir, cat, common, cat, ra, dec, [SampleFilter()], subs);
    }

    private static FilterAggregate SampleFilter()
    {
        DateTime first = new(2024, 1, 1, 22, 0, 0, DateTimeKind.Utc);
        return new FilterAggregate("H", "H", FilterPurpose.Light, 12, TimeSpan.FromSeconds(3600), first,
            first.AddHours(2), new TypicalSettings(100, 50, -10.0, (1, 1), 300.0), ["Z533"]);
    }

    private static TsPlanData Plan(IReadOnlyList<TsTarget>? targets = null, IReadOnlyList<TsExposurePlan>? plans = null) => new(
        [new TsProject(10, "profile-1", "Proj", 1, 1, null, 0, "g-proj")],
        targets ?? [],
        [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
        plans ?? []);

    private static TsTarget TsT(long id, string name, double ra, double dec, long project, string guid) =>
        new(id, name, 1, ra, dec, 2, null, null, project, -1, guid);

    private static TsExposurePlan TsP(long id, long target, long template) =>
        new(id, "profile-1", 300.0, 60, 10, 8, target, template);
}
