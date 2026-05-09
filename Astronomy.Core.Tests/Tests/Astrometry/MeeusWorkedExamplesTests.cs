using System;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Astronomy.Core.Tests.Tests.Astrometry
{
    // Validate the internal Meeus implementations (SunEphemeris, MoonPosition,
    // MoonIllumination) against Jean Meeus's published worked examples in
    // *Astronomical Algorithms* 2nd ed. The constants here are quoted directly from
    // the book and are the gold standard against which a low-precision implementation
    // is judged.
    //
    // Meeus's "low precision" Sun (chapter 25) is accurate to ~0.01 deg; the truncated
    // 60-term Moon series (chapter 47) is accurate to ~0.003 deg. Tolerances reflect
    // those budgets plus some extra slack.
    //
    // Reflection is used because SunEphemeris / MoonPosition / MoonIllumination are
    // internal -- we want them to stay internal (the public surface is AstroUtil) but
    // still want unit-test coverage on the math without making the InternalsVisibleTo
    // surface noisier than necessary.
    public class MeeusWorkedExamplesTests
    {
        private readonly ITestOutputHelper mLog;

        public MeeusWorkedExamplesTests(ITestOutputHelper log)
        {
            mLog = log;
        }

        // ---------------- Sun (Meeus example 25.a, pg. 165) ----------------

        [Fact]
        public void SunPosition_Apparent_Meeus25a_1992Oct13_Matches()
        {
            const double jd = 2448908.5;  // 1992 Oct 13.0 TD

            (double raDeg, double decDeg, double rAu) = InvokeSunApparent(jd);

            mLog.WriteLine($"Sun 1992-10-13 0h TD: RA={raDeg:F6} Dec={decDeg:F6} R={rAu:F6}");

            // Meeus pg. 165 (low-precision form): apparent lambda = 199.90734 deg,
            // expected apparent RA = 198.378178 deg, apparent Dec = -7.78507 deg,
            // R = 0.99766 AU.
            Assert.InRange(raDeg,  198.378178 - 0.02, 198.378178 + 0.02);
            Assert.InRange(decDeg, -7.78507   - 0.02, -7.78507   + 0.02);
            Assert.InRange(rAu,     0.99766   - 0.0001, 0.99766   + 0.0001);
        }

        // ---------------- Moon (Meeus example 47.a, pg. 342) ----------------

        [Fact]
        public void MoonPosition_ApparentEcliptic_Meeus47a_1992Apr12_Matches()
        {
            const double jd = 2448724.5;  // 1992 Apr 12.0 TD

            (double lonDeg, double latDeg, double distKm) = InvokeMoonApparentEcliptic(jd);

            mLog.WriteLine($"Moon 1992-04-12 0h TD: lon={lonDeg:F6} lat={latDeg:F6} dist={distKm:F1}");

            // Meeus pg. 342: lambda (with nutation) = 133.167269 deg, beta = -3.229126 deg,
            // Delta = 368409.7 km.
            Assert.InRange(lonDeg,  133.167269 - 0.005, 133.167269 + 0.005);
            Assert.InRange(latDeg,  -3.229126  - 0.005, -3.229126  + 0.005);
            Assert.InRange(distKm,  368409.7   - 50.0,  368409.7   + 50.0);
        }

        [Fact]
        public void MoonPosition_ApparentEquatorial_Meeus47a_1992Apr12_Matches()
        {
            const double jd = 2448724.5;

            (double raDeg, double decDeg, double distKm) = InvokeMoonApparent(jd);

            mLog.WriteLine($"Moon 1992-04-12 0h TD: RA={raDeg:F6} Dec={decDeg:F6}");

            // Meeus pg. 343: apparent RA = 134.688470 deg, apparent Dec = +13.768368 deg.
            Assert.InRange(raDeg,  134.688470 - 0.005, 134.688470 + 0.005);
            Assert.InRange(decDeg, +13.768368 - 0.005, +13.768368 + 0.005);
        }

        // ---------------- Moon illumination (Meeus example 48.a, pg. 347) ----------------

        [Fact]
        public void MoonIllumination_Fraction_Meeus48a_1992Apr12_Matches()
        {
            const double jd = 2448724.5;
            double k = InvokeMoonIllumination(jd);

            mLog.WriteLine($"Moon illumination 1992-04-12 0h TD: k={k:F4}");

            // Meeus pg. 347: k = 0.6786.
            Assert.InRange(k, 0.6786 - 0.001, 0.6786 + 0.001);
        }

        // ---------------- Reflection helpers ----------------
        // Reach into the internal Meeus types via reflection so we can keep them
        // internal in the production assembly. Cached at type-init time (not great
        // for per-test isolation, but fine for read-only static methods).

        private static readonly MethodInfo mSunApparent = typeof(Astronomy.Core.Astrometry.AstroUtil)
            .Assembly
            .GetType("Astronomy.Core.Astrometry.Meeus.SunEphemeris")
            .GetMethod("Apparent", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo mMoonApparent = typeof(Astronomy.Core.Astrometry.AstroUtil)
            .Assembly
            .GetType("Astronomy.Core.Astrometry.Meeus.MoonPosition")
            .GetMethod("Apparent", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo mMoonApparentEcliptic = typeof(Astronomy.Core.Astrometry.AstroUtil)
            .Assembly
            .GetType("Astronomy.Core.Astrometry.Meeus.MoonPosition")
            .GetMethod("ApparentEcliptic", BindingFlags.Public | BindingFlags.Static);

        private static readonly MethodInfo mMoonIllumination = typeof(Astronomy.Core.Astrometry.AstroUtil)
            .Assembly
            .GetType("Astronomy.Core.Astrometry.Meeus.MoonIllumination")
            .GetMethod("Fraction", BindingFlags.Public | BindingFlags.Static);

        private static (double, double, double) InvokeSunApparent(double jd)
        {
            object result = mSunApparent.Invoke(null, new object[] { jd });
            return DestructureTuple3(result);
        }

        private static (double, double, double) InvokeMoonApparent(double jd)
        {
            object result = mMoonApparent.Invoke(null, new object[] { jd });
            return DestructureTuple3(result);
        }

        private static (double, double, double) InvokeMoonApparentEcliptic(double jd)
        {
            object result = mMoonApparentEcliptic.Invoke(null, new object[] { jd });
            return DestructureTuple3(result);
        }

        private static double InvokeMoonIllumination(double jd)
        {
            return (double)mMoonIllumination.Invoke(null, new object[] { jd });
        }

        // C# value tuples are ValueTuple<T1, T2, T3> at runtime; field names "Item1"
        // etc. are baked in.
        private static (double, double, double) DestructureTuple3(object t)
        {
            var type = t.GetType();
            double a = (double)type.GetField("Item1").GetValue(t);
            double b = (double)type.GetField("Item2").GetValue(t);
            double c = (double)type.GetField("Item3").GetValue(t);
            return (a, b, c);
        }
    }
}
