using Astronomy.Catalog.Scan;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class FilterPurposeClassifierTests
{
    [Theory]
    [InlineData("B300", FilterPurpose.Light)]
    [InlineData("B", FilterPurpose.Light)]
    [InlineData("Stars B", FilterPurpose.Stars)]
    [InlineData("stars b", FilterPurpose.Stars)]   // case-insensitive
    [InlineData("Starship", FilterPurpose.Light)]  // prefix requires the trailing space
    public void Classify_ByStarsPrefix(string name, FilterPurpose expected) =>
        Assert.Equal(expected, FilterPurposeClassifier.Classify(name));
}
