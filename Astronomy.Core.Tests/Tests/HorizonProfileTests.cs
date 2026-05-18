using System;
using Astronomy.Core.Horizons;
using Xunit;

namespace Astronomy.Core.Tests.Tests
{
    // Direct tests for the three IHorizonProfile implementations. The
    // RiseSet / VisibilityWindows / CoarseVisibility tests cover them at
    // the consumer level; these pin the AltitudeAt / MinAltitude semantics
    // (linear interpolation, stepped sectors, azimuth wrap).
    public class HorizonProfileTests
    {
        // ---- ScalarHorizonProfile ----

        [Fact]
        public void Scalar_AltitudeAt_IsConstantAtEveryAzimuth()
        {
            var p = new ScalarHorizonProfile(30.0);

            Assert.Equal(30.0, p.AltitudeAt(0.0));
            Assert.Equal(30.0, p.AltitudeAt(90.0));
            Assert.Equal(30.0, p.AltitudeAt(180.0));
            Assert.Equal(30.0, p.AltitudeAt(359.999));
            Assert.Equal(30.0, p.AltitudeAt(720.0));   // wraps
            Assert.Equal(30.0, p.AltitudeAt(-45.0));   // negative wraps
        }

        [Fact]
        public void Scalar_MinAltitude_EqualsConfiguredAltitude()
        {
            Assert.Equal(15.0, new ScalarHorizonProfile(15.0).MinAltitude);
            Assert.Equal(0.0,  new ScalarHorizonProfile(0.0).MinAltitude);
            Assert.Equal(-5.0, new ScalarHorizonProfile(-5.0).MinAltitude);
        }

        // ---- PolylineHorizonProfile ----

        [Fact]
        public void Polyline_AltitudeAt_SamplePoint_ReturnsExactAltitude()
        {
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0, 180.0, 270.0 },
                altitudesDeg: new[] { 20.0,  40.0,  20.0,  40.0 });

            Assert.Equal(20.0, p.AltitudeAt(0.0));
            Assert.Equal(40.0, p.AltitudeAt(90.0));
            Assert.Equal(20.0, p.AltitudeAt(180.0));
            Assert.Equal(40.0, p.AltitudeAt(270.0));
        }

        [Fact]
        public void Polyline_AltitudeAt_BetweenSamples_LinearlyInterpolates()
        {
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0 },
                altitudesDeg: new[] { 20.0,  40.0 });

            // Halfway between az 0 and az 90 is altitude 30 (linear).
            Assert.Equal(30.0, p.AltitudeAt(45.0), precision: 9);
            // A quarter of the way is altitude 25.
            Assert.Equal(25.0, p.AltitudeAt(22.5), precision: 9);
        }

        [Fact]
        public void Polyline_AltitudeAt_WrapsAzimuthAcross360()
        {
            // Two-sample polyline at 350 and 10. Between 350 and 10 the
            // azimuth crosses 360; AltitudeAt(0) must interpolate across that
            // wrap (halfway between 350 and 10 deg is 0, midpoint of altitudes).
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] { 350.0,  10.0 },
                altitudesDeg: new[] {  10.0,  30.0 });

            Assert.Equal(20.0, p.AltitudeAt(0.0), precision: 9);
        }

        [Fact]
        public void Polyline_AltitudeAt_NegativeAzimuth_Wraps()
        {
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0 },
                altitudesDeg: new[] { 20.0,  40.0 });

            Assert.Equal(p.AltitudeAt(45.0), p.AltitudeAt(45.0 - 360.0), precision: 9);
            Assert.Equal(p.AltitudeAt(45.0), p.AltitudeAt(45.0 + 360.0), precision: 9);
        }

        [Fact]
        public void Polyline_MinAltitude_IsLowestSample()
        {
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0, 180.0 },
                altitudesDeg: new[] { 25.0,  40.0,  10.0 });

            Assert.Equal(10.0, p.MinAltitude);
        }

        [Fact]
        public void Polyline_SinglePoint_AltitudeAtIsConstant()
        {
            var p = new PolylineHorizonProfile(
                azimuthsDeg:  new[] { 45.0 },
                altitudesDeg: new[] { 30.0 });

            Assert.Equal(30.0, p.AltitudeAt(0.0));
            Assert.Equal(30.0, p.AltitudeAt(180.0));
            Assert.Equal(30.0, p.MinAltitude);
        }

        [Fact]
        public void Polyline_NullArgs_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PolylineHorizonProfile(null, new[] { 20.0 }));
            Assert.Throws<ArgumentNullException>(() =>
                new PolylineHorizonProfile(new[] { 0.0 }, null));
        }

        [Fact]
        public void Polyline_LengthMismatch_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new PolylineHorizonProfile(new[] { 0.0, 90.0 }, new[] { 20.0 }));
        }

        [Fact]
        public void Polyline_EmptyArrays_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new PolylineHorizonProfile(new double[0], new double[0]));
        }

        // ---- ObstructionTableHorizonProfile ----

        [Fact]
        public void Obstruction_AltitudeAt_ReturnsLastSampleAltitudeUpToNext()
        {
            // Stepped sectors: sample at az 0 covers [0, 90), az 90 covers [90, 180), etc.
            var p = new ObstructionTableHorizonProfile(new[]
            {
                ( AzimuthDeg:   0.0, AltitudeDeg: 20.0 ),
                ( AzimuthDeg:  90.0, AltitudeDeg: 40.0 ),
                ( AzimuthDeg: 180.0, AltitudeDeg: 60.0 ),
                ( AzimuthDeg: 270.0, AltitudeDeg: 30.0 ),
            });

            Assert.Equal(20.0, p.AltitudeAt(  0.0));
            Assert.Equal(20.0, p.AltitudeAt( 45.0));
            Assert.Equal(20.0, p.AltitudeAt( 89.999));
            Assert.Equal(40.0, p.AltitudeAt( 90.0));
            Assert.Equal(40.0, p.AltitudeAt(135.0));
            Assert.Equal(60.0, p.AltitudeAt(180.0));
            Assert.Equal(30.0, p.AltitudeAt(270.0));
            Assert.Equal(30.0, p.AltitudeAt(355.0));
        }

        [Fact]
        public void Obstruction_AltitudeAt_BeforeFirstSample_WrapsToLast()
        {
            // Samples at 90, 180, 270. Az = 30 falls before the smallest -- it's
            // covered by the sector that wraps from 270 around 360 back to 90.
            var p = new ObstructionTableHorizonProfile(new[]
            {
                ( AzimuthDeg:  90.0, AltitudeDeg: 20.0 ),
                ( AzimuthDeg: 180.0, AltitudeDeg: 30.0 ),
                ( AzimuthDeg: 270.0, AltitudeDeg: 50.0 ),
            });

            Assert.Equal(50.0, p.AltitudeAt(30.0));
            Assert.Equal(50.0, p.AltitudeAt( 0.0));
            Assert.Equal(50.0, p.AltitudeAt(89.999));
        }

        [Fact]
        public void Obstruction_AltitudeAt_NegativeAzimuth_Wraps()
        {
            var p = new ObstructionTableHorizonProfile(new[]
            {
                ( AzimuthDeg:   0.0, AltitudeDeg: 10.0 ),
                ( AzimuthDeg: 180.0, AltitudeDeg: 50.0 ),
            });

            Assert.Equal(p.AltitudeAt(45.0), p.AltitudeAt(45.0 - 360.0));
            Assert.Equal(p.AltitudeAt(200.0), p.AltitudeAt(200.0 - 360.0));
        }

        [Fact]
        public void Obstruction_MinAltitude_IsLowestSample()
        {
            var p = new ObstructionTableHorizonProfile(new[]
            {
                ( AzimuthDeg:   0.0, AltitudeDeg: 25.0 ),
                ( AzimuthDeg:  90.0, AltitudeDeg: 40.0 ),
                ( AzimuthDeg: 180.0, AltitudeDeg:  5.0 ),
                ( AzimuthDeg: 270.0, AltitudeDeg: 30.0 ),
            });

            Assert.Equal(5.0, p.MinAltitude);
        }

        [Fact]
        public void Obstruction_NullSamples_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ObstructionTableHorizonProfile(null));
        }

        [Fact]
        public void Obstruction_EmptySamples_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new ObstructionTableHorizonProfile(
                    new System.Collections.Generic.List<(double, double)>()));
        }

        // ---- MaxOfHorizonProfile ----

        [Fact]
        public void Max_AltitudeAt_ReturnsHigherOfTwoComponentsAtEveryAzimuth()
        {
            var a = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0, 180.0, 270.0 },
                altitudesDeg: new[] { 20.0,  40.0,  10.0,  35.0 });
            var b = new ScalarHorizonProfile(25.0);
            var max = new MaxOfHorizonProfile(a, b);

            Assert.Equal(25.0, max.AltitudeAt(  0.0));   // scalar wins
            Assert.Equal(40.0, max.AltitudeAt( 90.0));   // polyline wins
            Assert.Equal(25.0, max.AltitudeAt(180.0));   // scalar wins
            Assert.Equal(35.0, max.AltitudeAt(270.0));   // polyline wins
        }

        [Fact]
        public void Max_OverScalarBelow_IsIdentityToOther()
        {
            var poly = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0, 180.0 },
                altitudesDeg: new[] { 20.0,  40.0,  30.0 });
            var floor = new ScalarHorizonProfile(0.0);
            var max = new MaxOfHorizonProfile(poly, floor);

            Assert.Equal(20.0, max.AltitudeAt(  0.0), precision: 9);
            Assert.Equal(40.0, max.AltitudeAt( 90.0), precision: 9);
            Assert.Equal(30.0, max.AltitudeAt(180.0), precision: 9);
            // Midpoint between 90 and 180: polyline interps linearly from 40 -> 30 = 35.
            Assert.Equal(35.0, max.AltitudeAt(135.0), precision: 9);
        }

        [Fact]
        public void Max_IsCommutative()
        {
            var a = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0 },
                altitudesDeg: new[] { 20.0,  40.0 });
            var b = new ScalarHorizonProfile(30.0);

            var ab = new MaxOfHorizonProfile(a, b);
            var ba = new MaxOfHorizonProfile(b, a);

            for (double az = 0; az < 360; az += 15)
                Assert.Equal(ab.AltitudeAt(az), ba.AltitudeAt(az), precision: 9);
        }

        [Fact]
        public void Max_MinAltitude_IsMaxOfComponentMinAltitudes()
        {
            var a = new ScalarHorizonProfile(10.0);
            var b = new ScalarHorizonProfile(25.0);
            Assert.Equal(25.0, new MaxOfHorizonProfile(a, b).MinAltitude);

            var poly = new PolylineHorizonProfile(
                azimuthsDeg:  new[] {  0.0,  90.0 },
                altitudesDeg: new[] { 20.0,  40.0 });        // MinAltitude = 20
            var floor = new ScalarHorizonProfile(30.0);      // MinAltitude = 30
            Assert.Equal(30.0, new MaxOfHorizonProfile(poly, floor).MinAltitude);
        }

        [Fact]
        public void Max_NullArgs_Throws()
        {
            var p = new ScalarHorizonProfile(10.0);
            Assert.Throws<ArgumentNullException>(() => new MaxOfHorizonProfile(null, p));
            Assert.Throws<ArgumentNullException>(() => new MaxOfHorizonProfile(p, null));
        }
    }
}
