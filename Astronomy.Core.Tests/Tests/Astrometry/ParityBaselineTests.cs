using System;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Targets;
using Xunit;
using Xunit.Abstractions;

namespace Astronomy.Core.Tests.Tests.Astrometry
{
    // Phase 1 of the CoordinateSharp -> Meeus swap: plausibility-only baselines.
    //
    // These tests run today's CoordinateSharp-backed paths (NightCalculator.ComputeNight,
    // MoonSeparation.ObserveAt) over the ParityFixtures input table and assert each result
    // is in a sensible range. They establish that current behavior is well-defined for the
    // chosen inputs, and they pin the input table itself for Phase 3 (which will tighten
    // by snapshotting CoordinateSharp output and asserting the new AstroUtil-backed paths
    // match within tolerance).
    //
    // Tolerances Phase 3 will use:
    //   - moon Alt/Az: 30 arcsec
    //   - twilight events: 60 s
    //   - illumination fraction: 0.005
    public class ParityBaselineTests
    {
        private readonly ITestOutputHelper mLog;

        public ParityBaselineTests(ITestOutputHelper log)
        {
            mLog = log;
        }

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

            NightWindow night = NightCalculator.ComputeNight(loc);

            // Log the captured values for Phase 3 reference. xunit shows these on failure
            // and (with appendStandardOutputAndError=true in xunit.runner.json) on success.
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
        public void MoonSeparation_PlausibilityBaseline(string caseName)
        {
            ParityFixtures.Case c = FindCase(caseName);
            Location loc = MakeLocation(c);

            (double sep, double moonAlt) = MoonSeparation.ObserveAt(Target.Default, loc, c.UtcMoment);

            mLog.WriteLine($"[{c.Name}] sep={sep:F6} moonAlt={moonAlt:F6}");

            Assert.InRange(sep,     0.0, 180.0);
            Assert.InRange(moonAlt, -90.0, 90.0);
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
                horizon:      30.0,
                duration:     TimeSpan.FromHours(4),
                dateTime:     c.UtcMoment,           // Kind=Utc; NightCalculator's
                                                    // TimeZoneInfo.Local.GetUtcOffset still
                                                    // applies the local machine's offset.
                timeZoneInfo: TimeZoneInfo.Local);
        }

        private static string Fmt(DateTime dt)
            => dt == DateTime.MinValue ? "MinValue" : dt.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
    }
}
