using System.Text.Json;
using Astronomy.NINA.Persistence;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the Astronomy.NINA.Persistence serialization surface — CONSUMERS.md
/// "Semantic assumptions" #2. The serialized JSON property names ARE the cross-app file
/// format: TP persists a NamedSite array to disk, so a property rename recompiles TP cleanly
/// but silently zeroes the value when an existing sites file is loaded (old key not found →
/// default). The "minutes" *meaning* of MinDurationMinutes stays a naming convention (not
/// unit-assertable — see NotCleanlyTestableAssumptions history); what this bench freezes is
/// the serialization shape that carries it.
/// </summary>
public sealed class NamedSitePersistenceContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #2:
    //   "NINA.Persistence.PlanningPreferencesDto.MinDurationMinutes is minutes
    //    (serialized in NamedSite)."
    // Pinned half: the on-disk JSON property names and the lossless value round-trip.
    // The DTOs are flat POCOs (parameterless ctor + settable properties) precisely so
    // any JSON serializer round-trips them without a custom converter.
    // ---------------------------------------------------------------------------

    private static NamedSite MakeSite() => new()
    {
        Name               = "MidLat North",   // neutral fixture — keep personal site data out of new files (see ROADMAP publish-scrub)
        Latitude           = 40.25,
        Longitude          = 75.0,
        North              = true,
        West               = true,
        Elevation          = 98.0,
        BortleClass        = 5,
        ExtinctionK        = 0.28,
        LocalHorizonPath   = @"C:\horizons\site.hrz",
        TimeZoneId         = "Eastern Standard Time",
        Preferences        = new PlanningPreferencesDto
        {
            TargetFloorDeg     = 30.0,
            MinDurationMinutes = 90.0,
        },
    };

    [Fact]
    public void NamedSite_SerializedPropertyNames_AreTheCrossAppFileFormat()
    {
        string json = JsonSerializer.Serialize(MakeSite());

        // The persisted key names are contract surface — a rename breaks existing
        // consumer sites files silently (value falls back to default on load).
        Assert.Contains("\"Name\"",               json);
        Assert.Contains("\"Latitude\"",           json);
        Assert.Contains("\"Longitude\"",          json);
        Assert.Contains("\"North\"",              json);
        Assert.Contains("\"West\"",               json);
        Assert.Contains("\"Elevation\"",          json);
        Assert.Contains("\"BortleClass\"",        json);
        Assert.Contains("\"ExtinctionK\"",        json);
        Assert.Contains("\"LocalHorizonPath\"",   json);
        Assert.Contains("\"TimeZoneId\"",         json);
        Assert.Contains("\"Preferences\"",        json);
        Assert.Contains("\"TargetFloorDeg\"",     json);
        Assert.Contains("\"MinDurationMinutes\"", json);
    }

    [Fact]
    public void NamedSite_JsonRoundTrip_IsLossless_NoCustomConverter()
    {
        NamedSite original = MakeSite();

        string json = JsonSerializer.Serialize(original);
        NamedSite? restored = JsonSerializer.Deserialize<NamedSite>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Name,             restored!.Name);
        Assert.Equal(original.Latitude,         restored.Latitude);
        Assert.Equal(original.Longitude,        restored.Longitude);
        Assert.Equal(original.North,            restored.North);
        Assert.Equal(original.West,             restored.West);
        Assert.Equal(original.Elevation,        restored.Elevation);
        Assert.Equal(original.BortleClass,      restored.BortleClass);
        Assert.Equal(original.ExtinctionK,      restored.ExtinctionK);
        Assert.Equal(original.LocalHorizonPath, restored.LocalHorizonPath);
        Assert.Equal(original.TimeZoneId,       restored.TimeZoneId);
        Assert.NotNull(restored.Preferences);
        Assert.Equal(30.0, restored.Preferences!.TargetFloorDeg);
        Assert.Equal(90.0, restored.Preferences.MinDurationMinutes);
    }

    [Fact]
    public void NamedSite_NullPreferences_RoundTripsAsNull()
    {
        // "Null = consumer-side defaults" — the null must survive the round trip,
        // not materialize as a zeroed DTO (0-minute floor would be a silent-wrong-result).
        var site = new NamedSite { Name = "Bare" };

        string json = JsonSerializer.Serialize(site);
        NamedSite? restored = JsonSerializer.Deserialize<NamedSite>(json);

        Assert.NotNull(restored);
        Assert.Null(restored!.Preferences);
    }
}
