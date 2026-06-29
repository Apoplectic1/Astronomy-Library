using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract test for the ordering guarantee on <see cref="CatalogGraph"/> — CONSUMERS.md
/// "Semantic assumptions" #5 (the FK / mosaic-nesting class). Synthetic builders mirror
/// Astronomy.Catalog.Tests' TargetResolverTests so the input shapes match real usage.
/// </summary>
public sealed class CatalogGraphOrderingContractTests
{
    private const long Now = 1_700_000_000;

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #5:
    //   "Build.CatalogGraph lists are FK-insert order; mosaic panels immediately
    //    after their parent (TSM nesting depends on it)."
    // The self-referencing target FK requires parents-before-children, and TSM renders
    // its target tree directly off Targets order: each mosaic parent must be immediately
    // followed by its panel children as one contiguous block, with the overall list in
    // disk-insert order. Re-ordering Targets would compile but mis-nest the TSM tree.
    // ---------------------------------------------------------------------------

    [Fact]
    public void CatalogGraph_Targets_AreInsertOrder_PanelsImmediatelyAfterParent()
    {
        // Disk: a standalone, then a 3-panel mosaic, then another standalone.
        TargetReport[] disk =
        [
            Disk("M51 - Whirlpool", "M51", "Whirlpool", 13.50, 47.2, withFilter: true),
            DiskMosaic("Mosaic - Cygnus Loop",
                ("Panel 01of03", 20.5, 30.5), ("Panel 02of03", 20.7, 30.7), ("Panel 03of03", 20.9, 30.9)),
            Disk("NGC 7000 - North America", "NGC 7000", "North America", 20.97, 44.5, withFilter: true),
        ];

        // A matching mosaic project + panel targets so the panels resolve as Both (they
        // remain disk units, so the contiguity guarantee must still hold).
        TsPlanData ts = new(
            [new TsProject(20, "profile-1", "Mosaic - Cygnus Loop", 1, 1, null, 1, "g-mosaic")],
            [TsT(1, "CygnusLoop P1", 20.5, 30.5, project: 20, guid: "g-p1"),
             TsT(2, "CygnusLoop P2", 20.7, 30.7, project: 20, guid: "g-p2"),
             TsT(3, "CygnusLoop P3", 20.9, 30.9, project: 20, guid: "g-p3")],
            [new TsExposureTemplate(1000, "profile-1", "Ha", "H", 100, 50, 1, 300.0)],
            [TsP(100, target: 1, template: 1000), TsP(101, target: 2, template: 1000), TsP(102, target: 3, template: 1000)]);

        (CatalogGraph g, _) = TargetResolver.Resolve(disk, ts, Now);

        List<Target> targets = [.. g.Targets];

        Target parent = Assert.Single(targets, t => t.DirectoryName == "Mosaic - Cygnus Loop");
        List<Target> children = [.. targets.Where(t => t.ParentTargetId == parent.Id)];
        Assert.Equal(3, children.Count);

        int parentIdx = targets.FindIndex(t => t.Id == parent.Id);

        // The panel children occupy the contiguous slots IMMEDIATELY after the parent.
        List<int> childIdxs = [.. children.Select(c => targets.FindIndex(t => t.Id == c.Id)).OrderBy(i => i)];
        List<int> expectedContiguous = [.. Enumerable.Range(parentIdx + 1, children.Count)];
        Assert.Equal(expectedContiguous, childIdxs);

        // Disk-insert order preserved across the whole list: the standalone filed before
        // the mosaic precedes the parent; the one filed after follows the last panel
        // (no panel bleeds past it).
        int whirlpoolIdx = targets.FindIndex(t => t.DirectoryName == "M51 - Whirlpool");
        int northAmIdx = targets.FindIndex(t => t.DirectoryName == "NGC 7000 - North America");
        Assert.True(whirlpoolIdx < parentIdx, "standalone filed before the mosaic must precede the parent");
        Assert.True(northAmIdx > childIdxs[^1], "standalone filed after the mosaic must follow the last panel");

        // FK invariant: the parent is a root; every panel child points back at it.
        Assert.Null(parent.ParentTargetId);
        Assert.All(children, c => Assert.Equal(parent.Id, c.ParentTargetId));
    }

    // ---- synthetic builders (mirrored from Astronomy.Catalog.Tests/TargetResolverTests) ----

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

    private static TsTarget TsT(long id, string name, double ra, double dec, long project, string guid) =>
        new(id, name, 1, ra, dec, 2, null, null, project, -1, guid);

    private static TsExposurePlan TsP(long id, long target, long template) =>
        new(id, "profile-1", 300.0, 60, 10, 8, target, template);
}
