using System.Globalization;

namespace Astronomy.NINA.Xisf;

/// <summary>
/// FITS-keyword view of an XISF file header. Provides raw access plus typed accessors
/// for the keywords TP needs at scan time. Immutable; safe to share across threads.
/// </summary>
/// <remarks>
/// <para>
/// Constructed by <see cref="XisfHeaderReader"/>. Keyword names are case-insensitive
/// (matching FITS-spec). Values are normalized at construction: surrounding single
/// quotes (FITS string syntax) stripped; trailing FITS-pad whitespace trimmed.
/// </para>
/// <para>
/// Numeric accessors return <see langword="null"/> when the keyword is absent OR
/// when the value cannot be parsed as the requested numeric type (no exceptions
/// thrown). Callers branch on null to decide whether the keyword is required.
/// </para>
/// </remarks>
public sealed class XisfHeader
{
    private readonly IReadOnlyDictionary<string, string> mRaw;

    /// <summary>
    /// Creates a header from a pre-extracted FITS-keyword dictionary. Caller owns
    /// the dictionary; this ctor wraps it as-is (key comparer must be case-insensitive).
    /// </summary>
    public XisfHeader(IReadOnlyDictionary<string, string> rawKeywords)
    {
        ArgumentNullException.ThrowIfNull(rawKeywords);
        mRaw = rawKeywords;
    }

    /// <summary>Raw string value for a FITS keyword (null if absent).</summary>
    public string? Raw(string keyword) => mRaw.TryGetValue(keyword, out var v) ? v : null;

    /// <summary>True if the keyword is present in the header.</summary>
    public bool Has(string keyword) => mRaw.ContainsKey(keyword);

    /// <summary>All keyword names present (for diagnostic dumps).</summary>
    public IEnumerable<string> KeywordNames => mRaw.Keys;

    // ----- Required for Phase A aggregation -----

    /// <summary>OBJECT — target name (FITS spec, XFM-enforced). Named <c>ObjectName</c> to avoid collision with <see cref="object"/>.</summary>
    public string? ObjectName => Raw("OBJECT");

    /// <summary>RA in decimal degrees (FITS spec); null if absent or unparseable. Note: FITS RA is degrees, NOT hours — Library convention requires conversion (÷ 15) at the boundary.</summary>
    public double? RaDegrees => ParseDouble(Raw("RA"));

    /// <summary>DEC in decimal degrees, signed; null if absent or unparseable.</summary>
    public double? DecDegrees => ParseDouble(Raw("DEC"));

    /// <summary>DATE-OBS — exposure-start instant as <see cref="DateTimeKind.Utc"/>; null if absent or unparseable.</summary>
    public DateTime? DateObsUtc => ParseDateUtc(Raw("DATE-OBS"));

    /// <summary>EXPTIME — exposure duration in seconds. Falls back to legacy <c>EXPOSURE</c> keyword when EXPTIME is absent (XFM-processed files may carry either).</summary>
    public double? ExposureSec => ParseDouble(Raw("EXPTIME")) ?? ParseDouble(Raw("EXPOSURE"));

    /// <summary>FILTER — filter name from FITS (may be single-letter "H"/"O"/"S"/"L"/"R"/"G"/"B" or full "Ha"/"OIII" form; XFM normalizes per camera).</summary>
    public string? Filter => Raw("FILTER");

    /// <summary>GAIN — integer camera gain setting.</summary>
    public int? Gain => ParseInt(Raw("GAIN"));

    /// <summary>OFFSET — raw integer camera offset, pre per-camera normalization. See <see cref="OffsetNormalized"/> for the XFM-style normalized value.</summary>
    public int? OffsetRaw => ParseInt(Raw("OFFSET"));

    /// <summary>SET-TEMP — sensor setpoint °C.</summary>
    public double? SetTempC => ParseDouble(Raw("SET-TEMP"));

    /// <summary>CCD-TEMP — sensor readback °C at exposure start.</summary>
    public double? CcdTempC => ParseDouble(Raw("CCD-TEMP"));

    /// <summary>XBINNING — horizontal binning factor.</summary>
    public int? XBinning => ParseInt(Raw("XBINNING"));

    /// <summary>YBINNING — vertical binning factor.</summary>
    public int? YBinning => ParseInt(Raw("YBINNING"));

    /// <summary>IMAGETYP — frame type ("LIGHT", "DARK", "BIAS", "FLAT"); case may vary.</summary>
    public string? ImageType => Raw("IMAGETYP");

    /// <summary>INSTRUME — camera identifier (e.g. "ZWO ASI183MM Pro").</summary>
    public string? Instrument => Raw("INSTRUME");

    /// <summary>
    /// OFFSET normalized per-camera following XFM's convention. Null if
    /// <see cref="OffsetRaw"/> is null OR the camera is on XFM's strip list.
    /// </summary>
    /// <remarks>
    /// Per-camera divisors mirror XFM/KeywordList.cs. Matches both short XFM codes
    /// (Z183/Z533/Q178/A144 — observed in Dan's library) and longer manufacturer names
    /// (ZWO ASI183 / ZWO ASI533 / QHY178), since INSTRUME format varies by writer:
    /// <list type="bullet">
    ///   <item>Z183 / ZWO ASI183 → ÷5</item>
    ///   <item>Z533 / ZWO ASI533 → ÷40</item>
    ///   <item>Q178 / QHY178 → ÷18.33</item>
    ///   <item>A144 → strip (null)</item>
    /// </list>
    /// Unrecognized cameras pass through unchanged.
    /// </remarks>
    public int? OffsetNormalized
    {
        get
        {
            if (OffsetRaw is not int raw) return null;
            string cam = Instrument ?? string.Empty;
            if (cam.Contains("Z183", StringComparison.OrdinalIgnoreCase) || cam.Contains("ASI183", StringComparison.OrdinalIgnoreCase)) return raw / 5;
            if (cam.Contains("Z533", StringComparison.OrdinalIgnoreCase) || cam.Contains("ASI533", StringComparison.OrdinalIgnoreCase)) return raw / 40;
            if (cam.Contains("Q178", StringComparison.OrdinalIgnoreCase) || cam.Contains("QHY178", StringComparison.OrdinalIgnoreCase)) return (int)Math.Round(raw / 18.33);
            if (cam.Contains("A144", StringComparison.OrdinalIgnoreCase)) return null;
            return raw;
        }
    }

    // ----- Captured but not aggregated in Phase A (for future quality-summary work) -----

    /// <summary>SSWEIGHT — PixInsight WBPP subframe selector weight.</summary>
    public double? SsWeight => ParseDouble(Raw("SSWEIGHT"));

    /// <summary>NWEIGHT — PixInsight normalized weight.</summary>
    public double? NWeight => ParseDouble(Raw("NWEIGHT"));

    /// <summary>W_SNR — PixInsight subframe SNR component.</summary>
    public double? WSnr => ParseDouble(Raw("W_SNR"));

    /// <summary>W_FWHM — PixInsight subframe FWHM component (lower is better).</summary>
    public double? WFwhm => ParseDouble(Raw("W_FWHM"));

    /// <summary>W_ECC — PixInsight subframe eccentricity component (lower is better).</summary>
    public double? WEcc => ParseDouble(Raw("W_ECC"));

    /// <summary>W_PSFSNR — PixInsight PSF SNR.</summary>
    public double? WPsfSnr => ParseDouble(Raw("W_PSFSNR"));

    /// <summary>HFR — half-flux radius from focus/quality.</summary>
    public double? Hfr => ParseDouble(Raw("HFR"));

    /// <summary>AIRMASS — line-of-sight atmospheres at exposure start.</summary>
    public double? Airmass => ParseDouble(Raw("AIRMASS"));

    /// <summary>OBJCTALT — target altitude (degrees) at exposure start.</summary>
    public double? TargetAltDeg => ParseDouble(Raw("OBJCTALT"));

    /// <summary>OBJCTAZ — target azimuth (degrees) at exposure start.</summary>
    public double? TargetAzDeg => ParseDouble(Raw("OBJCTAZ"));

    /// <summary>FOCALLEN — focal length in mm.</summary>
    public double? FocalLengthMm => ParseDouble(Raw("FOCALLEN"));

    /// <summary>XPIXSZ — pixel size, X direction, μm.</summary>
    public double? XPixelSizeUm => ParseDouble(Raw("XPIXSZ"));

    /// <summary>YPIXSZ — pixel size, Y direction, μm.</summary>
    public double? YPixelSizeUm => ParseDouble(Raw("YPIXSZ"));

    /// <summary>FOCPOS — focuser position (microsteps).</summary>
    public int? FocuserPos => ParseInt(Raw("FOCPOS"));

    /// <summary>FOCTEMP — focuser temperature °C.</summary>
    public double? FocuserTempC => ParseDouble(Raw("FOCTEMP"));

    /// <summary>POSANGLE — rotator mechanical position (degrees).</summary>
    public double? RotatorPosAngleDeg => ParseDouble(Raw("POSANGLE"));

    /// <summary>OBJCTROT — rotator sky angle (degrees).</summary>
    public double? RotatorSkyAngleDeg => ParseDouble(Raw("OBJCTROT"));

    // ----- Parse helpers -----

    private static double? ParseDouble(string? v) =>
        v is not null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : null;

    private static int? ParseInt(string? v) =>
        v is not null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
            ? i : null;

    private static DateTime? ParseDateUtc(string? v)
    {
        if (v is null) return null;
        if (DateTime.TryParse(
                v, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return null;
    }
}
