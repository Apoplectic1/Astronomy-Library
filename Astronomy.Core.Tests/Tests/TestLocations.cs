using System;
using Astronomy.Core.Locations;

namespace Astronomy.Core.Tests.Tests
{
    // Test-only location fixtures with stable, hand-picked coordinates so test assertions
    // ("M31 transits at ~89° here", "M42 peaks at ~44°", etc.) keep their algorithmic
    // claims even after Location.Default's public-safe scrub. Centralised so adding a
    // fixture for a new fact style is one edit instead of dozens.
    internal static class TestLocations
    {
        // The historical Location.Default site -- US east coast, mid-latitude N, suburban
        // Bortle 5. Most facts in this suite are anchored to these coordinates.
        public static Location PennsPark => new Location(
            name:         "Penns Park",
            latitude:     40.282835, north: true,
            longitude:    74.997369, west:  true,
            horizon:      30,
            duration:     TimeSpan.FromMinutes(240),
            dateTime:     DateTime.Now,
            timeZoneInfo: TimeZoneInfo.Local,
            elevation:    80.67,
            bortleClass:  5,
            extinctionK:  0.28);
    }
}
