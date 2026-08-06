using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Catalog.Tests.Scan;

/// <summary>The altitude-clause tolerance behind the mosaic name-match (2026-08-06): project names may
/// carry a trailing minimum-altitude suffix as an authoring convention while capture directories stay
/// bare — stripping must be exact about the clause and leave everything else alone.</summary>
public class MosaicConventionTests
{
    [Theory]
    [InlineData("Mosaic - Cygnus Loop - 25", "Mosaic - Cygnus Loop")]              // the short authoring form
    [InlineData("Mosaic - Clamshell  - 30", "Mosaic - Clamshell")]                 // short form, stray double space
    [InlineData("Mosaic - X - 27.5", "Mosaic - X")]                                // short form, decimal
    [InlineData("Mosaic - Cygnus Loop - Above 25", "Mosaic - Cygnus Loop")]        // legacy form still stripped
    [InlineData("Mosaic - Clamshell  - Above 30", "Mosaic - Clamshell")]   // stray double space before the clause
    [InlineData("Mosaic - X - above 30", "Mosaic - X")]                    // case-insensitive
    [InlineData("Mosaic - X-Above 30", "Mosaic - X")]                      // spacing-tolerant around the dash
    [InlineData("Mosaic - X - Above 27.5", "Mosaic - X")]                  // decimal degrees
    [InlineData("Mosaic - X - Above 30 ", "Mosaic - X")]                   // trailing whitespace
    public void StripAltitudeClause_RemovesOneTrailingClause(string name, string expected) =>
        Assert.Equal(expected, MosaicConvention.StripAltitudeClause(name));

    [Theory]
    [InlineData("Mosaic - Cygnus Loop")]        // no clause — unchanged
    [InlineData("Above the Clouds")]            // "Above" inside a name is not a clause
    [InlineData("Mosaic - Above")]              // clause word without a number is part of the name
    [InlineData("Mosaic - Above 30 - Rosette")] // clause-like text not at the end
    [InlineData("Abell 2218")]                  // space-number without the dash is a NAME, never a clause
    public void StripAltitudeClause_LeavesNonClauseNamesAlone(string name) =>
        Assert.Equal(name, MosaicConvention.StripAltitudeClause(name));
}
