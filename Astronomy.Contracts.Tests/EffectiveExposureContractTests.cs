using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md "Semantic assumptions" #19 — THE effective sub-length rule:
/// a plan's whole-second effective exposure is its own value when set, else its template's default;
/// the raw-TS overload treats a NEGATIVE exposure as TS's "use template default" sentinel; both-null
/// resolves to 0, which never matches a scanner bucket (all ≥ 1). TSM's reconciliation cells and the
/// write-back key's seconds component both hang off this one definition — a sign/precedence change
/// here compiles cleanly and silently mis-buckets every cell.
/// </summary>
public sealed class EffectiveExposureContractTests
{
    // ---- catalog-side overload (resolver already normalized TS's sentinel to null) ----------------

    [Fact]
    public void CatalogSide_PlanOwnValue_WinsOverTemplateDefault()
    {
        Assert.Equal(240, EffectiveExposure.Seconds(
            CatalogPlan(exposureSeconds: 240.2),
            CatalogTemplate(defaultExposureSeconds: 300.0)));
    }

    [Fact]
    public void CatalogSide_NullPlanValue_DefersToTemplateDefault()
    {
        Assert.Equal(300, EffectiveExposure.Seconds(
            CatalogPlan(exposureSeconds: null),
            CatalogTemplate(defaultExposureSeconds: 300.0)));
    }

    [Fact]
    public void CatalogSide_BothNull_ResolvesToZero_TheNeverMatchingBucket()
    {
        // 0 is deliberate: scanner buckets are ≥ 1, so a synthetic both-null plan pairs with nothing.
        Assert.Equal(0, EffectiveExposure.Seconds(
            CatalogPlan(exposureSeconds: null),
            CatalogTemplate(defaultExposureSeconds: null)));
    }

    [Fact]
    public void CatalogSide_RoundsToWholeSeconds()
    {
        Assert.Equal(180, EffectiveExposure.Seconds(
            CatalogPlan(exposureSeconds: 180.4), CatalogTemplate(defaultExposureSeconds: null)));
        Assert.Equal(181, EffectiveExposure.Seconds(
            CatalogPlan(exposureSeconds: 180.6), CatalogTemplate(defaultExposureSeconds: null)));
    }

    // ---- raw-TS overload (sentinel still in band) --------------------------------------------------

    [Fact]
    public void RawTs_NegativeExposure_IsTheDeferToTemplateSentinel()
    {
        Assert.Equal(300, EffectiveExposure.Seconds(
            TsPlan(exposure: -1.0), TsTemplate(defaultExposure: 300.0)));
    }

    [Fact]
    public void RawTs_PositiveExposure_WinsOverTemplateDefault()
    {
        Assert.Equal(120, EffectiveExposure.Seconds(
            TsPlan(exposure: 120.0), TsTemplate(defaultExposure: 300.0)));
    }

    [Fact]
    public void RawTs_ZeroExposure_IsTakenLiterally_NotAsSentinel()
    {
        // PINS CURRENT BEHAVIOR — known divergence flagged in CONSUMERS.md #19: this overload's
        // sentinel test is `< 0` (0 is a literal zero-second exposure), while
        // TargetSchedulerEditor.ReadPlanEffectiveExposure's SQL uses `exposure > 0` as the override
        // test (0 defers to the template) — see TargetSchedulerContractTests. The two disagree at
        // exactly 0; adjudicate against TS's own semantics before relying on either at 0.
        Assert.Equal(0, EffectiveExposure.Seconds(
            TsPlan(exposure: 0.0), TsTemplate(defaultExposure: 300.0)));
    }

    // ---- fixtures (named args — the positional-ctor reorder hazard is CONSUMERS.md "Fragility") ----

    private static ExposurePlan CatalogPlan(double? exposureSeconds) => new(
        Id: Guid.NewGuid(), TargetId: Guid.NewGuid(), ExposureTemplateId: Guid.NewGuid(),
        ExposureSeconds: exposureSeconds, DesiredCount: 10, AcquiredCount: 0, AcceptedCount: 0,
        Enabled: true, ImportedFromTsGuid: null);

    private static ExposureTemplate CatalogTemplate(double? defaultExposureSeconds) => new(
        Id: Guid.NewGuid(), ProfileId: Guid.NewGuid(), Name: "Ha 300", FilterName: "Ha",
        Gain: 100, OffsetAdu: 10, Binning: 1, ReadoutMode: 0,
        DefaultExposureSeconds: defaultExposureSeconds, ImportedFromTsGuid: null);

    private static TsExposurePlan TsPlan(double exposure) => new(
        Id: 1, ProfileId: "p", Exposure: exposure, Desired: 10, Acquired: 0, Accepted: 0,
        TargetId: 1, ExposureTemplateId: 1);

    private static TsExposureTemplate TsTemplate(double defaultExposure) => new(
        Id: 1, ProfileId: "p", Name: "Ha 300", FilterName: "Ha",
        Gain: 100, Offset: 10, Bin: 1, DefaultExposure: defaultExposure);
}
