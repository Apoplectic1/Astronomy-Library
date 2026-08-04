using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.Reconcile;

/// <summary>
/// The single definition of capture-configuration pairing between the plan plane and the disk plane:
/// gain, offset and binning pair by plain value equality after plan-side normalization, with the plan's
/// "use the camera's default" sentinel (<see cref="Sentinel"/>) kept as the value it is — an unspecified
/// value can never be asserted to agree with anything captured, so a sentinel-carrying plan pairs with no
/// disk configuration. Dimensions only one plane can express (e.g. a readout mode the disk plane does not
/// record) are absent from the comparison entirely: a dimension participates only when both planes express
/// it. Every consumer that states a pairing verdict — cell merging, write-back crediting, assignment
/// cautions — derives it from here, so the verdicts can never drift apart.
/// </summary>
public static class CaptureConfigPairing
{
    /// <summary>The plan plane's "use the camera's default" sentinel, compared as the value it is.</summary>
    public const int Sentinel = -1;

    /// <summary>The plan-side gain the comparison (and any cell key) uses: the template's value, or
    /// <see cref="Sentinel"/> when unspecified.</summary>
    public static int PlanGain(ExposureTemplate template) => template.Gain ?? Sentinel;

    /// <summary>The plan-side offset the comparison (and any cell key) uses: the template's value, or
    /// <see cref="Sentinel"/> when unspecified.</summary>
    public static int PlanOffset(ExposureTemplate template) => template.OffsetAdu ?? Sentinel;

    /// <summary>The plan-side square binning the comparison (and any cell key) uses; an unspecified or
    /// non-positive value reads as 1 (the plan plane's binning has no sentinel convention).</summary>
    public static int PlanBin(ExposureTemplate template) => template.Binning is int b && b > 0 ? b : 1;

    /// <summary>Value-level pairing: true when the plan configuration equals the disk configuration on
    /// every compared dimension. A <see cref="Sentinel"/> plan value never equals a captured one.</summary>
    public static bool Pairs(
        int planGain, int planOffset, int planBin,
        int diskGain, int diskOffset, int diskBinX, int diskBinY) =>
        planGain == diskGain && planOffset == diskOffset && planBin == diskBinX && planBin == diskBinY;

    /// <summary>Whether <paramref name="template"/>'s configuration pairs with the disk aggregate
    /// <paramref name="inventory"/>.</summary>
    public static bool Pairs(ExposureTemplate template, InventoryFilter inventory) =>
        Pairs(PlanGain(template), PlanOffset(template), PlanBin(template),
            inventory.TypicalGain, inventory.TypicalOffset,
            inventory.TypicalBinningX, inventory.TypicalBinningY);
}
