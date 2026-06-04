namespace Astronomy.Catalog.Scan;

/// <summary>
/// The most-common (mode) exposure settings across a set of frames. Used to
/// summarize "what camera config did you actually use for this target/filter
/// combination?" — feeds future planning ("image the same way you have been").
/// </summary>
/// <remarks>
/// All fields are mode-based, not mean. Mode survives mixed sessions sensibly:
/// 50 frames at gain 111 plus 2 frames at gain 53 reports gain 111. Mean would
/// muddy the answer. <see cref="ExposureSec"/> is rounded to the nearest second
/// before mode-clustering so 599.97 and 600.00 don't split the cluster.
/// </remarks>
public sealed class TypicalSettings
{
    /// <summary>Most-common GAIN value across the frame set.</summary>
    public int Gain { get; }

    /// <summary>Most-common OFFSET value (post per-camera normalization, see <see cref="Astronomy.XISF.XisfHeader.OffsetNormalized"/>).</summary>
    public int Offset { get; }

    /// <summary>Most-common SET-TEMP °C.</summary>
    public double SetTempC { get; }

    /// <summary>Most-common binning (X, Y). Almost always (1, 1) or (2, 2).</summary>
    public (int X, int Y) Binning { get; }

    /// <summary>Most-common exposure duration in seconds, rounded to nearest second before clustering.</summary>
    public double ExposureSec { get; }

    /// <summary>Creates an immutable typical-settings snapshot.</summary>
    public TypicalSettings(int gain, int offset, double setTempC, (int X, int Y) binning, double exposureSec)
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
}
