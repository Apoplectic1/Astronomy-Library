using System.Globalization;
using Astronomy.Catalog.Scan;
using Astronomy.XISF;
using Xunit;

namespace Astronomy.Catalog.Tests.Scan;

/// <summary>
/// Framing clustering over synthetic headers (openspec <c>rotation-framing-key</c> → <c>framing-keys</c>):
/// fold-180 angle grouping with the field-center split, flip merging, translated-stray separation, and the
/// sky/mechanical/unknown expression partition.
/// </summary>
public class FramingClustererTests
{
    // ---- angle grouping -----------------------------------------------------

    [Fact]
    public void TwoRotations_BecomeTwoClusters()
    {
        List<XisfHeader> frames = [.. Frames(20, sky: 20.0), .. Frames(30, sky: 160.0)];

        (IReadOnlyList<FramingCluster> clusters, int[] assignment) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(30, clusters[0].FrameCount);            // largest first
        Assert.Equal(160.0, clusters[0].FoldAngleDegrees!.Value, 3);
        Assert.Equal(20.0, clusters[1].FoldAngleDegrees!.Value, 3);
        Assert.All(Enumerable.Range(0, 20), i => Assert.Equal(1, assignment[i]));
        Assert.All(Enumerable.Range(20, 30), i => Assert.Equal(0, assignment[i]));
    }

    [Fact]
    public void UniformFraming_StaysOneCluster()
    {
        // Within-framing jitter (measured ≤ 0.2° in the real library) never splits.
        List<XisfHeader> frames = [.. Frames(10, sky: 114.9), .. Frames(10, sky: 115.0)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        FramingCluster only = Assert.Single(clusters);
        Assert.Equal(RotationExpression.Sky, only.Expression);
        Assert.Equal(20, only.FrameCount);
    }

    [Fact]
    public void SingleStrayFraming_IsNotAbsorbed()
    {
        // The M100 shape: one 135°-off frame among a uniform campaign — the reference-frame hazard.
        List<XisfHeader> frames = [.. Frames(104, sky: 0.0), .. Frames(1, sky: 135.0)];

        (IReadOnlyList<FramingCluster> clusters, int[] assignment) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(1, clusters[1].FrameCount);
        Assert.Equal(135.0, clusters[1].FoldAngleDegrees!.Value, 3);
        Assert.Equal(1, assignment[104]);
    }

    [Fact]
    public void FoldWrap_ZeroAndNearly180_AreOneCluster()
    {
        // 179.95° folds to within tolerance of 0° across the wrap seam (the Wizard shape).
        List<XisfHeader> frames = [.. Frames(8, sky: 0.0), .. Frames(8, sky: 179.95)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Single(clusters);
    }

    // ---- the flip rule ------------------------------------------------------

    [Fact]
    public void PierFlip_SameCenter_IsOneFraming()
    {
        // 65° and 245° (Δ180) around one center: identical footprint, one cluster (the M81 flip shape).
        List<XisfHeader> frames = [.. Frames(12, sky: 65.0), .. Frames(9, sky: 245.0)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        FramingCluster only = Assert.Single(clusters);
        Assert.Equal(21, only.FrameCount);
        Assert.Equal(65.0, only.FoldAngleDegrees!.Value, 3);
    }

    [Fact]
    public void FlipAngles_DistinctCenters_StaySeparate()
    {
        // 180° apart in angle but pointing at genuinely different fields: the centroid guard splits them.
        List<XisfHeader> frames = [.. Frames(12, sky: 65.0), .. Frames(9, sky: 245.0, raDeg: 152.0)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.All(clusters, c => Assert.Equal(65.0, c.FoldAngleDegrees!.Value, 3));
        Assert.NotEqual(clusters[0].CentroidRaHours!.Value, clusters[1].CentroidRaHours!.Value, 3);
    }

    // ---- the translated stray -----------------------------------------------

    [Fact]
    public void TranslatedStray_AtUnchangedRotation_Separates()
    {
        // The M97 shape: same sky angle, center 1.4°+ away — footprint is center as much as angle.
        List<XisfHeader> frames = [.. Frames(211, sky: 125.0), .. Frames(1, sky: 125.0, raDeg: 152.5)];

        (IReadOnlyList<FramingCluster> clusters, int[] assignment) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(1, clusters[1].FrameCount);
        Assert.Equal(1, assignment[211]);
    }

    [Fact]
    public void CampaignDrift_ChainsIntoOneCluster()
    {
        // Single linkage: a drifting sequence (each step within the link distance) stays one framing even
        // when its extremes sit farther apart than the link distance.
        List<XisfHeader> frames =
            [.. Frames(5, sky: 50.0, raDeg: 150.0), .. Frames(5, sky: 50.0, raDeg: 150.4), .. Frames(5, sky: 50.0, raDeg: 150.8)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Single(clusters);
    }

    // ---- expressions ---------------------------------------------------------

    [Fact]
    public void MechanicalOnlyUnit_ClustersInternally()
    {
        // Two mechanical framings far apart fold-180 (the Eastern Veil shape). No sky angle anywhere,
        // and no sky value is fabricated.
        List<XisfHeader> frames = [.. Frames(28, mech: 119.4), .. Frames(53, mech: 128.3)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.All(clusters, c => Assert.Equal(RotationExpression.Mechanical, c.Expression));
    }

    [Fact]
    public void SkyAndMechanical_NeverShareACluster()
    {
        // The zero point between the two is unknowable, so equal numbers are not equal angles.
        List<XisfHeader> frames = [.. Frames(10, sky: 50.0), .. Frames(10, mech: 50.0)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        Assert.Contains(clusters, c => c.Expression == RotationExpression.Sky);
        Assert.Contains(clusters, c => c.Expression == RotationExpression.Mechanical);
    }

    [Fact]
    public void NoRotationAnywhere_IsOneUnknownCluster()
    {
        List<XisfHeader> frames = [.. Frames(5)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        FramingCluster only = Assert.Single(clusters);
        Assert.Equal(RotationExpression.Unknown, only.Expression);
        Assert.Null(only.FoldAngleDegrees);
    }

    [Fact]
    public void RotationlessFrames_JoinASoleCluster_ButNotAnAmbiguousUnit()
    {
        // Unambiguous unit: the rotation-less frames ride along.
        List<XisfHeader> sole = [.. Frames(10, sky: 20.0), .. Frames(2)];
        (IReadOnlyList<FramingCluster> soleClusters, _) = FramingClusterer.Assign(sole);
        FramingCluster only = Assert.Single(soleClusters);
        Assert.Equal(12, only.FrameCount);
        Assert.Equal(20.0, only.FoldAngleDegrees!.Value, 3);   // the mean averages recorded angles only

        // Ambiguous unit (two framings): never silently attributed — they form their own Unknown cluster.
        List<XisfHeader> ambiguous = [.. Frames(10, sky: 20.0), .. Frames(10, sky: 60.0), .. Frames(2)];
        (IReadOnlyList<FramingCluster> ambClusters, _) = FramingClusterer.Assign(ambiguous);
        Assert.Equal(3, ambClusters.Count);
        Assert.Contains(ambClusters, c => c.Expression == RotationExpression.Unknown && c.FrameCount == 2);
    }

    [Fact]
    public void CoordinatelessFrames_DoNotClusterAsThoughRecorded()
    {
        // An angle group split by center: frames with no coordinates cannot be placed in either center
        // group, so they form their own — never silently attributed to one side.
        List<XisfHeader> frames =
            [.. Frames(10, sky: 50.0, raDeg: 150.0), .. Frames(10, sky: 50.0, raDeg: 152.0), .. Frames(3, sky: 50.0, raDeg: null)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Equal(3, clusters.Count);
        FramingCluster noCoords = Assert.Single(clusters, c => c.CentroidRaHours is null);
        Assert.Equal(3, noCoords.FrameCount);
        Assert.Equal(RotationExpression.Sky, noCoords.Expression);
    }

    [Fact]
    public void ClusterCentroid_IsItsOwn_NotTheUnitBlend()
    {
        List<XisfHeader> frames = [.. Frames(10, sky: 0.0, raDeg: 94.55), .. Frames(10, sky: 15.0, raDeg: 94.40)];

        (IReadOnlyList<FramingCluster> clusters, _) = FramingClusterer.Assign(frames);

        Assert.Equal(2, clusters.Count);
        // RA converted to hours at the boundary (FITS degrees ÷ 15).
        Assert.Equal(94.55 / 15.0, Assert.Single(clusters, c => c.FoldAngleDegrees!.Value < 5).CentroidRaHours!.Value, 6);
        Assert.Equal(94.40 / 15.0, Assert.Single(clusters, c => c.FoldAngleDegrees!.Value > 5).CentroidRaHours!.Value, 6);
    }

    [Fact]
    public void ClusterFootprint_IsDerivedFromTheFrames_WithNoBinningFactor()
    {
        // Z183 at bin 1 and the same camera at bin 2 (half the pixels, double the pixel size) cover the
        // SAME field. Multiplying by the binning would double it for 15.8% of the library.
        (IReadOnlyList<FramingCluster> bin1, _) = FramingClusterer.Assign([.. Frames(5, sky: 20.0)]);
        (IReadOnlyList<FramingCluster> bin2, _) = FramingClusterer.Assign(
            [.. Frames(5, sky: 20.0, width: 2744, height: 1836, pixelSizeUm: 4.80)]);

        FramingCluster a = Assert.Single(bin1), b = Assert.Single(bin2);
        Assert.Equal(1.423, a.FieldWidthDeg!.Value, 3);
        Assert.Equal(0.951, a.FieldHeightDeg!.Value, 3);
        Assert.Equal(a.FieldWidthDeg!.Value, b.FieldWidthDeg!.Value, 2);
        Assert.Equal(a.FieldHeightDeg!.Value, b.FieldHeightDeg!.Value, 2);
        Assert.False(a.SpansMultipleSensors);
    }

    [Fact]
    public void MixedSensorCluster_TakesTheDominantSensorsFootprint_AndIsMarked()
    {
        // IC 405's real shape: one framing holding 123 Z183 frames beside 73 Z533 ones. Camera is not a
        // clustering key, so they share a cluster — and the footprint describes the more numerous sensor
        // rather than an average of two fields, which would describe a field nobody imaged.
        List<XisfHeader> frames =
        [
            .. Frames(123, sky: 99.0),                                                       // Z183 3:2
            .. Frames(73, sky: 99.0, width: 3008, height: 3008, pixelSizeUm: 3.76),          // Z533 square
        ];

        FramingCluster c = Assert.Single(FramingClusterer.Assign(frames).Clusters);

        Assert.Equal(196, c.FrameCount);
        Assert.True(c.SpansMultipleSensors);
        Assert.Equal(1.423, c.FieldWidthDeg!.Value, 3);       // the Z183 majority's field…
        Assert.Equal(0.951, c.FieldHeightDeg!.Value, 3);      // …not 1.220 square, and not a blend
    }

    // ---- synthetic headers ---------------------------------------------------

    /// <summary>Builds <paramref name="count"/> headers with the given rotation facts; coordinates default
    /// to a fixed field unless overridden (or suppressed with <c>raDeg: null</c>).</summary>
    private static IEnumerable<XisfHeader> Frames(
        int count, double? sky = null, double? mech = null, double? raDeg = 150.0, double? decDeg = 55.0,
        int width = 5496, int height = 3672, double pixelSizeUm = 2.40)
    {
        for (int i = 0; i < count; i++)
        {
            Dictionary<string, XisfHeader.KeywordEntry> kw = new(StringComparer.OrdinalIgnoreCase);
            if (sky is double s) kw["OBJCTROT"] = new(s.ToString(CultureInfo.InvariantCulture), null);
            if (mech is double m) kw["POSANGLE"] = new(m.ToString(CultureInfo.InvariantCulture), null);
            if (raDeg is double ra) kw["RA"] = new(ra.ToString(CultureInfo.InvariantCulture), null);
            if (decDeg is double dec && raDeg is not null) kw["DEC"] = new(dec.ToString(CultureInfo.InvariantCulture), null);
            kw["FOCALLEN"] = new("531", null);
            kw["XPIXSZ"] = new(pixelSizeUm.ToString(CultureInfo.InvariantCulture), null);
            kw["YPIXSZ"] = new(pixelSizeUm.ToString(CultureInfo.InvariantCulture), null);
            yield return new XisfHeader(kw, width, height);
        }
    }
}
