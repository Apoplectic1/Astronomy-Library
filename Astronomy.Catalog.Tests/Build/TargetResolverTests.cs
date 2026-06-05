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

    // ---- synthetic builders -------------------------------------------------

    private static TargetReport Disk(string dir, string cat, string common, double raH, double dec, bool withFilter)
    {
        FilterAggregate[] filters = withFilter ? [SampleFilter()] : [];
        return new TargetReport(dir, cat, common, cat, raH, dec, filters);
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
