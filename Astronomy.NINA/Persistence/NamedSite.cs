using Astronomy.Core.Locations;

namespace Astronomy.NINA.Persistence;

/// <summary>
/// Cross-app persistence DTO for a named observing site. The serialised JSON shape
/// is shared across consumer apps (TargetPlanner, IntervalScheduler, ISP, XisfManager)
/// so a sites array round-trips byte-for-byte between any of them.
/// </summary>
/// <remarks>
/// <para>
/// Flat POCO with parameterless ctor and public settable properties: round-trips
/// through both Newtonsoft.Json (consumed by TP) and System.Text.Json without a
/// custom converter. The DTO depends only on <see cref="Astronomy.Core.Locations"/>
/// for <see cref="Location"/>; no JSON-library dependency.
/// </para>
/// <para>
/// <see cref="Latitude"/> / <see cref="Longitude"/> are positive magnitudes; hemisphere
/// flags travel as paired <see cref="North"/> / <see cref="West"/> bools -- matches the
/// <see cref="Location"/> convention.
/// </para>
/// <para>
/// <see cref="TimeZoneId"/> is a Windows TZ ID string (<c>"Eastern Standard Time"</c>,
/// <c>"Pacific Standard Time"</c>, etc). Resolved at runtime via
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>, which returns a
/// DST-aware <see cref="TimeZoneInfo"/>. The per-instant DST evaluation in
/// <see cref="TimeZoneInfo.ConvertTimeFromUtc(System.DateTime, TimeZoneInfo)"/>
/// is what makes consumer-side per-night local-time labels correct across ST/DST
/// transitions without any caller-side date arithmetic.
/// </para>
/// <para>
/// A null or unknown <see cref="TimeZoneId"/> falls back to
/// <see cref="TimeZoneInfo.Local"/> at resolve time -- prevents an uninstalled or
/// renamed zone from crashing the consumer at boot.
/// </para>
/// </remarks>
public sealed class NamedSite
{
    /// <summary>Display name for the site, shown in the consumer's location picker.</summary>
    public string? Name { get; set; }

    /// <summary>Latitude magnitude in degrees (positive), paired with <see cref="North"/>.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude magnitude in degrees (positive), paired with <see cref="West"/>.</summary>
    public double Longitude { get; set; }

    /// <summary>Latitude hemisphere flag. <see langword="true"/> = northern.</summary>
    public bool North { get; set; }

    /// <summary>Longitude hemisphere flag. <see langword="true"/> = western.</summary>
    public bool West { get; set; }

    /// <summary>Observer ground elevation, meters above geoid. Default 0.</summary>
    public double Elevation { get; set; }

    /// <summary>Bortle dark-sky class (1 = excellent dark, 9 = inner-city). 0 = not set; <see cref="ToLocation"/> coerces to 5.</summary>
    public int BortleClass { get; set; }

    /// <summary>Atmospheric extinction coefficient k at 500 nm (mag/airmass). 0 = not set; <see cref="ToLocation"/> coerces to 0.28.</summary>
    public double ExtinctionK { get; set; }

    /// <summary>Optional path to a NINA-format <c>.hrz</c> local-horizon polyline file. Consumer-side loader parses it.</summary>
    public string? LocalHorizonPath { get; set; }

    /// <summary>
    /// Windows TZ ID string (<c>"Eastern Standard Time"</c>, <c>"Pacific Standard Time"</c>, etc).
    /// Resolved at runtime via <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>;
    /// null/empty/unknown falls back to <see cref="TimeZoneInfo.Local"/>.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Per-site planning preferences. Null = consumer-side defaults.</summary>
    public PlanningPreferencesDto? Preferences { get; set; }

    /// <summary>
    /// Build the <see cref="Location"/> math primitive from this DTO. The resolved
    /// <see cref="Location.TimeZoneInfo"/> is DST-aware when <see cref="TimeZoneId"/>
    /// names a real system zone; falls back to <see cref="TimeZoneInfo.Local"/>
    /// otherwise so an unknown ID doesn't crash the consumer.
    /// </summary>
    public Location ToLocation()
    {
        return new Location(
            name:         Name,
            latitude:     Latitude,  north: North,
            longitude:    Longitude, west:  West,
            timeZoneInfo: ResolveTimeZone(TimeZoneId),
            elevation:    Elevation,
            bortleClass:  BortleClass <= 0 ? 5    : BortleClass,
            extinctionK:  ExtinctionK <= 0 ? 0.28 : ExtinctionK);
    }

    /// <summary>
    /// Round-trips a <see cref="Location"/> + per-site planning prefs + horizon path
    /// into a serialisable <see cref="NamedSite"/>. The stamped <see cref="TimeZoneId"/>
    /// is <c>loc.TimeZoneInfo.Id</c>, which on Windows is the Windows TZ form
    /// (<c>"Eastern Standard Time"</c>) when the zone came from
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>.
    /// </summary>
    public static NamedSite FromLocation(
        Location loc,
        PlanningPreferencesDto? preferences,
        string? localHorizonPath)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return new NamedSite
        {
            Name             = loc.Name,
            Latitude         = loc.Latitude,
            Longitude        = loc.Longitude,
            North            = loc.North,
            West             = loc.West,
            Elevation        = loc.Elevation,
            BortleClass      = loc.BortleClass,
            ExtinctionK      = loc.ExtinctionK,
            LocalHorizonPath = localHorizonPath,
            TimeZoneId       = loc.TimeZoneInfo?.Id,
            Preferences      = preferences,
        };
    }

    internal static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
