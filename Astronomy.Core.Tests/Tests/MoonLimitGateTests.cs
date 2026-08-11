using System;
using Astronomy.Core;
using Astronomy.Core.Brightness;
using Astronomy.Core.Horizons;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // The K-S Δmag moon gate: KsMoonDeltaMag properties, the MoonLimitProfile POCO
    // contract, gate monotonicity through the public ResolveCandidates surface, and
    // the A-anchor calibration pins that make the shipped Narrowband/Broadband
    // tolerance defaults reproducible. Scenario source: the moon-brightness-gate spec
    // (openspec/changes/ks-dmag-moon-gate).
    public class MoonLimitGateTests
    {
        // Reference site for the calibration pins: Bortle 5, k500 = 0.28 (Location
        // defaults), sun at -18 (astronomical night, twilight term zero).
        private const double RefBortleV0 = 21.5;   // Bortle.DefaultZenithMag(5) sanity anchor
        private const double RefK500 = 0.28;
        private const double AstroNightSunAlt = -18.0;

        // Azimuth difference that yields angular separation sepDeg between two points
        // at altitudes alt1/alt2 (same closed form the 2026-07-24 calibration used).
        // Returns null when the geometry is infeasible.
        private static double? AzimuthDiffForSeparation(double alt1, double alt2, double sepDeg)
        {
            const double D = Math.PI / 180.0;
            double cosDaz = (Math.Cos(sepDeg * D) - Math.Sin(alt1 * D) * Math.Sin(alt2 * D))
                          / (Math.Cos(alt1 * D) * Math.Cos(alt2 * D));
            if (cosDaz < -1.0 || cosDaz > 1.0) return null;
            return Math.Acos(cosDaz) / D;
        }

        private static double DeltaAt(
            double targetAlt, double moonAlt, double sepDeg, double ageDays, double centerNm)
        {
            double? dAz = AzimuthDiffForSeparation(targetAlt, moonAlt, sepDeg);
            Assert.True(dAz.HasValue, $"infeasible geometry: alts {targetAlt}/{moonAlt} sep {sepDeg}");

            double phase = SkyBrightness.PhaseAngleDegFromAgeDays(ageDays);
            double kBand = SkyBrightness.ScaleK(RefK500, centerNm);
            double v0 = Bortle.DefaultZenithMag(5);

            return SkyBrightness.KsMoonDeltaMag(
                targetAlt, 180.0, moonAlt, 180.0 - dAz.Value,
                phase, AstroNightSunAlt, kBand, v0, centerNm);
        }

        private static double FullMoonAge => 0.5 * LunarAge.SynodicMonthDays;

        // ---- KsMoonDeltaMag properties ----

        [Fact]
        public void DeltaMag_MoonBelowApparentHorizon_IsZero()
        {
            double d = SkyBrightness.KsMoonDeltaMag(
                45.0, 180.0, moonAltDeg: -0.5, moonAzDeg: 90.0,
                moonPhaseAngleDeg: 0.0, sunAltDeg: AstroNightSunAlt,
                extinctionKBand: RefK500, v0Mag: RefBortleV0, centerNm: 540.0);
            Assert.Equal(0.0, d);
        }

        [Fact]
        public void DeltaMag_NewMoon_IsNegligible()
        {
            // Phase angle 180 = new moon: iStar collapses; Δ is far below any sane tolerance.
            double d = SkyBrightness.KsMoonDeltaMag(
                45.0, 180.0, 30.0, 120.0,
                moonPhaseAngleDeg: 180.0, sunAltDeg: AstroNightSunAlt,
                extinctionKBand: RefK500, v0Mag: RefBortleV0, centerNm: 540.0);
            Assert.InRange(d, 0.0, 0.05);
        }

        [Fact]
        public void DeltaMag_FullMoonNearTarget_RejectsAnySaneTolerance()
        {
            // Spec: near-moon minutes reject on magnitude, not via a special case.
            double d = DeltaAt(targetAlt: 45.0, moonAlt: 50.0, sepDeg: 8.0,
                               ageDays: FullMoonAge, centerNm: 540.0);
            Assert.True(d > 3.0, $"expected Δmag > 3 near a full moon, got {d:F2}");
        }

        [Fact]
        public void DeltaMag_TargetBelowHorizon_IsNaN()
        {
            double d = SkyBrightness.KsMoonDeltaMag(
                -1.0, 180.0, 45.0, 90.0, 0.0, AstroNightSunAlt, RefK500, RefBortleV0, 540.0);
            Assert.True(double.IsNaN(d));
        }

        [Fact]
        public void DeltaMag_EqualsKsAtDifference_AtAnyBandwidth()
        {
            // The decomposed Δ must equal the difference of two full KsAt evaluations,
            // and the bandwidth must cancel: same Δ whether KsAt runs at 3 nm or 300 nm.
            double tAlt = 40.0, tAz = 180.0, mAlt = 35.0, mAz = 100.0;
            double phase = 30.0, sun = AstroNightSunAlt, k = 0.21, v0 = 20.5, center = 540.0;

            double delta = SkyBrightness.KsMoonDeltaMag(tAlt, tAz, mAlt, mAz, phase, sun, k, v0, center);

            foreach (double bw in new[] { 3.0, 85.0, 300.0 })
            {
                double withMoon = SkyBrightness.KsAt(tAlt, tAz, mAlt, mAz, phase, sun, k, v0, bw, center);
                double noMoon   = SkyBrightness.KsAt(tAlt, tAz, -5.0, mAz, phase, sun, k, v0, bw, center);
                Assert.Equal(noMoon - withMoon, delta, precision: 10);
            }
        }

        [Fact]
        public void DeltaMag_BrighterTwilight_ShrinksDelta()
        {
            // Spec: twilight sits in the baseline and dilutes the moon's relative impact.
            double atNight    = DeltaAt(40.0, 35.0, 60.0, FullMoonAge, 540.0);

            double? dAz = AzimuthDiffForSeparation(40.0, 35.0, 60.0);
            double inTwilight = SkyBrightness.KsMoonDeltaMag(
                40.0, 180.0, 35.0, 180.0 - dAz!.Value,
                SkyBrightness.PhaseAngleDegFromAgeDays(FullMoonAge),
                sunAltDeg: -13.0,
                SkyBrightness.ScaleK(RefK500, 540.0), Bortle.DefaultZenithMag(5), 540.0);

            Assert.True(inTwilight < atNight,
                $"twilight Δ ({inTwilight:F3}) should be below astronomical-night Δ ({atNight:F3})");
        }

        [Fact]
        public void DeltaMag_BrighterSite_ShrinksDelta()
        {
            // Site params flow from the caller (the gate reads Location): a Bortle 9 sky
            // is already bright, so the same moon adds fewer magnitudes than at Bortle 3.
            double? dAz = AzimuthDiffForSeparation(40.0, 35.0, 60.0);
            double phase = SkyBrightness.PhaseAngleDegFromAgeDays(FullMoonAge);

            double Delta(int bortle) => SkyBrightness.KsMoonDeltaMag(
                40.0, 180.0, 35.0, 180.0 - dAz!.Value, phase, AstroNightSunAlt,
                SkyBrightness.ScaleK(Bortle.DefaultExtinctionK500(bortle), 540.0),
                Bortle.DefaultZenithMag(bortle), 540.0);

            Assert.True(Delta(9) < Delta(3),
                "urban sky should dilute the moon's relative contribution");
        }

        // ---- Calibration pins (A-anchors for the shipped defaults) ----

        [Fact]
        public void Calibration_NarrowbandFullMoonBoundary_SitsAboveShippedTolerance()
        {
            // The classic NB rule (60° separation at full moon) implies a tolerance of
            // ~1.6 mag (sky ×4.4) — 2026-07-24 calibration measured 1.631 at
            // (moon 45°, target 40°). The shipped Narrowband default (1.0) anchors at the
            // Lorentzian's CYCLE MEDIAN instead, so near full moon the gate is
            // deliberately stricter than the classic rule. This pin records the implied
            // full-moon tolerance so the relationship stays reproducible.
            double d = DeltaAt(targetAlt: 40.0, moonAlt: 45.0, sepDeg: 60.0,
                               ageDays: FullMoonAge, centerNm: 656.0);
            Assert.InRange(d, 1.45, 1.80);
            Assert.True(MoonLimitProfile.Narrowband.ToleranceMag < d,
                "shipped NB default should be stricter than the classic full-moon rule");
        }

        [Fact]
        public void Calibration_NarrowbandMedianAnchor_GibbousLorentzianBoundary()
        {
            // The shipped NB default (1.0) is the Lorentzian boundary's cycle-median Δmag;
            // the waxing-gibbous boundary (age full−3.5, required sep 48°) sits right on
            // it — 2026-07-24 calibration measured ~1.02 at (moon 45°, target 40°).
            double d = DeltaAt(targetAlt: 40.0, moonAlt: 45.0, sepDeg: 48.0,
                               ageDays: FullMoonAge - 3.5, centerNm: 656.0);
            Assert.InRange(d, 0.85, 1.15);
        }

        [Fact]
        public void Calibration_BroadbandAnchor_HalfMoonLorentzianBoundary()
        {
            // BB anchors at the half-moon boundary (96° for the 120°/14d Lorentzian):
            // the full-moon boundary sat at Δmag ≈ 1.7–2.0 (≈ 5–6× integration cost) and
            // failed the physics sanity check. 2026-07-24 calibration measured 0.321 at
            // (moon 15°, target 25°); shipped Broadband.ToleranceMag = 0.30.
            double d = DeltaAt(targetAlt: 25.0, moonAlt: 15.0, sepDeg: 96.0,
                               ageDays: FullMoonAge - 7.0, centerNm: 540.0);
            Assert.InRange(d, 0.20, 0.45);
        }

        // ---- Profile POCO contract ----

        [Fact]
        public void Profile_With_InheritsUnspecified()
        {
            var p = MoonLimitProfile.Narrowband.With(toleranceMag: 0.5);
            Assert.True(p.Enabled);
            Assert.Equal(0.5, p.ToleranceMag);
            Assert.Equal(656.0, p.CenterNm);
        }

        [Fact]
        public void Profile_Singletons_CarryShippedDefaults()
        {
            Assert.False(MoonLimitProfile.Disabled.Enabled);
            Assert.Equal(1.0, MoonLimitProfile.Narrowband.ToleranceMag);
            Assert.Equal(656.0, MoonLimitProfile.Narrowband.CenterNm);
            Assert.Equal(0.30, MoonLimitProfile.Broadband.ToleranceMag);
            Assert.Equal(SkyBrightness.VBandCenterNm, MoonLimitProfile.Broadband.CenterNm);
        }

        // ---- Gate behavior through the public surface ----

        [Fact]
        public void ResolveCandidates_ToleranceIsMonotone()
        {
            // A stricter tolerance can only shrink the accepted time. Sweep a lunar month
            // of nights so at least some have the moon up during the window.
            var loc = TestLocations.PennsPark;
            var horizon = new ScalarHorizonProfile(20.0);

            var strict  = MoonLimitProfile.Custom(toleranceMag: 0.1, centerNm: 540.0);
            var relaxed = MoonLimitProfile.Custom(toleranceMag: 1.5, centerNm: 540.0);

            static double TotalHours(System.Collections.Generic.IReadOnlyList<Astronomy.Core.Time.UtcInterval> w)
            {
                double h = 0;
                foreach (var (s, e) in w) h += (e - s).TotalHours;
                return h;
            }

            for (int day = 0; day < 28; day += 4)
            {
                var seed = new DateTime(2026, 11, 1, 22, 0, 0, DateTimeKind.Utc).AddDays(day);
                var night = NightCalculator.ComputeNight(loc, seed);

                double hStrict  = TotalHours(BestSession.ResolveCandidates(
                    Target.Default, loc, night, horizon, strict));
                double hRelaxed = TotalHours(BestSession.ResolveCandidates(
                    Target.Default, loc, night, horizon, relaxed));

                Assert.True(hStrict <= hRelaxed + 1e-9,
                    $"day {day}: strict tolerance accepted more time ({hStrict:F2}h) than relaxed ({hRelaxed:F2}h)");
            }
        }

        [Fact]
        public void ResolveCandidates_HugeTolerance_EqualsMoonBlindWindows()
        {
            // A tolerance no real sky can exceed accepts everything: the gate output must
            // equal the moon-blind visibility windows exactly.
            var loc = TestLocations.PennsPark;
            var horizon = new ScalarHorizonProfile(20.0);
            var seed = new DateTime(2026, 11, 4, 22, 0, 0, DateTimeKind.Utc); // near full moon
            var night = NightCalculator.ComputeNight(loc, seed);

            var blind = BestSession.ResolveCandidates(Target.Default, loc, night, horizon, profile: null);
            var gated = BestSession.ResolveCandidates(Target.Default, loc, night, horizon,
                MoonLimitProfile.Custom(toleranceMag: 99.0, centerNm: 540.0));

            Assert.Equal(blind.Count, gated.Count);
            for (int i = 0; i < blind.Count; i++)
            {
                Assert.Equal(blind[i].Start, gated[i].Start);
                Assert.Equal(blind[i].End, gated[i].End);
            }
        }
    }
}
