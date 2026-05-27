namespace Astronomy.NINA;

/// <summary>
/// Identity + characterization of an imaging filter. Immutable; carries a stable
/// <see cref="Name"/> (the string consumers display and NINA sequences use),
/// a typed <see cref="Kind"/> classification, and optional bandwidth metadata
/// for moon-tolerance / K-S sky brightness calculations.
/// </summary>
/// <remarks>
/// Static factories (<see cref="Ha"/>, <see cref="OIII"/>, …) provide the standard
/// narrowband / broadband / luminance filters with conventional center / bandwidth
/// values. Custom filters use the public constructor.
/// </remarks>
public sealed class Filter
{
    /// <summary>Stable filter identifier (e.g. "Ha", "OIII", "L", "R", "G", "B"). Used in NINA sequence files and TP display.</summary>
    public string Name { get; }

    /// <summary>Classification for branching at consumer sites without string matching.</summary>
    public FilterKind Kind { get; }

    /// <summary>Center wavelength in nanometres; <see langword="null"/> when not applicable (e.g. Luminance, RGB).</summary>
    public double? CenterNm { get; }

    /// <summary>FWHM bandwidth in nanometres; <see langword="null"/> when unknown or not applicable.</summary>
    public double? BandwidthNm { get; }

    /// <summary>Creates an immutable filter. Throws if <paramref name="name"/> is empty or bandwidth values are non-positive when present.</summary>
    public Filter(string name, FilterKind kind, double? centerNm = null, double? bandwidthNm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (centerNm.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(centerNm.Value, nameof(centerNm));
        if (bandwidthNm.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandwidthNm.Value, nameof(bandwidthNm));

        Name = name;
        Kind = kind;
        CenterNm = centerNm;
        BandwidthNm = bandwidthNm;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public Filter With(
        string? name = null,
        FilterKind? kind = null,
        double? centerNm = null,
        double? bandwidthNm = null)
        => new Filter(
            name ?? Name,
            kind ?? Kind,
            centerNm ?? CenterNm,
            bandwidthNm ?? BandwidthNm);

    // Standard filter presets — conventional center/bandwidth for the user's
    // astronomical narrowband set. Each is a canonical singleton (Filter is
    // immutable), so callers can rely on reference identity if useful.

    /// <summary>Hα emission line (656.3 nm). Standard 3 nm Astrodon-style narrowband bandwidth.</summary>
    public static readonly Filter Ha   = new("Ha",   FilterKind.Narrowband, 656.3, 3.0);

    /// <summary>OIII emission line (500.7 nm). Standard 3 nm narrowband bandwidth.</summary>
    public static readonly Filter OIII = new("OIII", FilterKind.Narrowband, 500.7, 3.0);

    /// <summary>SII emission line (671.6 nm). Standard 3 nm narrowband bandwidth.</summary>
    public static readonly Filter SII  = new("SII",  FilterKind.Narrowband, 671.6, 3.0);

    /// <summary>Luminance — broadband clear / IR-cut. No meaningful single center wavelength.</summary>
    public static readonly Filter L    = new("L",    FilterKind.Luminance);

    /// <summary>Red broadband.</summary>
    public static readonly Filter R    = new("R",    FilterKind.Broadband);

    /// <summary>Green broadband.</summary>
    public static readonly Filter G    = new("G",    FilterKind.Broadband);

    /// <summary>Blue broadband.</summary>
    public static readonly Filter B    = new("B",    FilterKind.Broadband);
}
