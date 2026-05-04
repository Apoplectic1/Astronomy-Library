using System;
using Astronomy.Core;
using Astronomy.Core.Astrometry;
using Astronomy.Core.Astrometry.Meeus;
using Astronomy.Core.Brightness;
using Astronomy.Core.Horizons;
using Astronomy.Core.Locations;
using Astronomy.Core.Moon;
using Astronomy.Core.Night;
using Astronomy.Core.Session;
using Astronomy.Core.Targets;
using Astronomy.Core.Time;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Astronomy.Core.Tests.Benchmarks
{
    // Per-call cost of the library primitives that dominate the chart cache prepare loop
    // (44 targets x ~150 moon-clear samples per night x 365 nights ~= 2.4M moon-position
    // calls per portfolio refresh) and the chart paint cycle. Establishes a baseline for
    // targeted optimizations: AggressiveInlining, int[,] -> flat array in MoonPosition,
    // FusedMultiplyAdd, etc. Pin all measurements with [MemoryDiagnoser] so the allocation
    // column shows where the GC pressure originates.
    //
    // Uses InProcessEmit toolchain because the test project transitively references
    // Astronomy.PCL.Native (vcxproj), which dotnet CLI can't build. InProcessEmit skips
    // BDN's separate-process generation and runs benchmarks in this assembly directly.
    [MemoryDiagnoser]
    [Config(typeof(InProcessConfig))]
    public class HotPathBenchmarks
    {
        private static readonly Func<double, double> SinAltQuality =
            alt => Math.Sin(alt * Math.PI / 180.0);

        private Target _target;
        private Location _location;
        private NightWindow _night;
        private IHorizonProfile _horizon;
        private DateTime _utc;
        private double _jd;
        private double _lstDeg;
        private ObserverInfo _observer;

        // Solar / lunar altaz cached for SkyBrightness benchmarks so each iteration
        // measures only the K-S formula cost, not its setup.
        private double _moonAlt, _moonAz, _moonPhaseDeg, _sunAlt;

        [GlobalSetup]
        public void Setup()
        {
            _target = Target.Default;
            _location = Location.Default.With(
                dateTime: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));
            _night = NightCalculator.ComputeNight(_location);
            _horizon = new ScalarHorizonProfile(20.0);
            // Mid-night anchor inside the visibility window for a typical case.
            _utc = new DateTime(2026, 11, 15, 4, 0, 0, DateTimeKind.Utc);
            _jd = JulianDate.FromUtc(_utc);

            double lonEast = _location.West ? -_location.Longitude : _location.Longitude;
            _lstDeg = SiderealTime.Local(_utc, lonEast) * 15.0;

            double latSigned = _location.North ? _location.Latitude : -_location.Latitude;
            _observer = new ObserverInfo(latSigned, lonEast, _location.Elevation);

            _moonAlt = AstroUtil.GetMoonAltitude(_utc, _observer);
            _moonAz  = AstroUtil.GetMoonAzimuth (_utc, _observer);
            _moonPhaseDeg = SkyBrightness.PhaseAngleDegFromAgeDays(LunarAge.DaysAt(_utc));
            _sunAlt = AstroUtil.GetSunAltitude(_utc, _observer);
        }

        // ---- Geometry primitives ----

        // Pure-trig per-call cost; no allocations expected. Called transitively by every
        // higher-level path (Simpson eval, AltitudeCurve.Sample, etc.).
        [Benchmark]
        public double TargetGeometry_AltitudeAtHourAngle()
        {
            return TargetGeometry.AltitudeAtHourAngle(2.5, 40.28, 41.27);
        }

        [Benchmark]
        public double TargetGeometry_AzimuthAtHourAngle()
        {
            return TargetGeometry.AzimuthAtHourAngle(2.5, 40.28, 41.27);
        }

        [Benchmark]
        public double SiderealTime_Local()
        {
            return SiderealTime.Local(_utc, -75.0);
        }

        // ---- Astrometry / Moon (public surface; covers the internal Meeus primitives) ----

        // Public sun-altitude wrapper -- exercises SunPosition.Apparent + AltAzFromRaDec.
        [Benchmark]
        public double AstroUtil_GetSunAltitude()
        {
            return AstroUtil.GetSunAltitude(_utc, _observer);
        }

        // Public moon-altitude wrapper -- exercises MoonPosition.Topocentric + AltAz path.
        [Benchmark]
        public double AstroUtil_GetMoonAltitude()
        {
            return AstroUtil.GetMoonAltitude(_utc, _observer);
        }

        // Chart-cache hot path: 10-min sweep through the night calls this once per sample
        // per target. The single-pass-Meeus shape (one MoonPosition.Topocentric, one
        // AltAzCalculator.At) is the realistic per-call cost.
        [Benchmark]
        public (double, double, double) MoonSeparation_ObserveAt()
        {
            return MoonSeparation.ObserveAt(_target, _location, _utc);
        }

        // ---- Brightness ----

        [Benchmark]
        public double SkyBrightness_KsAt()
        {
            return SkyBrightness.KsAt(
                targetAltDeg: 60.0, targetAzDeg: 180.0,
                moonAltDeg: _moonAlt, moonAzDeg: _moonAz,
                moonPhaseAngleDeg: _moonPhaseDeg,
                sunAltDeg: _sunAlt,
                extinctionKBand: 0.28,
                v0Mag: 21.5);
        }

        // ---- Aggregate session paths ----

        // Simpson 20 segments on a sin(alt) quality function. Each segment evaluates
        // TargetGeometry.AltitudeAtHourAngle, so this benchmark amplifies any inlining /
        // bounds-check / FMA gains in the geometry primitive.
        [Benchmark]
        public double IntegratedQuality_OverSession()
        {
            return IntegratedQuality.OverSession(
                _target, _location, _utc, TimeSpan.FromHours(2), SinAltQuality);
        }

        [Benchmark]
        public double IntegratedQuality_SinAltitudeOverSession()
        {
            // Closed-form variant; should be much cheaper than the Simpson path.
            return IntegratedQuality.SinAltitudeOverSession(
                _target, _location, _utc, TimeSpan.FromHours(2));
        }

        // End-to-end placement, moon-blind. The realistic per-(target, night) cost in
        // the Year / Sessions chart background fit task.
        [Benchmark]
        public (DateTime, DateTime, double)? BestSession_For_MoonBlind()
        {
            return BestSession.For(
                _target, _location, _night, _horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: null);
        }

        // Same with Narrowband (60 deg, 7 day) profile -- adds the moon-clear sweep
        // (roughly 12 hours of night / 10 min step ~= 72 ObserveAt calls) before
        // placement.
        [Benchmark]
        public (DateTime, DateTime, double)? BestSession_For_Narrowband()
        {
            return BestSession.For(
                _target, _location, _night, _horizon,
                TimeSpan.FromHours(2), TimeSpan.FromHours(4),
                SinAltQuality, profile: MoonAvoidanceProfile.Narrowband);
        }
    }

    // BDN's default csproj-based toolchain spawns a regenerated project that
    // ProjectReferences this test project; that pulls Astronomy.PCL.Native (vcxproj)
    // through the dependency graph, which `dotnet build` can't handle. InProcessEmit
    // toolchain runs benchmarks in this same process, sidestepping the build step.
    internal sealed class InProcessConfig : ManualConfig
    {
        public InProcessConfig()
        {
            AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }
}
