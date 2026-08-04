using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Xunit;

namespace Astronomy.Catalog.Tests;

public sealed class CaptureConfigPairingTests
{
    [Fact]
    public void EqualConfigs_Pair()
    {
        Assert.True(CaptureConfigPairing.Pairs(
            planGain: 0, planOffset: 10, planBin: 1,
            diskGain: 0, diskOffset: 10, diskBinX: 1, diskBinY: 1));
    }

    [Theory]
    [InlineData(53, 10, 1)]    // gain differs
    [InlineData(0, 50, 1)]     // offset differs
    [InlineData(0, 10, 2)]     // binning differs
    public void AnyDifferingDimension_DoesNotPair(int diskGain, int diskOffset, int diskBin)
    {
        Assert.False(CaptureConfigPairing.Pairs(
            planGain: 0, planOffset: 10, planBin: 1,
            diskGain, diskOffset, diskBin, diskBin));
    }

    [Fact]
    public void NonSquareDiskBinning_DoesNotPair()
    {
        Assert.False(CaptureConfigPairing.Pairs(
            planGain: 0, planOffset: 10, planBin: 1,
            diskGain: 0, diskOffset: 10, diskBinX: 1, diskBinY: 2));
    }

    [Fact]
    public void SentinelPlanValue_NeverPairs_EvenAgainstTheCamerasActualSetting()
    {
        // The sentinel means "use the camera's default" — whatever was captured cannot be asserted to
        // agree with an unspecified value, so no disk gain pairs with it.
        Assert.False(CaptureConfigPairing.Pairs(
            planGain: CaptureConfigPairing.Sentinel, planOffset: 10, planBin: 1,
            diskGain: 100, diskOffset: 10, diskBinX: 1, diskBinY: 1));
        Assert.False(CaptureConfigPairing.Pairs(
            planGain: 0, planOffset: CaptureConfigPairing.Sentinel, planBin: 1,
            diskGain: 0, diskOffset: 50, diskBinX: 1, diskBinY: 1));
    }

    [Fact]
    public void TemplateNormalization_SentinelAndBinDefaults()
    {
        ExposureTemplate sentinel = Template(gain: null, offset: null, bin: null);
        Assert.Equal(CaptureConfigPairing.Sentinel, CaptureConfigPairing.PlanGain(sentinel));
        Assert.Equal(CaptureConfigPairing.Sentinel, CaptureConfigPairing.PlanOffset(sentinel));
        Assert.Equal(1, CaptureConfigPairing.PlanBin(sentinel));   // binning has no sentinel: unspecified reads 1

        ExposureTemplate explicitTpl = Template(gain: 111, offset: 10, bin: 2);
        Assert.Equal(111, CaptureConfigPairing.PlanGain(explicitTpl));
        Assert.Equal(10, CaptureConfigPairing.PlanOffset(explicitTpl));
        Assert.Equal(2, CaptureConfigPairing.PlanBin(explicitTpl));
    }

    [Fact]
    public void TemplateAgainstInventory_UsesNormalizedValues()
    {
        InventoryFilter inv = Inventory(gain: 111, offset: 10, binX: 1, binY: 1);
        Assert.True(CaptureConfigPairing.Pairs(Template(gain: 111, offset: 10, bin: 1), inv));
        Assert.False(CaptureConfigPairing.Pairs(Template(gain: 0, offset: 10, bin: 1), inv));
        Assert.False(CaptureConfigPairing.Pairs(Template(gain: null, offset: 10, bin: 1), inv));
    }

    private static ExposureTemplate Template(int? gain, int? offset, int? bin) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "H", "H", Gain: gain, OffsetAdu: offset, Binning: bin,
            ReadoutMode: null, DefaultExposureSeconds: 300.0, ImportedFromTsGuid: null);

    private static InventoryFilter Inventory(int gain, int offset, int binX, int binY) =>
        new(Guid.NewGuid(), "H", FilterPurpose.Light, "H", ExposureCount: 10,
            TotalIntegrationSeconds: 3000.0, FirstImagedAt: 0, LastImagedAt: 0,
            TypicalGain: gain, TypicalOffset: offset, TypicalSetTempC: -10.0,
            TypicalBinningX: binX, TypicalBinningY: binY, ExposureSeconds: 300.0, Camera: "Z533",
            FramingOrdinal: 0, RotationExpression: RotationExpression.Sky, RotationFoldDeg: 20.0);
}
