using System;
using System.Collections.Generic;

namespace Astronomy.Core
{
    /// <summary>
    /// Pure geometry for rectangular fields on the sky: how much of one rectangular footprint falls inside
    /// another when the two differ in pointing, in orientation, or both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A footprint is a rectangle of a given angular width and height, centred on a sky position and rotated
    /// by a position angle. Callers that record a field's centre and orientation can ask what share of it a
    /// second field covers — the shared-area question, with no assumption about why the caller is asking.
    /// </para>
    /// <para>
    /// Both footprints are compared on a tangent plane about the reference centre. Right-ascension offsets
    /// are scaled by the cosine of the reference declination, without which east-west offsets are overstated
    /// by <c>1 / cos(dec)</c> — a factor of nearly three at <c>dec = +69&#176;</c>. The tangent-plane
    /// approximation is exact at the centre and degrades with field size; at the degree scale of typical
    /// imaging fields the error is far below the precision of a reported share.
    /// </para>
    /// <para>
    /// Rotation is a position angle in degrees. Its zero point and sense are the <em>caller's</em>: because
    /// both footprints are built with the same convention, the shared area is unchanged by which convention
    /// that is. A rectangle rotated 180&#176; about its own centre maps onto itself, so a half-turn between
    /// the two rotations yields a complete overlap without being special-cased.
    /// </para>
    /// </remarks>
    public static class FieldFootprint
    {
        private const double DegreesPerHour = 15.0;
        private const double DegToRad = Math.PI / 180.0;

        /// <summary>
        /// The share of the <em>measured</em> footprint's area that lies inside the <em>reference</em>
        /// footprint, in <c>[0, 1]</c>. Both footprints have the same angular size, so the result reflects
        /// only the difference in centre and in rotation.
        /// </summary>
        /// <param name="measuredRaHours">Measured footprint's centre RA, decimal hours.</param>
        /// <param name="measuredDecDeg">Measured footprint's centre declination, signed degrees.</param>
        /// <param name="measuredRotationDeg">Measured footprint's position angle, degrees.</param>
        /// <param name="referenceRaHours">Reference footprint's centre RA, decimal hours.</param>
        /// <param name="referenceDecDeg">Reference footprint's centre declination, signed degrees.</param>
        /// <param name="referenceRotationDeg">Reference footprint's position angle, degrees.</param>
        /// <param name="widthDeg">Shared angular width, degrees. Must be positive.</param>
        /// <param name="heightDeg">Shared angular height, degrees. Must be positive.</param>
        /// <returns>
        /// Shared area divided by the measured footprint's area. <c>1</c> when the measured footprint lies
        /// wholly inside the reference; <c>0</c> when they are disjoint.
        /// </returns>
        public static double OverlapFraction(
            double measuredRaHours, double measuredDecDeg, double measuredRotationDeg,
            double referenceRaHours, double referenceDecDeg, double referenceRotationDeg,
            double widthDeg, double heightDeg)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthDeg);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightDeg);

            // Tangent plane about the reference centre: the reference sits at the origin and the measured
            // footprint is displaced by its offset, with RA compressed by cos(dec).
            double cosDec = Math.Cos(referenceDecDeg * DegToRad);
            double deltaRaDeg = NormalizeDegrees((measuredRaHours - referenceRaHours) * DegreesPerHour);
            double dx = deltaRaDeg * cosDec;
            double dy = measuredDecDeg - referenceDecDeg;

            double[] measured = Corners(dx, dy, measuredRotationDeg, widthDeg, heightDeg);
            double[] reference = Corners(0.0, 0.0, referenceRotationDeg, widthDeg, heightDeg);

            double shared = ConvexIntersectionArea(measured, reference);
            double area = widthDeg * heightDeg;
            double fraction = shared / area;

            // Clamp away the last-bit noise of the clip: a footprint compared against itself must read
            // exactly 1, not 1.0000000000000002.
            return fraction < 0.0 ? 0.0 : fraction > 1.0 ? 1.0 : fraction;
        }

        /// <summary>
        /// The four corners of a rectangle centred at <paramref name="cx"/>,<paramref name="cy"/>, as
        /// <c>[x0, y0, x1, y1, x2, y2, x3, y3]</c> wound consistently.
        /// </summary>
        private static double[] Corners(double cx, double cy, double rotationDeg, double widthDeg, double heightDeg)
        {
            double theta = rotationDeg * DegToRad;
            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);
            double hw = widthDeg / 2.0;
            double hh = heightDeg / 2.0;

            // Counter-clockwise in the (x, y) plane before rotation; rotation preserves the winding.
            ReadOnlySpan<double> sx = [-hw, hw, hw, -hw];
            ReadOnlySpan<double> sy = [-hh, -hh, hh, hh];

            double[] pts = new double[8];
            for (int i = 0; i < 4; i++)
            {
                pts[2 * i] = Math.FusedMultiplyAdd(sx[i], cos, -sy[i] * sin) + cx;
                pts[2 * i + 1] = Math.FusedMultiplyAdd(sx[i], sin, sy[i] * cos) + cy;
            }
            return pts;
        }

        /// <summary>
        /// Area shared by two convex polygons: Sutherland&#8211;Hodgman clipping of
        /// <paramref name="subject"/> against every edge of <paramref name="clip"/>, then the shoelace area.
        /// Exact for convex inputs &#8212; no sampling, and deterministic.
        /// </summary>
        private static double ConvexIntersectionArea(double[] subject, double[] clip)
        {
            List<(double X, double Y)> poly = [];
            for (int i = 0; i < subject.Length; i += 2) poly.Add((subject[i], subject[i + 1]));

            int clipCount = clip.Length / 2;
            for (int e = 0; e < clipCount && poly.Count > 0; e++)
            {
                (double ax, double ay) = (clip[2 * e], clip[2 * e + 1]);
                int next = (e + 1) % clipCount;
                (double bx, double by) = (clip[2 * next], clip[2 * next + 1]);

                List<(double X, double Y)> clipped = [];
                for (int i = 0; i < poly.Count; i++)
                {
                    (double X, double Y) current = poly[i];
                    (double X, double Y) previous = poly[(i - 1 + poly.Count) % poly.Count];

                    double sideCurrent = Side(ax, ay, bx, by, current.X, current.Y);
                    double sidePrevious = Side(ax, ay, bx, by, previous.X, previous.Y);

                    if (sideCurrent >= 0.0)
                    {
                        if (sidePrevious < 0.0)
                            clipped.Add(Intersect(previous, current, ax, ay, bx, by));
                        clipped.Add(current);
                    }
                    else if (sidePrevious >= 0.0)
                    {
                        clipped.Add(Intersect(previous, current, ax, ay, bx, by));
                    }
                }
                poly = clipped;
            }

            if (poly.Count < 3) return 0.0;

            double twiceArea = 0.0;
            for (int i = 0; i < poly.Count; i++)
            {
                (double X, double Y) p = poly[i];
                (double X, double Y) q = poly[(i + 1) % poly.Count];
                twiceArea += (p.X * q.Y) - (q.X * p.Y);
            }
            return Math.Abs(twiceArea) / 2.0;
        }

        /// <summary>Cross product placing a point left (positive) or right (negative) of a directed edge.</summary>
        private static double Side(double ax, double ay, double bx, double by, double px, double py) =>
            ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));

        /// <summary>Where the segment <paramref name="p"/>&#8211;<paramref name="q"/> crosses the infinite edge.</summary>
        private static (double X, double Y) Intersect(
            (double X, double Y) p, (double X, double Y) q,
            double ax, double ay, double bx, double by)
        {
            double dxEdge = bx - ax;
            double dyEdge = by - ay;
            double dxSeg = q.X - p.X;
            double dySeg = q.Y - p.Y;

            // Solve cross(p + t*seg - a, edge) = 0 for t. Numerator and denominator must carry the SAME sign
            // convention: t = cross(a - p, edge) / cross(seg, edge).
            double denominator = (dxSeg * dyEdge) - (dySeg * dxEdge);
            if (denominator == 0.0) return q;   // parallel; the caller's side test already admitted q

            double t = (((ax - p.X) * dyEdge) - ((ay - p.Y) * dxEdge)) / denominator;
            return (p.X + (t * dxSeg), p.Y + (t * dySeg));
        }

        /// <summary>Wraps a degree difference into <c>[-180, 180)</c> so an RA pair spanning 0h is measured the short way.</summary>
        private static double NormalizeDegrees(double degrees)
        {
            double d = degrees % 360.0;
            if (d >= 180.0) d -= 360.0;
            else if (d < -180.0) d += 360.0;
            return d;
        }
    }
}
