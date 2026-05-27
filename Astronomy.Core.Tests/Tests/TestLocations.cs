using System;
using System.Collections.Generic;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Tests.Tests
{
    // Test-only location fixtures with stable, hand-picked coordinates so test assertions
    // ("M31 transits at ~89° here", "M42 peaks at ~44°", etc.) keep their algorithmic
    // claims even after Location.Default's public-safe scrub. Centralised so adding a
    // fixture for a new fact style is one edit instead of dozens.
    //
    // The non-PennsPark fixtures are diverse-geometry coverage for "this property
    // should hold at any location" [Theory] tests (see All() below): two hemispheres,
    // an equator-degenerate latitude, and a polar fringe.
    //
    // Shape: static readonly fields (not `=> new Location(...)` properties). Location
    // is immutable so sharing a single instance per fixture is safe, and downstream
    // callers (TargetPlanner cache tests) rely on reference identity for dict-key
    // lookups + the cache's stale-publish discard, which the per-access-new property
    // shape silently breaks.
    internal static class TestLocations
    {
        // The historical Location.Default site -- US east coast, mid-latitude N, suburban
        // Bortle 5. Most facts in this suite are anchored to these coordinates.
        public static readonly Location PennsPark = new Location(
            name:         "Penns Park",
            latitude:     40.282835, north: true,
            longitude:    74.997369, west:  true,
            timeZoneInfo: TimeZoneInfo.Local,
            elevation:    80.67,
            bortleClass:  5,
            extinctionK:  0.28);

        // Sydney Opera House. Southern hemisphere, eastern longitude.
        public static readonly Location Sydney = new Location(
            name:         "Sydney",
            latitude:     33.8568, north: false,
            longitude:    151.2153, west: false,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    20.0,
            bortleClass:  7,
            extinctionK:  0.35);

        // Quito, Ecuador. Just south of the equator (lat ~0.18 deg S), western
        // longitude. Stresses the equator-degenerate latitude case (cos(phi) ~ 1
        // throughout the geometry kernels).
        public static readonly Location Equator = new Location(
            name:         "Quito",
            latitude:     0.1807, north: false,
            longitude:    78.4678, west: true,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    2850.0,
            bortleClass:  6,
            extinctionK:  0.20);

        // Reykjavik. High northern latitude (~64 N), exercises summer-twilight
        // edge cases without going polar.
        public static readonly Location Reykjavik = new Location(
            name:         "Reykjavik",
            latitude:     64.1466, north: true,
            longitude:    21.9426, west: true,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    10.0,
            bortleClass:  4,
            extinctionK:  0.22);

        // McMurdo Station, Antarctica. ~-77 S; polar-day in austral summer,
        // polar-night in winter. The closed-form geometry identities hold here
        // even when the target is permanently below the horizon (e.g. M31
        // never rises from McMurdo, but altitude-at-transit still equals
        // MeridianAltitude, which is negative).
        public static readonly Location Antarctic = new Location(
            name:         "McMurdo",
            latitude:     77.8419, north: false,
            longitude:    166.6863, west: false,
            timeZoneInfo: TimeZoneInfo.Utc,
            elevation:    24.0,
            bortleClass:  2,
            extinctionK:  0.13);

        // Source for [Theory] [MemberData] tests whose property should hold at
        // any latitude / longitude (pure-geometry identities, batched-vs-
        // per-sample equivalence checks, etc). Each yield is
        // (displayName, Location); the display name shows in the xUnit
        // test-runner output so a failing case is identifiable at a glance.
        public static IEnumerable<object[]> All()
        {
            yield return new object[] { "PennsPark", PennsPark };
            yield return new object[] { "Sydney",    Sydney };
            yield return new object[] { "Equator",   Equator };
            yield return new object[] { "Reykjavik", Reykjavik };
            yield return new object[] { "Antarctic", Antarctic };
        }
    }
}
