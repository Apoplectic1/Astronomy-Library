namespace Astronomy.NINA;

/// <summary>
/// Camera configuration for one exposure or one set of "typical" exposures.
/// Immutable; equality is structural via the constructor (not record-derived
/// to keep the AL convention of sealed-class-with-With consistent).
/// </summary>
public sealed class ExposureSettings
{
    /// <summary>Camera gain setting (manufacturer-specific scale).</summary>
    public int Gain { get; }

    /// <summary>Camera offset setting (post per-camera XFM normalization for image-library-derived values).</summary>
    public int Offset { get; }

    /// <summary>Cooler set-point in °C.</summary>
    public double SetTempC { get; }

    /// <summary>Binning (X, Y). Almost always (1, 1) or (2, 2).</summary>
    public (int X, int Y) Binning { get; }

    /// <summary>Exposure duration in seconds.</summary>
    public double ExposureSec { get; }

    /// <summary>Creates an immutable exposure-settings record. Throws on negative gain/offset, sub-1 binning, or non-positive exposure.</summary>
    public ExposureSettings(int gain, int offset, double setTempC, (int X, int Y) binning, double exposureSec)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gain);
        ArgumentOutOfRangeException.ThrowIfLessThan(binning.X, 1, nameof(binning) + ".X");
        ArgumentOutOfRangeException.ThrowIfLessThan(binning.Y, 1, nameof(binning) + ".Y");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exposureSec);

        Gain = gain;
        Offset = offset;
        SetTempC = setTempC;
        Binning = binning;
        ExposureSec = exposureSec;
    }

    /// <summary>Named-argument builder; any omitted argument inherits from the current instance.</summary>
    public ExposureSettings With(
        int? gain = null,
        int? offset = null,
        double? setTempC = null,
        (int X, int Y)? binning = null,
        double? exposureSec = null)
        => new ExposureSettings(
            gain ?? Gain,
            offset ?? Offset,
            setTempC ?? SetTempC,
            binning ?? Binning,
            exposureSec ?? ExposureSec);
}
