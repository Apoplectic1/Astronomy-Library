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

    /// <summary>Raw TS-side rule: a negative exposure is TS's "use the template default" sentinel; 0 is a
    /// literal zero-second exposure. (TS's own planner tests <c>!= -1</c> — <c>PlanningExposure.cs</c>; only
    /// -1 ever occurs, so treating every negative as the sentinel is indistinguishable in-contract. Adjudicated
    /// 2026-07-07 against the TS source: 0 is literal, matching the planner, not TS's sync-client wrinkle that
    /// re-marks <c>&lt;= 0</c> as unset.)</summary>
    public static int Seconds(TsExposurePlan plan, TsExposureTemplate template) =>
        (int)Math.Round(plan.Exposure < 0 ? template.DefaultExposure : plan.Exposure);
}
