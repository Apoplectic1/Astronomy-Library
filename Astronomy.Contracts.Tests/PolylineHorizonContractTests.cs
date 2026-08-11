using Astronomy.Core.Horizons;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #15 — PolylineHorizonProfile's fail-fast input
/// contract. Pinned 2026-08-11 after adjudication: the registry's "the type does NOT validate"
/// note predated the fail-fast constructor; the code is the contract.
/// </summary>
public sealed class PolylineHorizonContractTests
{
    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #15:
    //   "PolylineHorizonProfile(az[], alt[]) — parallel arrays; length mismatch and
    //    empty input throw ArgumentException (fail-fast); azimuths are normalized to
    //    [0, 360) and sorted internally — unsorted/duplicate input is accepted."
    //   TP persists its polyline horizon and rebuilds this type from stored arrays,
    //   so the throw-vs-accept boundary is what stands between a corrupted store and
    //   a silently wrong horizon.
    // ---------------------------------------------------------------------------

    [Fact]
    public void LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new PolylineHorizonProfile([0.0, 90.0, 180.0], [10.0, 20.0]));
    }

    [Fact]
    public void EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PolylineHorizonProfile([], []));
    }

    [Fact]
    public void NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PolylineHorizonProfile(null!, [1.0]));
        Assert.Throws<ArgumentNullException>(() => new PolylineHorizonProfile([1.0], null!));
    }

    [Fact]
    public void UnsortedAndOutOfRangeAzimuths_AreNormalizedAndSorted_NotRejected()
    {
        // Out of order, with one azimuth outside [0, 360): −90 normalizes to 270.
        PolylineHorizonProfile p = new([180.0, -90.0, 0.0], [10.0, 30.0, 20.0]);

        // Each supplied knot reads back at its normalized azimuth — the ctor sorted
        // internally instead of rejecting or silently mispairing the parallel arrays.
        Assert.Equal(20.0, p.AltitudeAt(0.0), precision: 9);
        Assert.Equal(10.0, p.AltitudeAt(180.0), precision: 9);
        Assert.Equal(30.0, p.AltitudeAt(270.0), precision: 9);
        Assert.Equal(10.0, p.MinAltitude, precision: 9);
    }
}
