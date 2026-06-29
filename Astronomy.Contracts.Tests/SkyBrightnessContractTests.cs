using Astronomy.Core.Brightness;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract test for the SkyBrightness.KsAt positional-parameter order — CONSUMERS.md
/// "Semantic assumptions" #4 (units / encoding, the silent-wrong-result class).
/// </summary>
public sealed class SkyBrightnessContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #4:
    //   "Brightness.SkyBrightness.KsAt — 10 positional params, order load-bearing
    //    (reorder compiles, computes wrong)."
    // KsAt takes ten bare doubles; a same-typed reorder of the signature would compile
    // at every call site yet compute a different sky brightness. This pins a GOLDEN value
    // for a fixed, distinctive input vector (every param has a non-degenerate effect:
    // moon up, nautical twilight active, narrowband bandwidth+center ≠ V-band reference),
    // so any reorder of the parameter list shifts the result far past the tolerance.
    // Golden computed offline by replaying the documented K-S 1991 formula
    // (Twilight + dark-sky + moon contributions); locked to 6 dp (FMA-robust).
    // ---------------------------------------------------------------------------

    [Fact]
    public void KsAt_FixedInputs_ReturnsPinnedGoldenValue()
    {
        double v = SkyBrightness.KsAt(
            targetAltDeg: 45.0,
            targetAzDeg: 180.0,
            moonAltDeg: 30.0,
            moonAzDeg: 90.0,
            moonPhaseAngleDeg: 60.0,
            sunAltDeg: -12.0,        // nautical twilight ⇒ centerNm/Rayleigh scaling is live
            extinctionKBand: 0.3,
            v0Mag: 21.0,
            bandwidthNm: 3.0,        // narrowband ≠ BWRefNm(85) ⇒ bandwidth scale is live
            centerNm: 500.0);        // ≠ VBandCenterNm(540) ⇒ Rayleigh λ⁻⁴ scale is live

        Assert.Equal(22.516092, v, precision: 6);
    }

    // A degenerate companion that documents the closed-form anchor: at the zenith with the
    // moon down and the sun below astronomical twilight and the V-band reference bandwidth,
    // KsAt collapses to (essentially) v0Mag — the moonless dark-sky baseline. Locks that the
    // dark-sky term is the v0Mag param (8th positional), not, say, the extinction param.
    [Fact]
    public void KsAt_ZenithNoMoonNoTwilight_CollapsesToV0()
    {
        double v = SkyBrightness.KsAt(
            targetAltDeg: 90.0, targetAzDeg: 0.0,
            moonAltDeg: -5.0, moonAzDeg: 0.0,      // moon down ⇒ no moon contribution
            moonPhaseAngleDeg: 180.0,
            sunAltDeg: -30.0,                       // below −18° ⇒ no twilight
            extinctionKBand: 0.3,
            v0Mag: 21.0,
            bandwidthNm: SkyBrightness.BWRefNm,     // reference width ⇒ no bandwidth scaling
            centerNm: SkyBrightness.VBandCenterNm);

        Assert.Equal(21.0, v, precision: 4);        // ≈ v0Mag (zenith airmass ≈ 1)
    }
}
