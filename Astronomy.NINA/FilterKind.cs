namespace Astronomy.NINA;

/// <summary>
/// Classification of an imaging filter for planning purposes (color tinting,
/// per-bandwidth K-S sky brightness calc, etc.). Distinct from filter
/// *identity* (which is the <see cref="Filter.Name"/> string).
/// </summary>
public enum FilterKind
{
    /// <summary>Narrowband emission-line filter (Ha, OIII, SII, NII, etc.) — typically 3–7 nm bandwidth, much more tolerant of moonlight than broadband.</summary>
    Narrowband,

    /// <summary>Broadband color filter (Red, Green, Blue) — wide bandwidth, sensitive to moonlight.</summary>
    Broadband,

    /// <summary>Luminance — broad clear / IR-cut filter for high-SNR detail capture.</summary>
    Luminance,

    /// <summary>One-shot-color filter (full RGB Bayer matrix, OSC sensors).</summary>
    RGB,

    /// <summary>Filter kind not classified or unknown.</summary>
    Unknown,
}
