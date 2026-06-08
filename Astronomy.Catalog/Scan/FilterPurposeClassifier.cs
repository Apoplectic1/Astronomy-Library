namespace Astronomy.Catalog.Scan;

/// <summary>
/// Classifies a name as <see cref="FilterPurpose.Light"/> or <see cref="FilterPurpose.Stars"/> by the
/// <c>"Stars "</c> prefix convention. The same convention tags both disk filter directories (e.g. <c>"Stars B"</c>)
/// and N.I.N.A. Target Scheduler exposure-template names (e.g. <c>"Stars B"</c> vs <c>"B300"</c>), so the scanner
/// and any consumer that anchors plans to disk frames share one rule for deciding purpose.
/// </summary>
public static class FilterPurposeClassifier
{
    /// <summary>The directory / template name prefix that marks short-exposure star-only frames.</summary>
    public const string StarsPrefix = "Stars ";

    /// <summary>
    /// Returns <see cref="FilterPurpose.Stars"/> when <paramref name="name"/> begins with
    /// <see cref="StarsPrefix"/> (case-insensitive), otherwise <see cref="FilterPurpose.Light"/>.
    /// </summary>
    public static FilterPurpose Classify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(StarsPrefix, StringComparison.OrdinalIgnoreCase)
            ? FilterPurpose.Stars
            : FilterPurpose.Light;
    }
}
