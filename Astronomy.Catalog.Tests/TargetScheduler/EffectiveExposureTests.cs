using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using Xunit;

namespace Astronomy.Catalog.Tests.TargetScheduler;

public class EffectiveExposureTests
{
    [Theory]
    [InlineData(600.0, 300.0, 600)]   // plan's own value wins over the template default
    [InlineData(null, 300.0, 300)]    // null plan value falls back to the template default
    [InlineData(599.6, null, 600)]    // whole-second rounding
    [InlineData(null, null, 0)]       // both null (synthetic data): 0, which never matches a scanner bucket
    public void CatalogSide_PlanValueElseTemplateDefault(double? planSeconds, double? templateDefault, int expected)
    {
        ExposurePlan plan = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), planSeconds,
            DesiredCount: 10, AcquiredCount: 0, AcceptedCount: 0, Enabled: true, ImportedFromTsGuid: null);
        ExposureTemplate tpl = new(Guid.NewGuid(), Guid.NewGuid(), "H", "H", Gain: null, OffsetAdu: null,
            Binning: null, ReadoutMode: null, templateDefault, ImportedFromTsGuid: null);

        Assert.Equal(expected, EffectiveExposure.Seconds(plan, tpl));
    }

    [Theory]
    [InlineData(-1.0, 300.0, 300)]    // TS's "use template default" sentinel
    [InlineData(600.0, 300.0, 600)]   // explicit plan exposure wins
    [InlineData(599.6, 300.0, 600)]   // whole-second rounding
    public void RawTsSide_NegativeSentinelMeansTemplateDefault(double exposure, double templateDefault, int expected)
    {
        TsExposurePlan plan = new(Id: 1, ProfileId: "p", exposure, Desired: 10, Acquired: 0, Accepted: 0,
            TargetId: 1, ExposureTemplateId: 1000);
        TsExposureTemplate tpl = new(Id: 1000, ProfileId: "p", Name: "H", FilterName: "H", Gain: 100,
            Offset: 50, Bin: 1, templateDefault);

        Assert.Equal(expected, EffectiveExposure.Seconds(plan, tpl));
    }
}
