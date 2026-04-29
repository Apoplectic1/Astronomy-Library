using System;

namespace Astronomy.Core.Tests.Tests.Astrometry
{
    // Parity test inputs for the CoordinateSharp -> Meeus swap. Phase 1 uses these as
    // plausibility-only fixtures (NightCalculator returns valid windows when expected;
    // MoonSeparation outputs are in range). Phase 3 will tighten by snapshotting the
    // CoordinateSharp output and asserting the new AstroUtil-backed paths match within
    // tolerance (~30 arcsec moon Alt/Az; ~60 s twilight events; 0.005 illumination).
    //
    // All DateTimes are Kind=Utc so the test path is independent of the test machine's
    // TimeZoneInfo.Local for the MoonSeparation.ObserveAt calls (which take an explicit
    // utc parameter and pass utcOffset=0). NightCalculator currently does read
    // TimeZoneInfo.Local.GetUtcOffset(location.DateTime); that's a CoordinateSharp-era
    // quirk we accept for Phase 1, since the user's machine timezone is fixed across
    // Phases 1-3 of this swap.
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

        public static readonly Case[] All = new[]
        {
            // Penns Park, mid-spring, the user's home location at a typical observing time.
            new Case("PennsParkSpring",
                40.282835, true, 74.997369, true,
                new DateTime(2026, 4, 29, 21, 0, 0, DateTimeKind.Utc),
                expectValidNight: true),

            // Penns Park at the DST autumn-back transition. Validates the offset-recovery
            // path in NightCalculator.ToUtc that fixed the 2026-11-01 OptimalFloor spike
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
            // arguably "always-night" but CoordinateSharp may flag this as no astronomical
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
    }
}
