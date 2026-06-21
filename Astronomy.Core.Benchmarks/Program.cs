using BenchmarkDotNet.Running;

namespace Astronomy.Core.Benchmarks
{
    // BenchmarkDotNet entry point for the Astronomy.Core hot-path suite. Run with
    // `dotnet run -c Release` (Release is mandatory -- Debug numbers are misleading):
    //   -- --filter *    runs every benchmark
    //   -- --list tree   enumerates them
    //   (no args)        drops into the interactive chooser
    // Split out of Astronomy.Core.Tests (2026-06-21) so that project could adopt xUnit v3,
    // which owns its test assembly's auto-generated entry point.
    internal static class Program
    {
        public static int Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return 0;
        }
    }
}
