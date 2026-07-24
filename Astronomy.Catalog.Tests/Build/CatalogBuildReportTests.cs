using Astronomy.Catalog.Build;
using Xunit;

namespace Astronomy.Catalog.Tests.Build;

public class CatalogBuildReportTests
{
    [Fact]
    public void IssuesFor_CombinesIssueLists_PerDirectory()
    {
        CatalogBuildReport report = Report(
            mismatches: [new NameMismatch(null, "Sh2 132", "Sh2-132 - Lion", "Sh2-132", 0.1)],
            ambiguous: [new AmbiguousMatch(null, "Twins", ["Sh2-132 - Lion", "NGC 7000"], 0.2)],
            duplicates: [new DuplicateTsTarget("M 31", ["M31", "Andromeda"])]);

        Assert.Equal(TargetMatchIssues.NameMismatch | TargetMatchIssues.AmbiguousMatch, report.IssuesFor("Sh2-132 - Lion"));
        Assert.Equal(TargetMatchIssues.AmbiguousMatch, report.IssuesFor("NGC 7000"));
        Assert.Equal(TargetMatchIssues.Duplicate, report.IssuesFor("M 31"));
        Assert.Equal(TargetMatchIssues.None, report.IssuesFor("Unflagged Dir"));
        Assert.Equal(TargetMatchIssues.None, report.IssuesFor(null));
    }

    [Fact]
    public void IsIdentityFlagged_TrueOnlyForMismatchOrAmbiguous()
    {
        CatalogBuildReport report = Report(
            mismatches: [new NameMismatch(null, "Sh2 132", "Sh2-132 - Lion", "Sh2-132", 0.1)],
            duplicates: [new DuplicateTsTarget("M 31", ["M31", "Andromeda"])]);

        Assert.True(report.IsIdentityFlagged("Sh2-132 - Lion"));
        Assert.False(report.IsIdentityFlagged("M 31"));        // duplicate fold: routed, not identity-suspect
        Assert.False(report.IsIdentityFlagged(null));
    }

    [Fact]
    public void DirectoryLookups_AreCaseInsensitive()
    {
        CatalogBuildReport report = Report(
            mismatches: [new NameMismatch(null, "X", "Mosaic - Cygnus Loop/Panel 01of16", null, 0.1)],
            duplicates: [new DuplicateTsTarget("NGC 6888", ["NGC 6888", "Crescent"])]);

        Assert.True(report.IsIdentityFlagged("mosaic - cygnus loop/panel 01OF16"));
        Assert.Equal(TargetMatchIssues.Duplicate, report.IssuesFor("ngc 6888"));
    }

    [Fact]
    public void IsUnanchoredName_MatchesCaseInsensitively()
    {
        CatalogBuildReport report = Report(unanchored: [new UnanchoredTsTarget(null, "LBN 437")]);

        Assert.True(report.IsUnanchoredName("lbn 437"));
        Assert.False(report.IsUnanchoredName("LBN 438"));
    }

    private static CatalogBuildReport Report(
        IReadOnlyList<NameMismatch>? mismatches = null,
        IReadOnlyList<AmbiguousMatch>? ambiguous = null,
        IReadOnlyList<DuplicateTsTarget>? duplicates = null,
        IReadOnlyList<UnanchoredTsTarget>? unanchored = null) => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        mismatches ?? [], ambiguous ?? [], duplicates ?? [], unanchored ?? [], []);
}
