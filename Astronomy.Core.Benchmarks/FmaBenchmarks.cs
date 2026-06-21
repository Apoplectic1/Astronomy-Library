using System;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Attributes;

namespace Astronomy.Core.Benchmarks
{
    // v2 design — written after v1 results showed the JIT was hoisting / constant-folding
    // every case. To expose the real per-call cost of FMA vs separate mul+add, two
    // properties must hold simultaneously:
    //
    //   1. The polynomial input `x` must vary every iteration so the JIT cannot
    //      precompute the polynomial result and hoist it out of the loop.
    //   2. Iterations should be otherwise independent (the only cross-iter dep is the
    //      accumulator) so out-of-order execution can overlap successive polynomials.
    //      That way the measurement reflects either:
    //        a. The latency of the chain inside one polynomial (if ILP is saturated), or
    //        b. The throughput of the FMA/mul/add units (if iterations pipeline freely).
    //
    // Each iter increments `x` by a tiny perturbation. That introduces a real but
    // tiny loop-carried dep on `x` that the JIT cannot collapse, while keeping `x`
    // in a numerically stable range.
    //
    // Hardware reminders for interpreting results on Zen 4 (Ryzen 9 7950X):
    //   * mulsd latency 3 cyc, throughput 1/cyc
    //   * addsd latency 3 cyc, throughput 1/cyc
    //   * vfmadd*sd latency 4 cyc, throughput 1/cyc (2 FMA units)
    //   * So a fully serialized 4-term Horner is:
    //       vanilla: 4 * (mul + add) = 4 * (3 + 3) = 24 cyc / poly = ~5.3 ns
    //       FMA:     4 * 4           = 16 cyc / poly = ~3.6 ns
    //     Predicted ratio: 0.67. Real measurements will be lower because ILP across
    //     iterations overlaps multiple polynomials; the relative gap should remain.
    [MemoryDiagnoser]
    public class FmaBenchmarks
    {
        // Starting `x` value. Field, not const, so the JIT must read it.
        private double _x = 0.25;

        // Spherical-altitude inputs precomputed (we're studying the arithmetic,
        // not Math.Sin/Cos cost).
        private double _sinPhi, _cosPhi, _sinDelta, _cosDelta;

        [GlobalSetup]
        public void Setup()
        {
            _sinPhi   = Math.Sin(0.7032);
            _cosPhi   = Math.Cos(0.7032);
            _sinDelta = Math.Sin(0.7203);
            _cosDelta = Math.Cos(0.7203);

            Console.WriteLine($"[FmaBenchmarks] Fma.IsSupported = {Fma.IsSupported}");
        }

        // ---- 4-term Horner polynomial, per-iter varying x ----
        //
        // Coefficients ape MeeusUtility.MeanObliquityDeg. Each iter:
        //   * Perturbs x by 1e-9 (loop-carried, tiny but non-hoistable).
        //   * Evaluates the polynomial chain on the current x.
        //   * Adds to sum (the only inter-iter accumulator).

        [Benchmark(Baseline = true, OperationsPerInvoke = 1000)]
        public double Horner4_Vanilla()
        {
            double sum = 0.0;
            double x = _x;
            for (int i = 0; i < 1000; i++)
            {
                x += 1e-9;
                double r = 0.001813;
                r = r * x + (-0.00059);
                r = r * x + (-46.8150);
                r = r * x + 84381.448;
                sum += r;
            }
            return sum;
        }

        [Benchmark(OperationsPerInvoke = 1000)]
        public double Horner4_Fma()
        {
            double sum = 0.0;
            double x = _x;
            for (int i = 0; i < 1000; i++)
            {
                x += 1e-9;
                double r = 0.001813;
                r = Math.FusedMultiplyAdd(r, x, -0.00059);
                r = Math.FusedMultiplyAdd(r, x, -46.8150);
                r = Math.FusedMultiplyAdd(r, x, 84381.448);
                sum += r;
            }
            return sum;
        }

        // ---- 8-term Horner polynomial ----
        //
        // Longer chain = more chained mul-adds per iter, so the FMA latency
        // advantage gets multiplied. With ILP saturated this should show the
        // theoretical 16-cyc-per-FMA-chain vs 24-cyc-per-mul+add ratio (~0.67).

        [Benchmark(OperationsPerInvoke = 1000)]
        public double Horner8_Vanilla()
        {
            double sum = 0.0;
            double x = _x;
            for (int i = 0; i < 1000; i++)
            {
                x += 1e-9;
                double r = 1.0;
                r = r * x + 2.0;
                r = r * x + 3.0;
                r = r * x + 4.0;
                r = r * x + 5.0;
                r = r * x + 6.0;
                r = r * x + 7.0;
                r = r * x + 8.0;
                r = r * x + 9.0;
                sum += r;
            }
            return sum;
        }

        [Benchmark(OperationsPerInvoke = 1000)]
        public double Horner8_Fma()
        {
            double sum = 0.0;
            double x = _x;
            for (int i = 0; i < 1000; i++)
            {
                x += 1e-9;
                double r = 1.0;
                r = Math.FusedMultiplyAdd(r, x, 2.0);
                r = Math.FusedMultiplyAdd(r, x, 3.0);
                r = Math.FusedMultiplyAdd(r, x, 4.0);
                r = Math.FusedMultiplyAdd(r, x, 5.0);
                r = Math.FusedMultiplyAdd(r, x, 6.0);
                r = Math.FusedMultiplyAdd(r, x, 7.0);
                r = Math.FusedMultiplyAdd(r, x, 8.0);
                r = Math.FusedMultiplyAdd(r, x, 9.0);
                sum += r;
            }
            return sum;
        }

        // ---- Spherical altitude (a*b + c*d*e), varying e per iter ----
        //
        // Mirrors TargetGeometry.AltitudeAtHourAngle's kernel. The varying input
        // (cosHa) is the part that actually varies in production code anyway,
        // so this is the most faithful to real chart-paint cost.
        //
        // Vanilla critical path per iter: cosPhi*cosDelta (hoisted), then
        //   t = cpcd * cosHa (3 cyc), then sum += sinPhi*sinDelta + t (3 cyc add).
        // FMA critical path per iter: cosPhi*cosDelta (hoisted),
        //   t = FMA(cpcd, cosHa, sinPhi*sinDelta) (4 cyc), sum += t (3 cyc).
        // The FMA collapses two dependent ops into one but the bottleneck on this
        // shape is `sum +=`, so the gap will be small.

        [Benchmark(OperationsPerInvoke = 1000)]
        public double SphericalAlt_Vanilla()
        {
            double sum = 0.0;
            double cosHa = 0.5;
            for (int i = 0; i < 1000; i++)
            {
                cosHa += 1e-9;
                sum += _sinPhi * _sinDelta + _cosPhi * _cosDelta * cosHa;
            }
            return sum;
        }

        [Benchmark(OperationsPerInvoke = 1000)]
        public double SphericalAlt_Fma()
        {
            double sum = 0.0;
            double cosHa = 0.5;
            for (int i = 0; i < 1000; i++)
            {
                cosHa += 1e-9;
                sum += Math.FusedMultiplyAdd(_cosPhi * _cosDelta, cosHa, _sinPhi * _sinDelta);
            }
            return sum;
        }
    }
}
