using Astronomy.XISF;

namespace Astronomy.Catalog.Scan;

/// <summary>
/// Groups one scan unit's frames into <see cref="FramingCluster"/>s. Frames partition by rotation
/// expression first (sky / mechanical / unknown — the three are mutually incomparable), then within the
/// sky and mechanical partitions: single-linkage gap clustering on the angle folded mod 180°
/// (<see cref="FramingCluster.RotationToleranceDegrees"/>), then single-linkage field-center clustering
/// within each angle group (<see cref="FramingCluster.CentroidLinkDegrees"/>). Ordering angle-fold first
/// and center-split second makes a pier flip merge (identical footprint) while 180°-apart frames whose
/// centers genuinely differ still separate — the centroid guard as a consequence of ordering rather than
/// a special rule.
/// <para>
/// A frame missing a fact is never defaulted into a value that could cluster as though recorded: frames
/// without coordinates join their angle group's sole center group or form their own; frames with no
/// rotation at all join the unit's sole cluster or form their own Unknown cluster.
/// </para>
/// </summary>
internal static class FramingClusterer
{
    /// <summary>Assigns every header to a framing cluster. Returns the unit's clusters (largest first,
    /// ties by angle) and, parallel to <paramref name="headers"/>, each frame's cluster ordinal.</summary>
    internal static (IReadOnlyList<FramingCluster> Clusters, int[] Assignment) Assign(
        IReadOnlyList<XisfHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count == 0) return ([], []);

        List<int> sky = [], mech = [], unknown = [];
        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i].RotatorSkyAngleDeg is not null) sky.Add(i);
            else if (headers[i].RotatorPosAngleDeg is not null) mech.Add(i);
            else unknown.Add(i);
        }

        List<(RotationExpression Expression, List<int> Members)> groups = [];
        BuildExpressionGroups(headers, sky, RotationExpression.Sky, groups);
        BuildExpressionGroups(headers, mech, RotationExpression.Mechanical, groups);

        // Frames with no rotation at all: attributable only when the unit has exactly one cluster to
        // join (or none — the whole unit is unknown); ambiguous otherwise, so they form their own.
        if (unknown.Count > 0)
        {
            if (groups.Count == 1) groups[0].Members.AddRange(unknown);
            else groups.Add((RotationExpression.Unknown, unknown));
        }

        // Stable ordinals: largest cluster first, ties by fold angle then expression.
        List<(RotationExpression Expression, List<int> Members, double? Fold)> ordered = groups
            .Select(g => (g.Expression, g.Members, Fold: GroupFoldAngle(headers, g.Expression, g.Members)))
            .OrderByDescending(g => g.Members.Count)
            .ThenBy(g => g.Fold ?? double.MaxValue)
            .ThenBy(g => g.Expression)
            .ToList();

        FramingCluster[] clusters = new FramingCluster[ordered.Count];
        int[] assignment = new int[headers.Count];
        for (int ord = 0; ord < ordered.Count; ord++)
        {
            (RotationExpression expression, List<int> members, double? fold) = ordered[ord];
            (double? ra, double? dec) = GroupCentroid(headers, members);
            (double? fieldW, double? fieldH, bool mixedSensor) = GroupFootprint(headers, members);
            clusters[ord] = new FramingCluster(
                ord, expression, fold, ra, dec, members.Count, fieldW, fieldH, mixedSensor);
            foreach (int i in members) assignment[i] = ord;
        }
        return (clusters, assignment);
    }

    // Angle-fold groups first, then a center split inside each: members without coordinates join the
    // angle group's sole center group, or form one no-coordinates group of their own per angle group.
    private static void BuildExpressionGroups(
        IReadOnlyList<XisfHeader> headers, List<int> members, RotationExpression expression,
        List<(RotationExpression, List<int>)> groups)
    {
        if (members.Count == 0) return;

        foreach (List<int> angleGroup in GapClusterFold180(headers, members, expression))
        {
            List<int> withCoords = [], noCoords = [];
            foreach (int i in angleGroup)
                (headers[i].RaDegrees is not null && headers[i].DecDegrees is not null ? withCoords : noCoords).Add(i);

            List<List<int>> centerGroups = SingleLinkageByCenter(headers, withCoords);
            if (centerGroups.Count <= 1)
            {
                // Spatially unsplit (or coordinate-less entirely): the angle group is one framing.
                groups.Add((expression, angleGroup));
                continue;
            }
            foreach (List<int> centerGroup in centerGroups) groups.Add((expression, centerGroup));
            if (noCoords.Count > 0) groups.Add((expression, noCoords));
        }
    }

    // Single-linkage clustering on the fold-180 circle: sort the folded angles, split at gaps larger
    // than the rotation tolerance (including the wrap-around gap).
    private static List<List<int>> GapClusterFold180(
        IReadOnlyList<XisfHeader> headers, List<int> members, RotationExpression expression)
    {
        List<(double Fold, int Index)> folded = members
            .Select(i => (FramingCluster.Fold180(Angle(headers[i], expression)), i))
            .OrderBy(x => x.Item1)
            .ToList();

        int n = folded.Count;
        if (n == 1) return [[folded[0].Index]];

        // Gap after position k (wrapping from last back to first around the 180° circle).
        double GapAfter(int k) => k == n - 1
            ? folded[0].Fold + 180.0 - folded[n - 1].Fold
            : folded[k + 1].Fold - folded[k].Fold;

        int maxGapAt = 0;
        bool anySplit = false;
        for (int k = 0; k < n; k++)
        {
            if (GapAfter(k) > FramingCluster.RotationToleranceDegrees) anySplit = true;
            if (GapAfter(k) > GapAfter(maxGapAt)) maxGapAt = k;
        }
        if (!anySplit) return [[.. folded.Select(x => x.Index)]];

        // Start just past the widest gap so the wrap seam falls between clusters, then split at every
        // over-tolerance gap.
        List<List<int>> result = [];
        List<int> current = [];
        for (int step = 0; step < n; step++)
        {
            int k = (maxGapAt + 1 + step) % n;
            current.Add(folded[k].Index);
            if (GapAfter(k) > FramingCluster.RotationToleranceDegrees)
            {
                result.Add(current);
                current = [];
            }
        }
        if (current.Count > 0) result.Add(current);
        return result;
    }

    // Single-linkage union-find over pairwise great-circle separations ≤ CentroidLinkDegrees.
    private static List<List<int>> SingleLinkageByCenter(IReadOnlyList<XisfHeader> headers, List<int> members)
    {
        if (members.Count == 0) return [];
        int n = members.Count;
        int[] parent = [.. Enumerable.Range(0, n)];

        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }

        for (int a = 0; a < n; a++)
        {
            XisfHeader ha = headers[members[a]];
            for (int b = a + 1; b < n; b++)
            {
                XisfHeader hb = headers[members[b]];
                double sep = SeparationDegrees(
                    ha.RaDegrees!.Value, ha.DecDegrees!.Value, hb.RaDegrees!.Value, hb.DecDegrees!.Value);
                if (sep <= FramingCluster.CentroidLinkDegrees) Union(a, b);
            }
        }

        return members
            .Select((m, k) => (Member: m, Root: Find(k)))
            .GroupBy(x => x.Root)
            .Select(g => g.Select(x => x.Member).ToList())
            .ToList();
    }

    private static double Angle(XisfHeader h, RotationExpression expression) =>
        expression == RotationExpression.Sky ? h.RotatorSkyAngleDeg!.Value : h.RotatorPosAngleDeg!.Value;

    // Circular mean on the fold-180 circle (via the doubled angle so 0.02° and 179.95° average to ~0°,
    // not ~90°). Null for Unknown groups. Members not carrying the expression's angle are skipped — a
    // unit whose sole cluster absorbed its rotation-less frames still averages only the recorded angles.
    private static double? GroupFoldAngle(
        IReadOnlyList<XisfHeader> headers, RotationExpression expression, List<int> members)
    {
        if (expression == RotationExpression.Unknown) return null;
        double s = 0, c = 0;
        foreach (int i in members)
        {
            double? angle = expression == RotationExpression.Sky
                ? headers[i].RotatorSkyAngleDeg
                : headers[i].RotatorPosAngleDeg;
            if (angle is not double a) continue;
            double doubled = FramingCluster.Fold180(a) * 2.0 * Math.PI / 180.0;
            s += Math.Sin(doubled);
            c += Math.Cos(doubled);
        }
        double mean = Math.Atan2(s, c) * 180.0 / Math.PI / 2.0;   // Atan2(0,0) is 0, so a degenerate cancel still folds cleanly
        return FramingCluster.Fold180(mean);
    }

    // Median RA/Dec of the members carrying coordinates, converted to library convention (RA hours).
    // Median matches the unit-consensus approach (robust to stragglers; wrap caveat shared with it).
    private static (double? RaHours, double? DecDegrees) GroupCentroid(
        IReadOnlyList<XisfHeader> headers, List<int> members)
    {
        List<double> ras = [], decs = [];
        foreach (int i in members)
        {
            if (headers[i].RaDegrees is double ra && headers[i].DecDegrees is double dec)
            {
                ras.Add(ra);
                decs.Add(dec);
            }
        }
        if (ras.Count == 0) return (null, null);

        double raHours = Median(ras) / 15.0 % 24.0;
        if (raHours < 0) raHours += 24.0;
        return (raHours, Math.Clamp(Median(decs), -90.0, 90.0));
    }

    // The field this cluster covers, from its DOMINANT sensor geometry — the most numerous
    // (pixel width, height) among its members — plus whether the members span more than one such geometry.
    // Camera is not a clustering key, so one framing can hold two sensors (measured: one mechanical cluster
    // holding 123 frames of 5496x3672 beside 73 of 3008x3008). Dimensions from two sensors are never blended:
    // an averaged rectangle would describe a field that was never imaged. Absent when no frame of the
    // dominant sensor carries the focal length and pixel size the derivation needs — absent, never defaulted.
    private static (double? WidthDeg, double? HeightDeg, bool SpansMultipleSensors) GroupFootprint(
        IReadOnlyList<XisfHeader> headers, List<int> members)
    {
        if (members.Count == 0) return (null, null, false);

        List<IGrouping<(int W, int H), int>> bySensor = [.. members
            .GroupBy(i => (W: headers[i].PixelWidth, H: headers[i].PixelHeight))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => (long)g.Key.W * g.Key.H)];

        bool spansMultipleSensors = bySensor.Count > 1;

        // Within the dominant sensor, the first member that can express a field size decides it; they share
        // optics in practice, so this is a pick, not an average.
        foreach (int i in bySensor[0])
        {
            if (headers[i].FieldWidthDeg is double w && headers[i].FieldHeightDeg is double h && w > 0 && h > 0)
                return (w, h, spansMultipleSensors);
        }
        return (null, null, spansMultipleSensors);
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values.OrderBy(d => d)];
        int mid = sorted.Length / 2;
        return (sorted.Length & 1) == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    // Great-circle separation in degrees; inputs in degrees (FITS convention at this boundary).
    private static double SeparationDegrees(double ra1, double dec1, double ra2, double dec2)
    {
        const double Rad = Math.PI / 180.0;
        double dRa = (ra2 - ra1) * Rad;
        double dDec = (dec2 - dec1) * Rad;
        double h = Math.Sin(dDec / 2) * Math.Sin(dDec / 2)
                 + Math.Cos(dec1 * Rad) * Math.Cos(dec2 * Rad) * Math.Sin(dRa / 2) * Math.Sin(dRa / 2);
        return 2.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(h))) / Rad;
    }
}
