namespace Astronomy.NINA;

/// <summary>
/// Identity + characterization of an imaging filter. Immutable; carries a stable
/// <see cref="Name"/> (the string consumers display and NINA sequences use) plus
/// optional center wavelength and bandwidth metadata for moon-tolerance / K-S
/// sky brightness calculations.
/// </summary>
/// <remarks>
/// Static factories (<see cref="H"/>, <see cref="O"/>, …) provide the standard
/// narrowband / broadband / luminance filters with conventional center / bandwidth
/// values calibrated to a specific Astrodon Gen 2 E-Series LRGB + Astrodon 3nm
/// Hα/OIII + Chroma 3nm SII filter set. Custom filters use the public constructor.
/// </remarks>
public sealed class Filter
{
    /// <summary>Stable filter identifier ("H", "O", "S", "L", "R", "G", "B" for the standard set). Used in NINA sequence files and TP display.</summary>
    public string Name { get; }

    /// <summary>Center wavelength in nanometres; <see langword="null"/> when not applicable or unknown (custom user-imported filters).</summary>
    public double? CenterNm { get; }

    /// <summary>FWHM bandwidth in nanometres; <see langword="null"/> when not applicable or unknown.</summary>
    public double? BandwidthNm { get; }

    /// <summary>Creates an immutable filter. Throws if <paramref name="name"/> is empty or bandwidth values are non-positive when present.</summary>
    public Filter(string name, double? centerNm = null, double? bandwidthNm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (centerNm.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(centerNm.Value, nameof(centerNm));
        if (bandwidthNm.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandwidthNm.Value, nameof(bandwidthNm));

        Name = name;
        CenterNm = centerNm;
        BandwidthNm = bandwidthNm;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public Filter With(
        string? name = null,
        double? centerNm = null,
        double? bandwidthNm = null)
        => new Filter(
            name ?? Name,
            centerNm ?? CenterNm,
            bandwidthNm ?? BandwidthNm);

    // Standard filter presets — conventional center/bandwidth for the user's
    // astronomical narrowband + LRGB set. Each is a canonical singleton (Filter
    // is immutable), so callers can rely on reference identity if useful.
    //
    // Calibrated to a specific Astrodon Gen 2 E-Series LRGB + Astrodon 3nm Hα/OIII
    // + Chroma 3nm SII filter set. Center / bandwidth per manufacturer datasheets;
    // Chroma SII centered between the 671.6 / 673.1 doublet lines (not on the
    // 671.6 spectroscopic line). Values match TargetPlanner's
    // FilterLibrary.BuiltinDefaults so a TP filter and a Library preset of the
    // same Name carry the same K-S inputs.

    /// <summary>Hα emission line (656.3 nm). Standard 3 nm Astrodon narrowband.</summary>
    public static readonly Filter H = new("H", 656.3, 3.0);

    /// <summary>[O III] emission line (500.7 nm). Standard 3 nm Astrodon narrowband.</summary>
    public static readonly Filter O = new("O", 500.7, 3.0);

    /// <summary>[S II] doublet centered between 671.6 / 673.1 nm. Chroma 3 nm narrowband.</summary>
    public static readonly Filter S = new("S", 672.4, 3.0);

    /// <summary>Luminance — broadband clear / IR-cut. Astrodon E-Series ~300 nm bandwidth at 550 nm center.</summary>
    public static readonly Filter L = new("L", 550.0, 300.0);

    /// <summary>Red broadband. Astrodon E-Series ~60 nm bandwidth at 650 nm.</summary>
    public static readonly Filter R = new("R", 650.0, 60.0);

    /// <summary>Green broadband. Astrodon E-Series ~65 nm bandwidth at 525 nm.</summary>
    public static readonly Filter G = new("G", 525.0, 65.0);

    /// <summary>Blue broadband. Astrodon E-Series ~100 nm bandwidth at 450 nm.</summary>
    public static readonly Filter B = new("B", 450.0, 100.0);
}
