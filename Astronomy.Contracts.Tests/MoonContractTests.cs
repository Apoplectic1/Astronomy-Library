using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Targets;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for the Core Moon surface — the compiler-invisible semantic
/// assumptions consumers bake in, catalogued in CONSUMERS.md "Semantic assumptions".
/// </summary>
public sealed class MoonContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #16:
    //   "Moon.LunarAge.DaysAt throws on non-UTC DateTimeKind."
    // The whole Core moment-pipeline is UTC-only; a Local/Unspecified instant would
    // silently mis-place the lunar age (and everything downstream of it). The guard
    // turns that into a loud failure instead of a silent-wrong-result.
    // ---------------------------------------------------------------------------

    [Fact]
    public void LunarAge_DaysAt_ThrowsOnLocalAndUnspecifiedKind_SucceedsForUtc()
    {
        var local = new DateTime(2026, 6, 28, 3, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 6, 28, 3, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => LunarAge.DaysAt(local));
        Assert.Throws<ArgumentException>(() => LunarAge.DaysAt(unspecified));

        // UTC is the supported path: no throw, result inside one synodic month.
        var utc = new DateTime(2026, 6, 28, 3, 0, 0, DateTimeKind.Utc);
        double age = LunarAge.DaysAt(utc);
        Assert.InRange(age, 0.0, LunarAge.SynodicMonthDays);
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #13:
    //   "MoonEphemeris.Sample(count) returns EXACTLY count elements
    //    (TP gates a cache hit on it)."
    // Off-by-one here would corrupt the per-night moon cache indexing.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]    // documented empty-list edge
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(289)]  // a full ~24h night at 5-min spacing-ish; arbitrary large count
    public void MoonEphemeris_Sample_ReturnsExactlyCount(int count)
    {
        var start = new DateTime(2026, 6, 28, 2, 0, 0, DateTimeKind.Utc);

        IReadOnlyList<MoonSample> samples =
            MoonEphemeris.Sample(Location.Default, start, TimeSpan.FromMinutes(1), count);

        Assert.Equal(count, samples.Count);
    }

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #3:
    //   "Moon.MoonSeparation.ObserveAt returns GEOMETRIC MoonAltDeg
    //    (TP adds refraction itself; apparent would double-apply)."
    // ObserveAt's MoonAltDeg must equal the GEOMETRIC topocentric altitude, NOT the
    // Saemundsson-refracted (apparent) altitude. MoonEphemeris.Sample carries both
    // (AltDegGeometric / AltDegApparent) from the identical Meeus pipeline, so it is
    // the reference: ObserveAt must match geometric and differ from apparent.
    // ---------------------------------------------------------------------------

    [Fact]
    public void MoonSeparation_ObserveAt_ReturnsGeometricMoonAlt_NotApparent()
    {
        // A site + target are needed; only the location affects the moon altitude.
        Location loc = Location.Default;                     // 40N, 75W, sea level
        Target target = Target.Default;                      // any target (M31) — alt math is target-independent

        // Find a UTC instant where the moon is meaningfully above the horizon but low
        // enough that refraction (apparent - geometric) is clearly non-zero, so the
        // geometric-vs-apparent distinction is actually resolvable. Deterministic scan
        // of a fixed UTC day at 10-min steps; pick the first sample in a low-alt band.
        var dayStart = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<MoonSample> day =
            MoonEphemeris.Sample(loc, dayStart, TimeSpan.FromMinutes(10), 24 * 6);

        int idx = -1;
        for (int i = 0; i < day.Count; i++)
        {
            if (day[i].AltDegGeometric >= 3.0 && day[i].AltDegGeometric <= 25.0)
            {
                idx = i;
                break;
            }
        }
        Assert.True(idx >= 0, "expected at least one low-altitude (3..25 deg) moon sample in the test day");

        DateTime t = dayStart + TimeSpan.FromMinutes(10 * idx);
        MoonSample reference = MoonEphemeris.Sample(loc, t, TimeSpan.FromMinutes(1), 1)[0];

        // Sanity: at this instant geometric and apparent are distinguishable (refraction
        // is non-trivial), so the assertion below proves something.
        double refraction = reference.AltDegApparent - reference.AltDegGeometric;
        Assert.True(refraction > 0.02,
            $"chosen instant must have non-trivial refraction; was {refraction:F4} deg");

        (double _, double moonAltDeg, double _) = MoonSeparation.ObserveAt(target, loc, t);

        // ObserveAt matches GEOMETRIC altitude (same Meeus pipeline → bit-identical).
        Assert.Equal(reference.AltDegGeometric, moonAltDeg, precision: 6);

        // ...and is NOT the apparent (refraction-added) altitude.
        Assert.True(Math.Abs(moonAltDeg - reference.AltDegApparent) > 0.02,
            "ObserveAt MoonAltDeg must not equal the refraction-adjusted (apparent) altitude");
    }
}
