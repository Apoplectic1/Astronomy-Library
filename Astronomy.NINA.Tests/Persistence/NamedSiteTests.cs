using Astronomy.Core.Locations;
using Astronomy.NINA.Persistence;
using Xunit;

namespace Astronomy.NINA.Tests.Persistence;

public class NamedSiteTests
{
    // Helper -- the Penns Park fixture used across round-trip tests. Elevation/Bortle/k
    // are non-zero so the round-trip exercises every field on the DTO including the
    // zero-coercion path in NamedSite.ToLocation.
    private static Location MakePennsParkLocation()
    {
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        return new Location(
            name:         "Penns Park",
            latitude:     40.235, north: true,
            longitude:    74.985, west:  true,
            timeZoneInfo: tz,
            elevation:    80.0,
            bortleClass:  5,
            extinctionK:  0.28);
    }

    [Fact]
    public void FromLocation_RoundTrip_PreservesIdentity()
    {
        Location loc = MakePennsParkLocation();
        PlanningPreferencesDto prefs = new() { TargetFloorDeg = 30.0, MinDurationMinutes = 240.0 };

        NamedSite site = NamedSite.FromLocation(loc, prefs, localHorizonPath: @"C:\hrz\penns.hrz");
        Location roundtripped = site.ToLocation();

        Assert.Equal(loc.Name,             roundtripped.Name);
        Assert.Equal(loc.Latitude,         roundtripped.Latitude);
        Assert.Equal(loc.Longitude,        roundtripped.Longitude);
        Assert.Equal(loc.North,            roundtripped.North);
        Assert.Equal(loc.West,             roundtripped.West);
        Assert.Equal(loc.Elevation,        roundtripped.Elevation);
        Assert.Equal(loc.BortleClass,      roundtripped.BortleClass);
        Assert.Equal(loc.ExtinctionK,      roundtripped.ExtinctionK);
        Assert.Equal(loc.TimeZoneInfo.Id,  roundtripped.TimeZoneInfo.Id);
    }

    [Fact]
    public void ToLocation_WithKnownWindowsId_ReturnsDstAwareZone()
    {
        NamedSite site = new() { TimeZoneId = "Eastern Standard Time" };
        Location loc = site.ToLocation();

        Assert.True(loc.TimeZoneInfo.SupportsDaylightSavingTime);
        Assert.Equal("Eastern Standard Time", loc.TimeZoneInfo.Id);
    }

    [Fact]
    public void ToLocation_WithUnknownTimeZoneId_FallsBackToLocalNoThrow()
    {
        NamedSite site = new() { TimeZoneId = "Not A Real Zone XYZ" };
        Location loc = site.ToLocation();

        // Defensive resolver swallows TimeZoneNotFoundException and falls back to Local
        // so a missing/renamed zone on disk doesn't crash the consumer at boot.
        Assert.Equal(TimeZoneInfo.Local.Id, loc.TimeZoneInfo.Id);
    }

    [Fact]
    public void ToLocation_WithNullTimeZoneId_FallsBackToLocal()
    {
        NamedSite site = new() { TimeZoneId = null };
        Location loc = site.ToLocation();

        Assert.Equal(TimeZoneInfo.Local.Id, loc.TimeZoneInfo.Id);
    }

    [Fact]
    public void ToLocation_WithNullPreferences_Succeeds()
    {
        NamedSite site = new()
        {
            Name        = "Test Site",
            Latitude    = 40.0, North = true,
            Longitude   = 75.0, West  = true,
            TimeZoneId  = "Eastern Standard Time",
            Preferences = null,
        };

        Location loc = site.ToLocation();
        Assert.NotNull(loc);
        Assert.Equal("Test Site", loc.Name);
    }

    [Fact]
    public void FromLocation_WithNullLocation_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NamedSite.FromLocation(null, preferences: null, localHorizonPath: null));
    }
}
