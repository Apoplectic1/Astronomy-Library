using System;
using System.Collections.Generic;

namespace Astronomy.Core.Tests.Tests.Astrometry
{
    // Parity test inputs and frozen baseline snapshots for the post-CoordinateSharp Meeus
    // surfaces. Baselines were captured on 2026-05-18 from a build the user had externally
    // verified as correct; downstream regressions (Meeus formula edits, refactors of the
    // sign-resolution preamble, etc.) show up here as a clean per-case failure with the
    // delta quantified instead of as plausibility-only divergence.
    //
    // Tolerances mirror the contract documented in the 2026-05-18 review:
    //   - twilight events (AstronomicalDusk, AstronomicalDawn): 60 s
    //   - moon altitude:                                        30 arcsec
    //   - target-moon separation:                               60 arcsec (derived)
    //   - lunar illumination fraction:                          0.005
    //
    // All DateTimes are Kind=Utc so the test path is independent of the test machine's
    // TimeZoneInfo.Local. NightCalculator (post-2249834, Meeus-backed) no longer reads
    // TimeZoneInfo.Local for any computation, so the captured baselines are
    // machine-independent.
    public static class ParityFixtures
    {
        // Whether each case's night window is expected to be valid. Polar-day / polar-night
        // cases (sun never reaches -18 degrees) are flagged so the test asserts
        // NightWindow.IsValid is false instead of looking for dusk < dawn.
        public sealed class Case
        {
            public string   Name      { get; }
            public double   Latitude  { get; }   // non-negative magnitude
            public bool     North     { get; }
            public double   Longitude { get; }   // non-negative magnitude
            public bool     West      { get; }
            public DateTime UtcMoment { get; }   // Kind=Utc
            public bool     ExpectValidNight { get; }

            public Case(string name, double lat, bool north, double lon, bool west,
                        DateTime utc, bool expectValidNight)
            {
                Name = name;
                Latitude = lat;
                North = north;
                Longitude = lon;
                West = west;
                UtcMoment = utc;
                ExpectValidNight = expectValidNight;
            }
        }

        // Frozen baseline outputs for each case. Dusk / Dawn are DateTime.MinValue for
        // polar-day / polar-night cases where NightCalculator returns the sentinel
        // (no astronomical night). Illumination / Separation / MoonAlt are real values
        // even for polar cases (lunar position computes regardless of sun visibility).
        public sealed record BaselineSnapshot(
            DateTime Dusk,
            DateTime Dawn,
            double Illumination,
            double Separation,
            double MoonAlt);

        public static readonly Case[] All = new[]
        {
            // Penns Park, mid-spring, the user's home location at a typical observing time.
            new Case("PennsParkSpring",
                40.282835, true, 74.997369, true,
                new DateTime(2026, 4, 29, 21, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Penns Park at the DST autumn-back transition. Validates the offset-recovery
            // path in NightCalculator that fixed the 2026-11-01 night-window spike
            // (Library commit 5eb3d0b).
            new Case("PennsParkDstFall",
                40.282835, true, 74.997369, true,
                new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Penns Park at the DST spring-forward transition. Companion DST trap.
            new Case("PennsParkDstSpring",
                40.282835, true, 74.997369, true,
                new DateTime(2027, 3, 14, 7, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Penns Park at summer solstice (long-day, short-night).
            new Case("PennsParkSummerSolstice",
                40.282835, true, 74.997369, true,
                new DateTime(2026, 6, 21, 4, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Equator at June solstice. Equal day/night; both well-defined.
            new Case("EquatorSolstice",
                0.0, true, 0.0, false,
                new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // 65 degrees N (Reykjavik-ish) at June solstice. Sun never reaches -18 degrees;
            // astronomical night does not exist. NightWindow.IsValid is expected false.
            new Case("ReykjavikPolarDay",
                65.0, true, 18.0, true,
                new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc),
                expectValidNight: false),

            // 65 degrees S (Antarctic edge) at December solstice. Sun never rises;
            // arguably "always-night" but the calculator flags this as no astronomical
            // night-window. Expect IsValid=false either way.
            new Case("AntarcticPolarNight",
                65.0, false, 30.0, false,
                new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc),
                expectValidNight: false),

            // Sydney, mid-southern winter. Standard southern-hemisphere observing site.
            new Case("Sydney",
                33.87, false, 151.21, false,
                new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Tokyo, mid-winter. East-Asia evening near new year.
            new Case("Tokyo",
                35.68, true, 139.69, false,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),
        };

        // Frozen 2026-05-18 baselines. To regenerate after a deliberate behaviour change,
        // unskip ParityBaselineTests._DumpBaselinesForRegeneration, run it, and paste the
        // emitted entries here.
        public static readonly IReadOnlyDictionary<string, BaselineSnapshot> Baselines =
            new Dictionary<string, BaselineSnapshot>
            {
                ["PennsParkSpring"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 4, 30, 1, 36, 57, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 4, 30, 8, 17,  6, DateTimeKind.Utc),
                    Illumination: 0.966591,
                    Separation:   149.268582,
                    MoonAlt:      -13.779190),

                ["PennsParkDstFall"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 10, 31, 23, 30,  1, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 11,  1,  9, 57, 35, DateTimeKind.Utc),
                    Illumination: 0.569414,
                    Separation:   92.236811,
                    MoonAlt:      29.540646),

                ["PennsParkDstSpring"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2027, 3, 14, 0, 35,  9, DateTimeKind.Utc),
                    Dawn:        new DateTime(2027, 3, 14, 9, 42, 57, DateTimeKind.Utc),
                    Illumination: 0.346976,
                    Separation:   45.016701,
                    MoonAlt:      -14.419087),

                ["PennsParkSummerSolstice"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 6, 21, 2, 37, 42, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 6, 21, 7, 25, 48, DateTimeKind.Utc),
                    Illumination: 0.423415,
                    Separation:   133.386305,
                    MoonAlt:      5.432167),

                ["EquatorSolstice"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 6, 21, 19, 20, 37, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 6, 22,  4, 43, 15, DateTimeKind.Utc),
                    Illumination: 0.458234,
                    Separation:   136.517469,
                    MoonAlt:      3.954612),

                ["ReykjavikPolarDay"] = new BaselineSnapshot(
                    Dusk:        DateTime.MinValue,
                    Dawn:        DateTime.MinValue,
                    Illumination: 0.405989,
                    Separation:   132.065492,
                    MoonAlt:      5.179448),

                ["AntarcticPolarNight"] = new BaselineSnapshot(
                    Dusk:        DateTime.MinValue,
                    Dawn:        DateTime.MinValue,
                    Illumination: 0.902941,
                    Separation:   36.848226,
                    MoonAlt:      -30.425482),

                ["Sydney"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 7, 15,  8, 32, 21, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 7, 15, 19, 29, 51, DateTimeKind.Utc),
                    Illumination: 0.017925,
                    Separation:   96.966355,
                    MoonAlt:      -47.280454),

                ["Tokyo"] = new BaselineSnapshot(
                    Dusk:        new DateTime(2026, 1, 15,  9, 20, 41, DateTimeKind.Utc),
                    Dawn:        new DateTime(2026, 1, 15, 20, 20, 28, DateTimeKind.Utc),
                    Illumination: 0.104254,
                    Separation:   125.610192,
                    MoonAlt:      -82.233994),
            };
    }
}
