using System;
using Astronomy.Core.Moon;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Correctness guard for MoonAvoidance: the Lorentzian formula must match the TS
    // reference (AstrometryUtils.GetMoonAvoidanceLorentzianSeparation) byte-for-byte
    // (modulo Math.Pow vs a*a), the relaxation zone must mirror MoonAvoidanceExpert's
    // ramping rules, and the Disabled profile must short-circuit every decision branch.
    public class MoonAvoidanceTests
    {
        // ============================================================
        // LorentzianRequiredSep -- pure formula tests
        // ============================================================

        [Fact]
        public void Lorentzian_AtFullMoon_ReturnsDistance()
        {
            // Age = 0.5 * 29.5305882 = 14.7652941 (full moon mid-point); the squared
            // term collapses to zero, returning 'distance' exactly.
            double age = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double result = MoonAvoidance.LorentzianRequiredSep(age, 60.0, 7.0);
            Assert.Equal(60.0, result, 12);
        }

        [Theory]
        [InlineData(60.0, 7.0)]
        [InlineData(120.0, 14.0)]
        [InlineData(45.0, 5.5)]
        public void Lorentzian_PlusOrMinusWidthOffFull_ReturnsHalfDistance(
            double distance, double width)
        {
            double full = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double earlier = MoonAvoidance.LorentzianRequiredSep(full - width, distance, width);
            double later   = MoonAvoidance.LorentzianRequiredSep(full + width, distance, width);
            Assert.Equal(distance / 2.0, earlier, 12);
            Assert.Equal(distance / 2.0, later, 12);
        }

        [Fact]
        public void Lorentzian_AtNewMoon_CollapsesToSmallFraction()
        {
            // Age = 0 (new moon); the squared term dominates, so result << distance.
            // Sanity check: ((0.5)/(7/29.53))^2 ≈ (2.108)^2 ≈ 4.444; 60 / (1 + 4.444) ≈ 11.02.
            double result = MoonAvoidance.LorentzianRequiredSep(0.0, 60.0, 7.0);
            Assert.True(result > 10.0 && result < 12.0, $"expected ~11, got {result}");
        }

        [Fact]
        public void Lorentzian_ZeroWidth_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => MoonAvoidance.LorentzianRequiredSep(10.0, 60.0, 0.0));
        }

        // Reference equivalence with the TS implementation. TS's formula at
        // AstrometryUtils.cs:136 is:
        //   distance / (1 + Math.Pow((0.5 - (age/29.5305882)) / (width/29.5305882), 2))
        // We compute identity at a sweep of (age, distance, width) triples; tolerance is
        // 1e-12 because a*a vs Math.Pow(_, 2) can differ at the ULP level on some FPUs.
        [Theory]
        [InlineData(0.0,        60.0,  7.0)]
        [InlineData(7.0,        60.0,  7.0)]
        [InlineData(14.7652941, 60.0,  7.0)]
        [InlineData(22.0,       60.0,  7.0)]
        [InlineData(29.5,       60.0,  7.0)]
        [InlineData(0.0,       120.0, 14.0)]
        [InlineData(14.7652941,120.0, 14.0)]
        [InlineData(7.0,       120.0, 14.0)]
        [InlineData(10.0,       30.0,  3.0)]
        [InlineData(15.0,      180.0, 21.0)]
        public void Lorentzian_MatchesTsReference(double age, double distance, double width)
        {
            double cycle = MoonAvoidance.DaysInLunarCycle;
            double a = (0.5 - (age / cycle)) / (width / cycle);
            double tsRef = distance / (1.0 + Math.Pow(a, 2));
            double ours = MoonAvoidance.LorentzianRequiredSep(age, distance, width);
            Assert.Equal(tsRef, ours, 12);
        }

        // ============================================================
        // RequiredSepWithRelax -- relaxation-zone tests
        // ============================================================

        [Fact]
        public void RequiredSepWithRelax_DisabledProfile_ReturnsZero()
        {
            double r = MoonAvoidance.RequiredSepWithRelax(
                14.7, 30.0, MoonAvoidanceProfile.Disabled);
            Assert.Equal(0.0, r);
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(30.0)]
        [InlineData(-45.0)]
        public void RequiredSepWithRelax_RelaxOff_AltDoesNotMatter(double moonAlt)
        {
            // Relaxation disabled: result is plain Lorentzian regardless of moonAlt.
            var profile = MoonAvoidanceProfile.Narrowband;
            double full = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double r = MoonAvoidance.RequiredSepWithRelax(full, moonAlt, profile);
            Assert.Equal(60.0, r, 12);
        }

        [Fact]
        public void RequiredSepWithRelax_RelaxOn_BelowMinAlt_ReturnsZero()
        {
            // Moon altitude below relaxMin -> avoidance off entirely.
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxMinAltDeg: -15.0, relaxMaxAltDeg: 5.0, relaxScale: 1.0);
            double r = MoonAvoidance.RequiredSepWithRelax(
                0.5 * MoonAvoidance.DaysInLunarCycle, -20.0, profile);
            Assert.Equal(0.0, r);
        }

        [Fact]
        public void RequiredSepWithRelax_RelaxOn_BelowMinAlt_ReturnsZero_EvenWithRelaxScaleZero()
        {
            // Even when RelaxScale = 0, the floor-cuts-off-avoidance rule still applies.
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxMinAltDeg: -15.0, relaxMaxAltDeg: 5.0, relaxScale: 0.0);
            double r = MoonAvoidance.RequiredSepWithRelax(
                0.5 * MoonAvoidance.DaysInLunarCycle, -20.0, profile);
            Assert.Equal(0.0, r);
        }

        [Fact]
        public void RequiredSepWithRelax_RelaxOn_AboveMaxAlt_FullLorentzian()
        {
            // Moon altitude above relaxMax -> full Lorentzian.
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxScale: 1.0);
            double full = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double r = MoonAvoidance.RequiredSepWithRelax(full, 30.0, profile);
            Assert.Equal(60.0, r, 12);
        }

        [Fact]
        public void RequiredSepWithRelax_RelaxOn_InZone_RampsLinearly()
        {
            // alt = -5, in zone [-15, +5]:
            //   distance += 2 * (-5 - 5) = -20  ->  distance = 60 + (-20) = 40
            //   width    *= (-5 - -15) / (5 - -15) = 10/20 = 0.5  ->  width = 7 * 0.5 = 3.5
            // At full moon (age = 0.5 * cycle), Lorentzian returns distance directly = 40.
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxMinAltDeg: -15.0, relaxMaxAltDeg: 5.0, relaxScale: 2.0);
            double full = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double r = MoonAvoidance.RequiredSepWithRelax(full, -5.0, profile);
            Assert.Equal(40.0, r, 10);
        }

        [Fact]
        public void RequiredSepWithRelax_RelaxOn_RelaxScaleZero_InZone_FullLorentzian()
        {
            // RelaxEnabled but RelaxScale = 0: matches TS's `RelaxScale > 0` gate -- no ramps.
            // Full Lorentzian applies above the floor. (The floor still kills avoidance below.)
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxMinAltDeg: -15.0, relaxMaxAltDeg: 5.0, relaxScale: 0.0);
            double full = 0.5 * MoonAvoidance.DaysInLunarCycle;
            double r = MoonAvoidance.RequiredSepWithRelax(full, -5.0, profile);
            Assert.Equal(60.0, r, 12);
        }

        [Fact]
        public void RequiredSepWithRelax_NullProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => MoonAvoidance.RequiredSepWithRelax(10.0, 30.0, null));
        }

        // ============================================================
        // IsRejected -- decision tests
        // ============================================================

        [Fact]
        public void IsRejected_DisabledProfile_AlwaysFalse()
        {
            // Even at zero separation under "full moon", Disabled rejects nothing.
            bool r = MoonAvoidance.IsRejected(
                0.0, 0.5 * MoonAvoidance.DaysInLunarCycle, 30.0,
                MoonAvoidanceProfile.Disabled);
            Assert.False(r);
        }

        [Fact]
        public void IsRejected_NarrowbandFullMoonZeroSep_True()
        {
            // 60° required at full moon; actual = 0° -> reject.
            bool r = MoonAvoidance.IsRejected(
                0.0, 0.5 * MoonAvoidance.DaysInLunarCycle, 30.0,
                MoonAvoidanceProfile.Narrowband);
            Assert.True(r);
        }

        [Fact]
        public void IsRejected_NarrowbandFullMoon90Sep_False()
        {
            // 60° required at full moon; actual = 90° -> accept.
            bool r = MoonAvoidance.IsRejected(
                90.0, 0.5 * MoonAvoidance.DaysInLunarCycle, 30.0,
                MoonAvoidanceProfile.Narrowband);
            Assert.False(r);
        }

        [Fact]
        public void IsRejected_NewMoon_AcceptsSmallSeparations()
        {
            // At new moon, NB-profile required collapses to ~11° -> 30° actual passes.
            bool r = MoonAvoidance.IsRejected(
                30.0, 0.0, 30.0, MoonAvoidanceProfile.Narrowband);
            Assert.False(r);
        }

        [Fact]
        public void IsRejected_RelaxOn_BelowMinAlt_AcceptsAtZeroSep()
        {
            // Moon below the relaxation floor -> avoidance off; accept everything.
            var profile = MoonAvoidanceProfile.Custom(
                separationDeg: 60.0, widthDays: 7.0,
                relaxEnabled: true, relaxScale: 1.0);
            bool r = MoonAvoidance.IsRejected(
                0.0, 0.5 * MoonAvoidance.DaysInLunarCycle, -20.0, profile);
            Assert.False(r);
        }

        [Fact]
        public void IsRejected_NullProfile_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => MoonAvoidance.IsRejected(30.0, 14.7, 10.0, null));
        }

        // ============================================================
        // MoonAvoidanceProfile -- POCO tests
        // ============================================================

        [Fact]
        public void Profile_Disabled_HasEnabledFalse()
        {
            Assert.False(MoonAvoidanceProfile.Disabled.Enabled);
        }

        [Fact]
        public void Profile_Narrowband_60_7_RelaxOff()
        {
            var p = MoonAvoidanceProfile.Narrowband;
            Assert.True(p.Enabled);
            Assert.Equal(60.0, p.SeparationDeg);
            Assert.Equal(7.0, p.WidthDays);
            Assert.False(p.RelaxEnabled);
        }

        [Fact]
        public void Profile_Broadband_120_14_RelaxOff()
        {
            var p = MoonAvoidanceProfile.Broadband;
            Assert.True(p.Enabled);
            Assert.Equal(120.0, p.SeparationDeg);
            Assert.Equal(14.0, p.WidthDays);
            Assert.False(p.RelaxEnabled);
        }

        [Fact]
        public void Profile_Custom_PreservesArgs()
        {
            var p = MoonAvoidanceProfile.Custom(
                separationDeg: 80.0, widthDays: 9.0,
                relaxEnabled: true, relaxMinAltDeg: -10.0, relaxMaxAltDeg: 8.0, relaxScale: 3.0);
            Assert.True(p.Enabled);
            Assert.Equal(80.0, p.SeparationDeg);
            Assert.Equal(9.0, p.WidthDays);
            Assert.True(p.RelaxEnabled);
            Assert.Equal(-10.0, p.RelaxMinAltDeg);
            Assert.Equal(8.0, p.RelaxMaxAltDeg);
            Assert.Equal(3.0, p.RelaxScale);
        }

        [Fact]
        public void Profile_With_OverridesOnlySpecifiedFields()
        {
            var p = MoonAvoidanceProfile.Narrowband.With(separationDeg: 75.0);
            Assert.Equal(75.0, p.SeparationDeg);
            Assert.Equal(7.0, p.WidthDays); // unchanged
            Assert.True(p.Enabled);          // unchanged
        }
    }
}
