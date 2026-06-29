using Astronomy.XISF;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for Astronomy.XISF unit/encoding assumptions — CONSUMERS.md
/// "Semantic assumptions" (units / encoding, the silent-wrong-result class).
/// </summary>
public sealed class XisfUnitsContractTests
{
    // Mirrors Astronomy.XISF.Tests' fixture: build a header from (name -> value) pairs.
    private static XisfHeader Make(params (string Name, string Value)[] kv)
    {
        var d = new Dictionary<string, XisfHeader.KeywordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in kv) d[n] = new XisfHeader.KeywordEntry(v, null);
        return new XisfHeader(d);
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #1:
    //   "XisfHeader.RaDegrees is degrees (TP ÷15 for hours)."
    // The FITS "RA" keyword is decimal DEGREES; RaDegrees must surface that value
    // verbatim (no hidden ÷15). If the accessor ever silently returned hours, a
    // consumer that also divides by 15 would land at 1/15th of the true RA — a
    // silent-wrong-result. A value where degrees != hours makes the two
    // interpretations distinguishable: 150.0 deg == 10.0 h.
    // ---------------------------------------------------------------------------

    [Fact]
    public void XisfHeader_RaDegrees_ReturnsDegrees_NotHours()
    {
        XisfHeader h = Make(("RA", "150.0"), ("DEC", "-45.5"));

        Assert.NotNull(h.RaDegrees);
        Assert.Equal(150.0, h.RaDegrees!.Value, precision: 6);   // the DEGREE value, verbatim
        Assert.NotEqual(10.0, h.RaDegrees.Value);                // NOT the hours value (150/15) — no hidden ÷15
        // The documented consumer conversion (TP ÷15) recovers hours from degrees.
        Assert.Equal(10.0, h.RaDegrees.Value / 15.0, precision: 6);

        // DecDegrees is signed decimal degrees, surfaced verbatim.
        Assert.NotNull(h.DecDegrees);
        Assert.Equal(-45.5, h.DecDegrees!.Value, precision: 6);
    }
}
