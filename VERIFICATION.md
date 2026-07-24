# VERIFICATION.md

**Charter.** How to **build, test, and benchmark** the Library, and the build traps that turn a passing-looking run into a wrong result. Read before claiming a change builds or its tests pass. Module mechanics live in `ARCHITECTURE.md`.

## Build / test / benchmark

**Prerequisites.**
- **VS2026 (build 18.x) `MSBuild.exe`** — not optional and not merely "preferred": `Astronomy.PCL.Native.vcxproj` pins `<PlatformToolset>v145</PlatformToolset>`, and v145 ships only with the VS2026 line, so **VS2022 / 17.x cannot load the vcxproj at all**. Earlier lines can still build the pure-managed projects individually (see below). Locate MSBuild via `vswhere.exe` or use a Developer Command Prompt. On this machine: `C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe`.
- **.NET 10 SDK** — `global.json` pins `10.0.203` with `rollForward: latestMajor`, so a newer 10.x (e.g. 10.0.302) resolves fine, but with no .NET 10 SDK installed you get an opaque `global.json` resolution failure rather than a build error.
- **The vendored `PCL/` tree**, for anything touching `Astronomy.PCL` — see *PCL native prerequisites* below. It is gitignored, so a fresh clone has none of it.

`dotnet build Astronomy.sln` cannot drive the C++ vcxproj — see the trap below.

```bash
# Full mixed build (native + managed) — REQUIRED to produce Astronomy.PCL.Native.dll
# -restore is load-bearing: MSBuild.exe does NOT restore implicitly (unlike dotnet build),
# and there is no NuGet.config or Directory.Build.targets hook, so without it a clean clone
# fails on missing project.assets.json / NU1101 before compiling anything.
msbuild Astronomy.sln -restore -p:Configuration=Debug -p:Platform=x64 -m

# Run xUnit tests (all). The native DLL must already be built; --no-build skips a redundant rebuild.
dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj -c Debug -p:Platform=x64 --no-build

# Run a single test by fully-qualified name or substring filter
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AltitudeCurveTests"
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "TargetDefault_IsM31"

# Pure-managed Astronomy.Core build (no SLN, no C++ tooling needed)
dotnet build Astronomy.Core/Astronomy.Core.csproj

# Astronomy.XISF / Astronomy.NINA / Catalog / Diagnostics — pure-managed (no native dep)
dotnet build Astronomy.XISF/Astronomy.XISF.csproj
dotnet test  Astronomy.XISF.Tests/Astronomy.XISF.Tests.csproj
dotnet build Astronomy.NINA/Astronomy.NINA.csproj
dotnet test  Astronomy.NINA.Tests/Astronomy.NINA.Tests.csproj
dotnet build Astronomy.Catalog/Astronomy.Catalog.csproj
dotnet test  Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj
dotnet build Astronomy.Diagnostics/Astronomy.Diagnostics.csproj

# Contract bench (the CONSUMERS.md pinout) — pure-managed
dotnet test Astronomy.Contracts.Tests/Astronomy.Contracts.Tests.csproj

# NOTE: Astronomy.Core.Tests is NOT pure-managed — it ProjectReferences Astronomy.PCL,
# which drags Astronomy.PCL.Native.vcxproj into its graph, so `dotnet test` on it alone
# fails with MSB4278 (Microsoft.Cpp.Default.props). Build it with MSBuild, then:
#   msbuild Astronomy.Core.Tests/Astronomy.Core.Tests.csproj -restore -p:Configuration=Debug -p:Platform=x64
#   dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj --no-build -c Debug -p:Platform=x64

# Smoke-test against a real image library (env var gates the live scan)
TP_SMOKE_IMAGE_LIBRARY='E:\Photography\Astro Photography\Processing' \
    dotnet test Astronomy.Catalog.Tests --filter "FullyQualifiedName~ImageLibrarySmokeTest" --logger "console;verbosity=detailed"

# Run BenchmarkDotNet — Release is mandatory (Debug numbers are misleading).
# Benchmarks live in Astronomy.Core.Benchmarks (pure-managed, references only Astronomy.Core),
# so plain dotnet drives them — no SLN / MSBuild / native build needed.
dotnet run -c Release --project Astronomy.Core.Benchmarks -- --filter *
dotnet run -c Release --project Astronomy.Core.Benchmarks -- --list tree
```

`BDN0001` (BenchmarkDotNet's "build in Release" warning) is suppressed in `Astronomy.Core.Benchmarks.csproj` so a Debug build stays warning-free; the runtime check inside BenchmarkSwitcher still enforces Release for benchmark runs.

### PCL native prerequisites (required before the full mixed build)

The vendored `PCL/` tree is **gitignored**, so a fresh clone cannot build `Astronomy.PCL.Native` until
these run — the "REQUIRED" mixed build above will otherwise link-error. Module mechanics (what the tree
*is*, how the wrapper links it) are in `ARCHITECTURE.md` § *PCL local build* / *PCL interop*; this is the
procedure.

1. **Re-extract the vendored tree.** `PCL\PCL-master.zip` → `Library\PCL\` (snapshot pinned 2025-02-22).
   Also the way to discard local edits to the tree.
2. **Set four system environment variables, then reboot** before opening the SLN. They are consumed by
   `PCL.vcxproj`, the six 3rd-party libs, and `xisf.vcxproj`; without them the C++ build fails to find
   headers and libraries:
   - `PCLINCDIR`   = `E:\Projects\VisualStudio\Astronomy\Library\PCL\include`
   - `PCLSRCDIR`   = `E:\Projects\VisualStudio\Astronomy\Library\PCL\src`
   - `PCLLIBDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\lib\x64`
   - `PCLBINDIR64` = `E:\Projects\VisualStudio\Astronomy\Library\PCL\bin\x64`
3. **Build the PCL solution** — `Library\PCL\src\pcl\windows\vc18\PCL.sln` — to emit the seven
   `lib\x64\$(Configuration)\*-pxi.lib` static libs that `Astronomy.PCL.Native` links.
   **Budget real time for this:** the solution carries **46 buildable vcxproj**, not just PCL + the six
   3rd-party libs + xisf — it also builds six file-format modules (BMP, FITS, JPEG, JPEG2000, TIFF, XISF)
   and ~32 process modules (PixelMath, Gaia, Convolution, Debayer, the whole `contrib/` set), which is
   what fills the 2.9 GB `PCL\bin\x64\`.

**Test-data side effect.** `Astronomy.Core.Tests` conditionally copies
`..\PCL\src\utils\xisf\TestData\test.xisf`, so a missing `PCL/` tree silently changes *which tests run*
rather than failing outright — a green run on a fresh clone may simply have skipped the PCL round-trips.

**Running the `xisf` utility.** `xisf.exe` **must be invoked from its solution directory** so its
relative-path arguments resolve. `Test_GetXisfKeywords..bat` (double-dot name; lives in
`src\pcl\windows\vc18\`, not the xisf directory) is a developer-convenience smoke that runs
`xisf.exe --read-fits-keywords` against `TestData\test.xisf`.

**Trap to avoid:** `dotnet build Astronomy.sln` silently produces a managed-only build (skipping the vcxproj) and any test that touches `Astronomy.PCL` then throws `DllNotFoundException`. Always use `msbuild` for the SLN.

**Trap to avoid (xUnit v3):** every test project is xUnit v3 — `OutputType=Exe` + `xunit.v3` + `xunit.runner.visualstudio` 4.0.0-pre + `Microsoft.NET.Test.Sdk` 18.x — and v3 **generates the assembly entry point**, so a test project can't also define its own `Main` (that's why benchmarks live in a separate `Astronomy.Core.Benchmarks` exe). **Never let `xunit.v3` land on a non-test project:** its `mtp-v1` targets force `OutputType=Exe`, and a "Manage NuGet Packages for Solution → all projects" action sprays it silently — the build only breaks later when a version bump enforces the check (this bit four production projects on 2026-06-21). A non-test project that genuinely needs xUnit types references `xunit.v3.extensibility.core` instead.

## Benchmark findings

Consolidated home for benchmark *conclusions*. Raw BenchmarkDotNet runs are not committed — they land in the gitignored `BenchmarkDotNet.Artifacts/`. Re-run from `Astronomy.Core.Benchmarks` (see the `dotnet run -c Release` invocations above).

### FMA / `Math.FusedMultiplyAdd` hygiene pass (commit `b83a0d8`)

Standing conclusions from the 2026-05-12 SIMD/FMA investigation (full field notes archived at `archive/2026-06-21-simd-investigation.md`):

- **What was adopted.** `Math.FusedMultiplyAdd` was applied to every polynomial and spherical-trig hot spot in `Astronomy.Core` — Meeus (`MeeusUtility`, `MoonPosition`, `SunEphemeris`), `TargetGeometry`, and `SkyBrightness`. All tolerance-based tests still pass (round-once FMA is strictly *more* accurate than separate `mul; add`, so epsilon asserts hold; bit-exact asserts would not).
- **No `.csproj` changes needed.** RyuJIT detects FMA3 at runtime and lowers `Math.FusedMultiplyAdd` to a single `vfmadd*sd`; the only relevant property is `<Platforms>x64</Platforms>`, already present. Requires hardware FMA3 (`Fma.IsSupported`); the software fallback preserves semantics but is ~10–50× slower than vanilla `a*b+c`, so confirm hardware support before relying on it for perf.
- **Where FMA paid off** (real-workload `HotPathBenchmarks`): chained polynomial code the trig calls don't already dominate — `IntegratedQuality_OverSession` **-4.2%** (Simpson with 20+ `AltitudeAtHourAngle` evals), `Sun_AltAzAt` **-1.8%** (Horner ×3 + nutation chains). Headline real-workload range: **1.5–4.2%** on Sun / Simpson paths.
- **Where it didn't** (noise): the transcendental-dominated moon path — `AstroUtil_GetMoonAltitude`, `MoonSeparation_ObserveAt` via `MoonPosition.ApparentEcliptic`'s two 60-term `sin`/`cos` loops. FMA savings on the arg computation are buried under ~50-cycle trig latency. Speeding the moon path needs vectorised trig, not FMA.
- **Isolated microbench wins** (`FmaBenchmarks`, FMA chains the JIT can't constant-fold): Horner4 **23%**, Horner8 **44%**, SphericalAlt **15%**. Caveat: single-FMA microbenches are deceptive — the JIT may hoist vanilla `a*b+c` but not FMA, making FMA look slower; measure with chained Horner polynomials and per-iter-varied inputs.
- **Not adopted / future work.** Specialized non-`params` `Horner` overloads (kill the `double[]` alloc), `Vector<double>` SIMD on the Moon 60-term tables (needs hand-rolled/vectorised sin/cos), Estrin's-scheme polynomials, and explicit `System.Runtime.Intrinsics.X86` intrinsics — ranked in `ROADMAP.md` § *Open: SIMD / FMA deep dive*.

## Cross-repo contract verification (constellation DRC)

The portfolio-level design-rule check is `..\build-all.ps1` (one level up from this repo, at the `Astronomy\` constellation root). It compiles the downstream consumers against the current Library and runs the contract tests in `Astronomy.Contracts.Tests` — run it to confirm a Library change hasn't broken a consumer's pinned pinout. The consumer contract itself is documented in `CONSUMERS.md`.
