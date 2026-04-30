using System;
using Astronomy.Core.Brightness;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Correctness guard for SkyBrightness: the K-S 1991 formula's structural
    // properties (moonless baseline, moon-down sentinel, airmass / phase / extinction
    // monotonicity) must hold, and the Bortle lookup tables must produce sane
    // ordering. No formal byte-for-byte parity reference exists in the codebase yet
    // (K-S original paper has Table 1 but it's hand-typed); the tests below are
    // structural / range / monotonicity assertions sufficient for v1 test-bed.
    public class SkyBrightnessTests
    {
        // ============================================================
        // KsAt -- moon-down sentinel and baseline
        // ============================================================

        [Fact]
        public void KsAt_MoonBelowHorizon_ReturnsBaselineNearV0()
        {
            // Target at zenith, moon well below horizon -> moon contribution is zero,
            // sky should be approximately V₀ (small extinction adjustment from the
            // airmass-1 term cancels out at zenith).
            double v0 = 21.5;
            double sky = SkyBrightness.KsAt(
                targetAltDeg: 90.0, targetAzDeg: 0.0,
                moonAltDeg:  -30.0, moonAzDeg:  180.0,
                moonPhaseAngleDeg: 0.0,   // full moon, but it's below horizon
                sunAltDeg:        -30.0,  // deep night; no twilight
                extinctionKBand:   0.20,
                v0Mag: v0);
            // V(X=1) = V₀ - 2.5 log10(1) + k * (1 - 1) = V₀ exactly.
            Assert.Equal(v0, sky, 5);
        }

        [Fact]
        public void KsAt_TargetBelowHorizon_ReturnsNaN()
        {
            double sky = SkyBrightness.KsAt(
                targetAltDeg: -5.0, targetAzDeg: 0.0,
                moonAltDeg:   45.0, moonAzDeg:  90.0,
                moonPhaseAngleDeg: 0.0,
                sunAltDeg:        -30.0,
                extinctionKBand:   0.20,
                v0Mag: 21.5);
            Assert.True(double.IsNaN(sky));
        }

        [Fact]
        public void KsAt_TargetAtLowAltitude_BrighterThanZenith()
        {
            // Same dark site, no moon. Low-altitude target sees more airmass -> more
            // scattered ground/airglow light -> brighter (lower magnitude).
            double v0 = 21.5;
            double zenith = SkyBrightness.KsAt(
                targetAltDeg: 89.0, targetAzDeg: 0.0,
                moonAltDeg:  -30.0, moonAzDeg:  180.0,
                moonPhaseAngleDeg: 180.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            double low = SkyBrightness.KsAt(
                targetAltDeg: 20.0, targetAzDeg: 0.0,
                moonAltDeg:  -30.0, moonAzDeg:  180.0,
                moonPhaseAngleDeg: 180.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            Assert.True(low < zenith, $"expected low ({low}) brighter than zenith ({zenith})");
        }

        // ============================================================
        // KsAt -- moon contribution shape
        // ============================================================

        [Fact]
        public void KsAt_FullMoonNearTarget_VeryBrightSky()
        {
            // Full moon high in sky, target ~10° away from it. K-S should produce
            // a dramatically brightened sky vs the moonless case.
            double v0 = 21.5;
            double moonless = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:  -10.0, moonAzDeg:  0.0,
                moonPhaseAngleDeg: 0.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            double withFullMoon = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:   60.0, moonAzDeg:  10.0,   // moon ~10° from target, both high
                moonPhaseAngleDeg: 0.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            // Full moon near target should brighten the sky by several magnitudes.
            Assert.True(withFullMoon < moonless - 2.0,
                $"expected full-moon sky {withFullMoon} >2 mag brighter than moonless {moonless}");
            // Predicted sky should still be in a physically plausible range.
            Assert.InRange(withFullMoon, 14.0, 22.0);
        }

        [Fact]
        public void KsAt_NewMoonNearTarget_NoMeaningfulContribution()
        {
            // New moon (α = 180°) is essentially dark; even close to the target the
            // contribution to sky brightness should be negligible (< 0.1 mag/arcsec²).
            double v0 = 21.5;
            double moonless = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:  -10.0, moonAzDeg:  0.0,
                moonPhaseAngleDeg: 180.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            double newMoon = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:   60.0, moonAzDeg:  10.0,
                moonPhaseAngleDeg: 180.0, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: v0);
            Assert.True(Math.Abs(newMoon - moonless) < 0.1,
                $"expected new-moon contribution negligible; got Δ = {newMoon - moonless}");
        }

        [Fact]
        public void KsAt_PhaseAngleMonotonic_FullBrighterThanQuarter()
        {
            // Same geometry, sweep phase angle. Full (α=0) -> brightest;
            // first/last quarter (α=90) -> medium; new (α=180) -> dimmest.
            double Sky(double alpha) => SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:   60.0, moonAzDeg:  10.0,
                moonPhaseAngleDeg: alpha, sunAltDeg: -30.0,
                extinctionKBand: 0.20, v0Mag: 21.5);
            double full    = Sky(0.0);
            double quarter = Sky(90.0);
            double newMoon = Sky(180.0);
            Assert.True(full < quarter, $"full ({full}) should be brighter than quarter ({quarter})");
            Assert.True(quarter < newMoon, $"quarter ({quarter}) should be brighter than new ({newMoon})");
        }

        // ============================================================
        // ScaleK -- Rayleigh λ⁻⁴ wavelength scaling
        // ============================================================

        [Fact]
        public void ScaleK_At500nmReference_ReturnsInputUnchanged()
        {
            Assert.Equal(0.18, SkyBrightness.ScaleK(0.18, 500.0), 12);
        }

        [Fact]
        public void ScaleK_BlueBandHeavierExtinction()
        {
            // Rayleigh: shorter wavelength scatters more strongly (λ⁻⁴).
            // B-band (445 nm) should have higher extinction than R-band (650 nm).
            double k500 = 0.20;
            double kB = SkyBrightness.ScaleK(k500, 445.0);
            double kR = SkyBrightness.ScaleK(k500, 650.0);
            Assert.True(kB > k500, $"k(445) = {kB} should exceed k(500) = {k500}");
            Assert.True(kR < k500, $"k(650) = {kR} should be less than k(500) = {k500}");
            Assert.True(kB > kR,    $"k(B) = {kB} should exceed k(R) = {kR}");
        }

        [Fact]
        public void ScaleK_ExactRayleighRatio()
        {
            // k(λ) = k_500 × (500/λ)^4. At λ = 1000 nm: ratio = 0.5^4 = 0.0625.
            Assert.Equal(0.20 * 0.0625, SkyBrightness.ScaleK(0.20, 1000.0), 6);
        }

        // ============================================================
        // Airmass -- Pickering's formula sanity
        // ============================================================

        [Fact]
        public void Airmass_AtZenith_IsOne()
        {
            Assert.Equal(1.0, SkyBrightness.Airmass(90.0), 4);
        }

        [Fact]
        public void Airmass_MonotonicallyIncreasingTowardHorizon()
        {
            double a90 = SkyBrightness.Airmass(90.0);
            double a45 = SkyBrightness.Airmass(45.0);
            double a30 = SkyBrightness.Airmass(30.0);
            double a10 = SkyBrightness.Airmass(10.0);
            Assert.True(a90 < a45);
            Assert.True(a45 < a30);
            Assert.True(a30 < a10);
            // sec(60°) = 2 -> a30 should be near 2 (Pickering matches sec(z) at moderate alts).
            Assert.InRange(a30, 1.95, 2.05);
        }

        [Fact]
        public void Airmass_BelowHorizon_ReturnsLargeSentinel()
        {
            Assert.True(SkyBrightness.Airmass(-5.0) >= 99.0);
        }

        // ============================================================
        // PhaseAngleDegFromAgeDays -- synodic-cycle conversion
        // ============================================================

        [Theory]
        [InlineData(0.0,    180.0)]    // new moon
        [InlineData(7.38,    90.0)]    // first quarter
        [InlineData(14.77,    0.0)]    // full
        [InlineData(22.15,   90.0)]    // last quarter
        [InlineData(29.53,  180.0)]    // back to new
        public void PhaseAngleDegFromAgeDays_KnownPhases(double ageDays, double expectedAlpha)
        {
            double alpha = SkyBrightness.PhaseAngleDegFromAgeDays(ageDays);
            Assert.Equal(expectedAlpha, alpha, 0);  // 1° tolerance
        }

        // ============================================================
        // Bortle lookup tables -- monotonicity + boundaries
        // ============================================================

        [Fact]
        public void Bortle_ZenithMag_DescendingWithClass()
        {
            // V₀ should decrease (sky gets brighter -- lower magnitude) as Bortle
            // class climbs from 1 (excellent dark) to 9 (inner-city).
            for (int i = 1; i < 9; i++)
            {
                double a = Bortle.DefaultZenithMag(i);
                double b = Bortle.DefaultZenithMag(i + 1);
                Assert.True(a > b, $"V₀ at Bortle {i} ({a}) should exceed Bortle {i+1} ({b})");
            }
        }

        [Fact]
        public void Bortle_ExtinctionK_AscendingWithClass()
        {
            // k should increase (more atmospheric crud) as Bortle class climbs.
            for (int i = 1; i < 9; i++)
            {
                double a = Bortle.DefaultExtinctionK500(i);
                double b = Bortle.DefaultExtinctionK500(i + 1);
                Assert.True(a < b, $"k at Bortle {i} ({a}) should be less than Bortle {i+1} ({b})");
            }
        }

        [Fact]
        public void Bortle_ZenithMag_BoundariesPhysicallyPlausible()
        {
            // B1 should be near or above 21.5 (true dark site); B9 should be near or
            // below 17 (inner-city sky-glow).
            Assert.InRange(Bortle.DefaultZenithMag(1), 21.5, 22.5);
            Assert.InRange(Bortle.DefaultZenithMag(9), 16.0, 17.5);
        }

        [Fact]
        public void Bortle_ClampsOutOfRangeInputs()
        {
            // Out-of-range Bortle classes clamp to [1, 9].
            Assert.Equal(Bortle.DefaultZenithMag(1), Bortle.DefaultZenithMag(0));
            Assert.Equal(Bortle.DefaultZenithMag(1), Bortle.DefaultZenithMag(-5));
            Assert.Equal(Bortle.DefaultZenithMag(9), Bortle.DefaultZenithMag(10));
            Assert.Equal(Bortle.DefaultZenithMag(9), Bortle.DefaultZenithMag(99));
        }

        // ============================================================
        // Twilight -- ZenithBrightening shape + KsAt integration
        // ============================================================

        [Theory]
        [InlineData(-18.0)]
        [InlineData(-25.0)]
        [InlineData(-90.0)]
        public void Twilight_BelowAstronomicalNight_ReturnsZero(double sunAlt)
        {
            Assert.Equal(0.0, Twilight.ZenithBrightening(sunAlt), 12);
        }

        [Theory]
        [InlineData(0.0,  10.0)]
        [InlineData(5.0,  12.0)]
        [InlineData(45.0, 12.0)]
        public void Twilight_AtOrAboveHorizon_Saturates(double sunAlt, double expectedMin)
        {
            // Saturated daylight values; not physically literal but well-defined.
            double result = Twilight.ZenithBrightening(sunAlt);
            Assert.True(result >= expectedMin - 0.01,
                $"sun {sunAlt}° gave {result}, expected >= {expectedMin}");
        }

        [Fact]
        public void Twilight_MonotonicallyIncreasingFromMinus18()
        {
            double prev = Twilight.ZenithBrightening(-18.0);
            for (double alt = -17.0; alt <= 0.0; alt += 1.0)
            {
                double cur = Twilight.ZenithBrightening(alt);
                Assert.True(cur > prev, $"Twilight not monotonic at alt={alt}: prev={prev}, cur={cur}");
                prev = cur;
            }
        }

        [Theory]
        [InlineData(-12.0, 0.5,  2.0)]   // nautical: ~1.1 mag
        [InlineData(-6.0,  3.5,  5.5)]   // civil: ~4.4 mag
        public void Twilight_KnownPhaseWindows(double sunAlt, double minDelta, double maxDelta)
        {
            double delta = Twilight.ZenithBrightening(sunAlt);
            Assert.InRange(delta, minDelta, maxDelta);
        }

        [Fact]
        public void KsAt_TwilightAddsBrightness()
        {
            // Same target/moon geometry, twilight on vs off. Twilight version must
            // be brighter (lower magnitude).
            double withoutTwilight = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:  -10.0, moonAzDeg:  0.0,
                moonPhaseAngleDeg: 180.0,
                sunAltDeg:        -30.0,                // night
                extinctionKBand:   0.20,
                v0Mag: 21.5);
            double withCivilTwilight = SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 0.0,
                moonAltDeg:  -10.0, moonAzDeg:  0.0,
                moonPhaseAngleDeg: 180.0,
                sunAltDeg:        -6.0,                 // civil twilight
                extinctionKBand:   0.20,
                v0Mag: 21.5);
            Assert.True(withCivilTwilight < withoutTwilight - 1.0,
                $"civil-twilight sky ({withCivilTwilight}) should be >1 mag brighter than night ({withoutTwilight})");
        }

        [Fact]
        public void KsAt_TwilightDominatesAtCivilDusk()
        {
            // Civil twilight produces a very bright sky regardless of moon state.
            double sky = SkyBrightness.KsAt(
                targetAltDeg: 80.0, targetAzDeg: 0.0,    // near zenith
                moonAltDeg:  -10.0, moonAzDeg:  180.0,   // moon down
                moonPhaseAngleDeg: 180.0,                // moonless
                sunAltDeg:        -6.0,                  // civil twilight
                extinctionKBand:   0.20,
                v0Mag: 21.5);
            Assert.True(sky < 18.0, $"civil-twilight zenith sky should be < 18 mag/arcsec²; got {sky}");
        }

        [Fact]
        public void KsAt_NoTwilightAtAstronomicalNight()
        {
            // Sun at -18° is the threshold; should equal the moon-only path (sky = V₀
            // at zenith with no moon).
            double sky = SkyBrightness.KsAt(
                targetAltDeg: 89.0, targetAzDeg: 0.0,
                moonAltDeg:  -30.0, moonAzDeg:  180.0,
                moonPhaseAngleDeg: 180.0,
                sunAltDeg:        -18.0,                 // exactly at threshold
                extinctionKBand:   0.20,
                v0Mag: 21.5);
            Assert.InRange(sky, 21.4, 21.6);
        }
    }
}
