using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// THE effective sub-length rule: a plan's whole-second exposure duration is its own value when set, else
/// its template's default. The write key's seconds component and any consumer projecting reconciliation
/// cells MUST agree on this — one definition, no re-implementations.
/// </summary>
public static class EffectiveExposure
{
    /// <summary>
    /// Catalog-side rule (the resolver already normalized the external scheduler's "use template default"
    /// sentinel to null). The 0 fallback — both values null, synthetic data only — never matches a scanner
    /// bucket (those are ≥ 1), so such a plan deterministically pairs with nothing.
    /// </summary>
    public static int Seconds(ExposurePlan plan, ExposureTemplate template) =>
        (int)Math.Round(plan.ExposureSeconds ?? template.DefaultExposureSeconds ?? 0.0);

    /// <summary>Raw TS-side rule: a negative exposure is TS's "use the template default" sentinel.</summary>
    public static int Seconds(TsExposurePlan plan, TsExposureTemplate template) =>
        (int)Math.Round(plan.Exposure < 0 ? template.DefaultExposure : plan.Exposure);
}
