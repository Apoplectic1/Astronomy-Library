using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Catalog.Tests.Scan;

/// <summary>The altitude-clause grammar (tightened + given compose/read/extract verbs 2026-08-16,
/// openspec project-name-altitude-clause): the clause is exactly the spaced <c>" - N"</c> suffix.
/// Names may carry it as a definitional minimum-altitude mirror while capture directories stay bare —
/// stripping must be exact about the clause and leave everything else alone. The spaces are
/// load-bearing: the loose dashed match was only safe while stripping was symmetric; with clauses on
/// one side of a compare only, a designation like "Sh2-155" must never strip on the bare side.</summary>
public class MosaicConventionTests
{
    // ---- StripAltitudeClause (the matcher path: spaced clause only) ----------------------------------

    [Theory]
    [InlineData("Mosaic - Cygnus Loop - 25", "Mosaic - Cygnus Loop")]              // the short authoring form
    [InlineData("Mosaic - Clamshell  - 30", "Mosaic - Clamshell")]                 // short form, stray double space
    [InlineData("Mosaic - X - 27.5", "Mosaic - X")]                                // short form, decimal
    [InlineData("Nebulae - 0", "Nebulae")]                                         // zero floor is a real clause
    [InlineData("Veil - 3 - 30", "Veil - 3")]                                      // only the FINAL clause strips
    [InlineData("Mosaic - X - 30 ", "Mosaic - X")]                                 // trailing whitespace
    public void StripAltitudeClause_RemovesOneTrailingSpacedClause(string name, string expected) =>
        Assert.Equal(expected, MosaicConvention.StripAltitudeClause(name));

    [Theory]
    [InlineData("Mosaic - Cygnus Loop")]        // no clause — unchanged
    [InlineData("Above the Clouds")]            // "Above" inside a name is not a clause
    [InlineData("Mosaic - Above")]              // clause word without a number is part of the name
    [InlineData("Mosaic - Above 30 - Rosette")] // clause-like text not at the end
    [InlineData("Abell 2218")]                  // space-number without the dash is a NAME, never a clause
    [InlineData("Sh2-155")]                     // hyphen-digit designation: no spaces, never a clause
    [InlineData("Mosaic - X-30")]               // dashed number without the spaces is part of the name
    [InlineData("Mosaic - Cygnus Loop - Above 25")]  // the RETIRED legacy form is no longer a clause here
    public void StripAltitudeClause_LeavesNonClauseNamesAlone(string name) =>
        Assert.Equal(name, MosaicConvention.StripAltitudeClause(name));

    // ---- ComposeAltitudeName -------------------------------------------------------------------------

    [Theory]
    [InlineData("Nebulae", 45, "Nebulae - 45")]                 // integer renders bare
    [InlineData("Nebulae", 89.9, "Nebulae - 89.9")]             // tenths kept
    [InlineData("Nebulae", 0, "Nebulae - 0")]                   // zero floor composes like any other
    [InlineData("Mosaic - Pleiades", 50, "Mosaic - Pleiades - 50")]
    [InlineData("Veil - 3", 30, "Veil - 3 - 30")]               // clause-like base composes verbatim
    [InlineData("Nebulae  ", 45, "Nebulae - 45")]               // base trailing whitespace trimmed
    public void ComposeAltitudeName_ComposesTheSpacedForm(string baseName, double deg, string expected) =>
        Assert.Equal(expected, MosaicConvention.ComposeAltitudeName(baseName, deg));

    // ---- TryReadAltitudeClause -----------------------------------------------------------------------

    [Theory]
    [InlineData("Nebulae - 45", 45)]
    [InlineData("Nebulae - 89.9", 89.9)]
    [InlineData("Nebulae - 0", 0)]
    [InlineData("Veil - 3 - 30", 30)]           // the FINAL clause is the clause
    public void TryReadAltitudeClause_ReadsTheSpacedClause(string name, double expected)
    {
        Assert.True(MosaicConvention.TryReadAltitudeClause(name, out double deg));
        Assert.Equal(expected, deg);
    }

    [Theory]
    [InlineData("Sh2-155")]                     // hyphen-digit designation misses
    [InlineData("Abell 2218")]                  // bare trailing number misses
    [InlineData("Nebulae - Above 45")]          // the retired legacy form misses (nonconforming, not a clause)
    [InlineData("Galaxies")]                    // no clause at all
    public void TryReadAltitudeClause_MissesNonClauses(string name) =>
        Assert.False(MosaicConvention.TryReadAltitudeClause(name, out _));

    // ---- ExtractBaseName -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Nebulae - 45", "Nebulae")]                     // spaced clause strips
    [InlineData("Nebulae - Above 45", "Nebulae")]               // legacy strips HERE (and only here) — heals on recompose
    [InlineData("Nebulae - above45", "Nebulae")]                // legacy tolerance: case + missing space before the number
    [InlineData("Galaxies", "Galaxies")]                        // no clause: whole name is the base
    [InlineData("Sh2-155", "Sh2-155")]                          // designation untouched
    [InlineData("Veil - 3 - 30", "Veil - 3")]                   // round-trip: only the final clause strips
    [InlineData("Mosaic - Pleiades - 50", "Mosaic - Pleiades")]
    public void ExtractBaseName_StripsOneClause_LegacyIncluded(string name, string expected) =>
        Assert.Equal(expected, MosaicConvention.ExtractBaseName(name));

    [Fact]
    public void ComposeThenExtract_RoundTrips_EvenForClauseLikeBases()
    {
        string composed = MosaicConvention.ComposeAltitudeName("Veil - 3", 30);
        Assert.Equal("Veil - 3", MosaicConvention.ExtractBaseName(composed));
        Assert.True(MosaicConvention.TryReadAltitudeClause(composed, out double deg));
        Assert.Equal(30, deg);
    }
}
