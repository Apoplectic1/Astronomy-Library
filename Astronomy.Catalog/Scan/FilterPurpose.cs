namespace Astronomy.Catalog.Scan;

/// <summary>
/// Why a set of frames was captured. Separates "main subject" Light frames from
/// short-exposure "stars only" companion frames used in the broadband
/// starless-recombination workflow.
/// </summary>
public enum FilterPurpose
{
    /// <summary>Standard light frames intended as the primary subject capture.</summary>
    Light,

    /// <summary>Short-exposure star-only frames (from <c>Stars &lt;Filter&gt;</c> directories) used to recombine stars onto a starless-processed broadband image.</summary>
    Stars,
}
