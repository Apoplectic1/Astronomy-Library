using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Xunit;
using Xunit.Abstractions;

namespace Astronomy.Core.Tests.Tests.Astrometry
{
    // Phase 3 of the post-CoordinateSharp parity contract.
    //
    // Two layers of tests run over the ParityFixtures.All input table:
    //
    //   1. PlausibilityBaseline -- in-range / sentinel guards (IsValid, Kind=Utc,
    //      dusk < dawn, illumination in [0, 1], sep / moonAlt finite). Catches the
    //      "produces garbage" regression class.
    //
    //   2. MatchesBaseline -- assert the computed values match the frozen snapshots
    //      in ParityFixtures.Baselines within the documented tolerances:
    //        twilight events: 60 s             illumination:   0.005
    //        moon altitude:   30 arcsec        target-moon sep: 60 arcsec (derived)
    //      Catches the "drifts within ostensibly-valid range" regression class.
    //
    // Baselines were captured on 2026-05-18 from a verified-correct build. To
    // regenerate after a deliberate behaviour change, unskip
    // _DumpBaselinesForRegeneration, run it, copy the emitted dictionary entries
    // into ParityFixtures.Baselines, and re-apply the Skip attribute.
    public class ParityBaselineTests
    {
        private readonly ITestOutputHelper mLog;

        public ParityBaselineTests(ITestOutputHelper log)
        {
            mLog = log;
        }

        // Tolerance constants. Keep co-located so a deliberate widening is one edit.
        private const double DuskDawnToleranceSeconds  = 60.0;
        private const double IlluminationTolerance     = 0.005;
        private const double MoonAltToleranceArcsec    = 30.0;
        private const double SeparationToleranceArcsec = 60.0;

        public static TheoryData<string> CaseNames()
        {
            var data = new TheoryData<string>();
            foreach (var c in ParityFixtures.All)
                data.Add(c.Name);
            return data;
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void NightCalculator_PlausibilityBaseline(string caseName)
        {
            ParityFixtures.Case c = FindCase(caseName);
            Location loc = MakeLocation(c);

            NightWindow night = NightCalculator.ComputeNight(loc, c.UtcMoment);

            // Log captured values so the dump for baseline regeneration is recoverable
            // from a normal test run too (xunit shows these on failure and -- with
            // appendStandardOutputAndError=true in xunit.runner.json -- on success).
            mLog.WriteLine($"[{c.Name}] dusk={Fmt(night.AstronomicalDusk)} dawn={Fmt(night.AstronomicalDawn)} illum={night.LunarIlluminationFraction:F6}");

            if (c.ExpectValidNight)
            {
                Assert.True(night.IsValid,
                    $"Expected valid night window for {c.Name} but IsValid=false (dusk={Fmt(night.AstronomicalDusk)}, dawn={Fmt(night.AstronomicalDawn)})");
                Assert.Equal(DateTimeKind.Utc, night.AstronomicalDusk.Kind);
                Assert.Equal(DateTimeKind.Utc, night.AstronomicalDawn.Kind);
                Assert.True(night.AstronomicalDusk < night.AstronomicalDawn,
                    $"Expected dusk before dawn for {c.Name}: dusk={Fmt(night.AstronomicalDusk)} dawn={Fmt(night.AstronomicalDawn)}");
            }
            else
            {
                Assert.False(night.IsValid,
                    $"Expected polar (no astronomical night) for {c.Name} but got valid window dusk={Fmt(night.AstronomicalDusk)} dawn={Fmt(night.AstronomicalDawn)}");
            }

            Assert.InRange(night.LunarIlluminationFraction, 0.0, 1.0);
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void NightCalculator_MatchesBaseline(string caseName)
        {
            ParityFixtures.Case c = FindCase(caseName);
            ParityFixtures.BaselineSnapshot bl = ParityFixtures.Baselines[caseName];
            Location loc = MakeLocation(c);
            NightWindow night = NightCalculator.ComputeNight(loc, c.UtcMoment);

            // Dusk/Dawn baseline only applies to non-polar cases. For polar cases the
            // PlausibilityBaseline already pins IsValid=false; the baseline's MinValue
            // sentinel matches that and there's nothing further to assert on the times.
            if (c.ExpectValidNight)
            {
                double duskDeltaS = Math.Abs((night.AstronomicalDusk - bl.Dusk).TotalSeconds);
                double dawnDeltaS = Math.Abs((night.AstronomicalDawn - bl.Dawn).TotalSeconds);

                Assert.True(duskDeltaS < DuskDawnToleranceSeconds,
                    $"[{c.Name}] dusk drift {duskDeltaS:F2}s exceeds {DuskDawnToleranceSeconds}s budget (got {Fmt(night.AstronomicalDusk)}, baseline {Fmt(bl.Dusk)})");
                Assert.True(dawnDeltaS < DuskDawnToleranceSeconds,
                    $"[{c.Name}] dawn drift {dawnDeltaS:F2}s exceeds {DuskDawnToleranceSeconds}s budget (got {Fmt(night.AstronomicalDawn)}, baseline {Fmt(bl.Dawn)})");
            }

            double illumDelta = Math.Abs(night.LunarIlluminationFraction - bl.Illumination);
            Assert.True(illumDelta < IlluminationTolerance,
                $"[{c.Name}] illumination drift {illumDelta:F6} exceeds {IlluminationTolerance} budget (got {night.LunarIlluminationFraction:F6}, baseline {bl.Illumination:F6})");
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void MoonSeparation_PlausibilityBaseline(string caseName)
        {
            ParityFixtures.Case c = FindCase(caseName);
            Location loc = MakeLocation(c);

            (double sep, double moonAlt, _) = MoonSeparation.ObserveAt(Target.Default, loc, c.UtcMoment);

            mLog.WriteLine($"[{c.Name}] sep={sep:F6} moonAlt={moonAlt:F6}");

            Assert.InRange(sep,     0.0, 180.0);
            Assert.InRange(moonAlt, -90.0, 90.0);
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void MoonSeparation_MatchesBaseline(string caseName)
        {
            ParityFixtures.Case c = FindCase(caseName);
            ParityFixtures.BaselineSnapshot bl = ParityFixtures.Baselines[caseName];
            Location loc = MakeLocation(c);

            (double sep, double moonAlt, _) = MoonSeparation.ObserveAt(Target.Default, loc, c.UtcMoment);

            double sepDeltaArcsec     = Math.Abs(sep     - bl.Separation) * 3600.0;
            double moonAltDeltaArcsec = Math.Abs(moonAlt - bl.MoonAlt)    * 3600.0;

            Assert.True(sepDeltaArcsec < SeparationToleranceArcsec,
                $"[{c.Name}] separation drift {sepDeltaArcsec:F2} arcsec exceeds {SeparationToleranceArcsec} arcsec budget (got {sep:F6}, baseline {bl.Separation:F6})");
            Assert.True(moonAltDeltaArcsec < MoonAltToleranceArcsec,
                $"[{c.Name}] moonAlt drift {moonAltDeltaArcsec:F2} arcsec exceeds {MoonAltToleranceArcsec} arcsec budget (got {moonAlt:F6}, baseline {bl.MoonAlt:F6})");
        }

        // Manual regeneration tool for ParityFixtures.Baselines after a deliberate
        // behaviour change. To run:
        //   1. Remove the Skip from the [Fact] attribute below.
        //   2. dotnet test ... --filter "FullyQualifiedName~_DumpBaselinesForRegeneration"
        //                      --logger "console;verbosity=detailed"
        //   3. Copy the emitted dictionary entries into ParityFixtures.Baselines.
        //   4. Re-apply the Skip attribute.
        [Fact(Skip = "Manual regeneration tool; unskip per the comment above to re-snapshot.")]
        public void _DumpBaselinesForRegeneration()
        {
            mLog.WriteLine("// Paste below into ParityFixtures.Baselines:");
            foreach (var c in ParityFixtures.All)
            {
                Location loc = MakeLocation(c);
                NightWindow night = NightCalculator.ComputeNight(loc, c.UtcMoment);
                (double sep, double moonAlt, _) = MoonSeparation.ObserveAt(Target.Default, loc, c.UtcMoment);

                mLog.WriteLine($"[\"{c.Name}\"] = new BaselineSnapshot(");
                mLog.WriteLine($"    Dusk:        {CSharpDateTime(night.AstronomicalDusk)},");
                mLog.WriteLine($"    Dawn:        {CSharpDateTime(night.AstronomicalDawn)},");
                mLog.WriteLine($"    Illumination: {night.LunarIlluminationFraction:F6},");
                mLog.WriteLine($"    Separation:   {sep:F6},");
                mLog.WriteLine($"    MoonAlt:      {moonAlt:F6}),");
            }
        }

        // -------- helpers --------

        private static ParityFixtures.Case FindCase(string name)
        {
            foreach (var c in ParityFixtures.All)
                if (c.Name == name) return c;
            throw new InvalidOperationException($"Unknown parity case '{name}'.");
        }

        private static Location MakeLocation(ParityFixtures.Case c)
        {
            return new Location(
                name:         c.Name,
                latitude:     c.Latitude,  north: c.North,
                longitude:    c.Longitude, west:  c.West,
                timeZoneInfo: TimeZoneInfo.Utc);     // NightCalculator (post-Meeus) reads no
                                                     // TZ state, so this is machine-portable.
        }

        private static string Fmt(DateTime dt)
            => dt == DateTime.MinValue ? "MinValue" : dt.ToString("yyyy-MM-dd HH:mm:ss") + "Z";

        private static string CSharpDateTime(DateTime dt)
            => dt == DateTime.MinValue
                ? "DateTime.MinValue"
                : $"new DateTime({dt.Year}, {dt.Month}, {dt.Day}, {dt.Hour}, {dt.Minute}, {dt.Second}, DateTimeKind.Utc)";
    }
}
