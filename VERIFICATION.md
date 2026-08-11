# VERIFICATION.md

**Charter.** How to **build, test, and benchmark** the Library, and the build traps that turn a passing-looking run into a wrong result. Read before claiming a change builds or its tests pass. Module mechanics live in `ARCHITECTURE.md`.

## Build / test / benchmark

**Prerequisites.**
- **VS2026 (build 18.x) `MSBuild.exe`** — not optional and not merely "preferred": `Astronomy.PCL.Native.vcxproj` pins `<PlatformToolset>v145</PlatformToolset>`, and v145 ships only with the VS2026 line, so **VS2022 / 17.x cannot load the vcxproj at all**. Earlier lines can still build the pure-managed projects individually (see below). Locate MSBuild via `vswhere.exe` or use a Developer Command Prompt. On this machine: `C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe`.
- **.NET 10 SDK** — `global.json` pins `10.0.203` with `rollForward: latestMajor`, so a newer 10.x (e.g. 10.0.302) resolves fine, but with no .NET 10 SDK installed you get an opaque `global.json` resolution failure rather than a build error.
- **The CENTRAL Windows TFM must stay OS-version-less.** `net10.0-windows` — never `net10.0-windows10.0.26100.1`. An earlier build pinned the OS version and broke with `NETSDK1229` / `MSB4184`; the TFM is set once centrally in the repo-root `Directory.Build.props`, so re-adding a suffix there re-breaks every project at once. (Derivation: `CHANGELOG.md` § 2026-05-11.) **Per-project overrides are the supported escape hatch and two exist deliberately** (since 2026-08-10, `diagnostics-portable-core`): `Astronomy.Diagnostics` pins `net10.0` (the TFM-neutral core — do not "fix" it back to `-windows`; a platform API failing its build is the enforcement working) and `Astronomy.Diagnostics.WinUI` pins `net10.0-windows10.0.19041.0` (the WinUI/WindowsAppSDK floor — verified building clean in-sln 2026-08-10). Both build standalone with `dotnet build` and inside the MSBuild sln pass.
- **The vendored `PCL/` tree**, for anything touching `Astronomy.PCL` — see *PCL native prerequisites* below. It is gitignored, so a fresh clone has none of it.

`dotnet build Astronomy.sln` cannot drive the C++ vcxproj — see the trap below.

```bash
# Full mixed build (native + managed) — REQUIRED to produce Astronomy.PCL.Native.dll
# -restore is load-bearing: MSBuild.exe does NOT restore implicitly (unlike dotnet build),
# and there is no NuGet.config or Directory.Build.targets hook, so without it a clean clone
# fails on missing project.assets.json / NU1101 before compiling anything.
# (A repo-root Directory.Build.props DOES exist and is load-bearing for a different reason:
# it supplies TargetFramework/LangVersion to every csproj — several projects declare neither.)
msbuild Astronomy.sln -restore -p:Configuration=Debug -p:Platform=x64 -m

# Run xUnit tests (all). The native DLL must already be built; --no-build skips a redundant rebuild.
dotnet test Astronomy.Core.Tests/Astronomy.Core.Tests.csproj -c Debug -p:Platform=x64 --no-build

# Run a single test by fully-qualified name or substring filter
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "FullyQualifiedName~AltitudeCurveTests"
dotnet test Astronomy.Core.Tests -c Debug -p:Platform=x64 --no-build --filter "TargetDefault_IsM31"

# Pure-managed Astronomy.Core build (no SLN, no C++ tooling needed)
dotnet build Astronomy.Core/Astronomy.Core.csproj

# Astronomy.XISF / Astronomy.NINA / Catalog / Diagnostics — pure-managed (no native dep).
# NOTE the platform flag on the TEST projects: they are <Platforms>x64</Platforms>-only, so a
# platform-less `dotnet test` resolves Platform=AnyCPU and builds/reads a DIFFERENT output tree
# (bin\Debug\ vs the SLN's bin\x64\Debug\) — combine that with --no-build elsewhere and a green
# run can exercise stale binaries. Pass -p:Platform=x64 on test invocations.
dotnet build Astronomy.XISF/Astronomy.XISF.csproj
dotnet test  Astronomy.XISF.Tests/Astronomy.XISF.Tests.csproj -p:Platform=x64
dotnet build Astronomy.NINA/Astronomy.NINA.csproj
dotnet test  Astronomy.NINA.Tests/Astronomy.NINA.Tests.csproj -p:Platform=x64
dotnet build Astronomy.Catalog/Astronomy.Catalog.csproj
dotnet test  Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj -p:Platform=x64
dotnet build Astronomy.Diagnostics/Astronomy.Diagnostics.csproj
dotnet build Astronomy.Diagnostics.Windows/Astronomy.Diagnostics.Windows.csproj
dotnet build Astronomy.Diagnostics.WinForms/Astronomy.Diagnostics.WinForms.csproj
dotnet build Astronomy.Diagnostics.WinUI/Astronomy.Diagnostics.WinUI.csproj
dotnet test  Astronomy.Diagnostics.Tests/Astronomy.Diagnostics.Tests.csproj -p:Platform=x64

# Contract bench (the CONSUMERS.md pinout) — pure-managed
dotnet test Astronomy.Contracts.Tests/Astronomy.Contracts.Tests.csproj -p:Platform=x64

# EVERY project in this repo builds with warnings-as-errors (since 2026-08-01, after 45 xUnit1051s
# accumulated silently in test code): <TreatWarningsAsErrors> on all six test csprojs and all NINE
# shipped csprojs (incl. the three 2026-08 Diagnostics satellites .Windows/.WinForms/.WinUI),
# /WX (<TreatWarningAsError>) on Astronomy.PCL.Native's wrapper TUs. A new warning
# IS a build failure, not noise to scroll past — fix it or (rarely, with a comment) suppress it
# deliberately; never turn the ratchet off. Sole exception: Astronomy.Core.Benchmarks (not shipped,
# not test). In test code, pass TestContext.Current.CancellationToken to any ct-accepting call
# (xUnit1051); tests that deliberately exercise cancellation keep their own cts.Token.

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
2. **Set four system environment variables, then reboot** before opening the SLN. They are consumed
   across all 46 sln-listed vcxproj (`PCLBINDIR64` by the modules + `xisf.vcxproj` only — `PCL.vcxproj`
   and the six 3rd-party libs use the other three); without them the C++ build fails to find
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

**Trap to avoid:** `dotnet build Astronomy.sln` errors out on the vcxproj (MSB4278 — dotnet's MSBuild can't load the C++ targets), leaving no native DLL; any test that touches `Astronomy.PCL` then throws `DllNotFoundException`. Always use `msbuild` for the SLN.

**Trap to avoid (xUnit v3):** every test project is xUnit v3 — `OutputType=Exe` + `xunit.v3` + `xunit.runner.visualstudio` 4.0.0-pre + `Microsoft.NET.Test.Sdk` 18.x — and v3 **generates the assembly entry point**, so a test project can't also define its own `Main` (that's why benchmarks live in a separate `Astronomy.Core.Benchmarks` exe). **Never let `xunit.v3` land on a non-test project:** its `mtp-v1` targets force `OutputType=Exe`, and a "Manage NuGet Packages for Solution → all projects" action sprays it silently — the build only breaks later when a version bump enforces the check (this bit four production projects on 2026-06-21). A non-test project that genuinely needs xUnit types references `xunit.v3.extensibility.core` instead.

**Trap to avoid (MinVer needs git history):** version stamps are derived from git history at build
time, so a build from a source zip, a tag-less clone, or a shallow checkout silently stamps the
fallback `-alpha` prerelease instead of the real version — and the consumer `release.ps1` gates abort
on an `-alpha` stamp in the embedded Library payload, so a history-less build doesn't just mislabel a
DLL, it trips the AL-releases-first gate. Build from a full clone with tags whenever the stamp
matters (→ `RELEASING.md` § *Distribution*).

**Trap to avoid (Debug cannot see native ISA regressions):** `/Od` suppresses auto-vectorization, so an escalated instruction-set flag in the PCL tree emits **nothing** in Debug — the entire Debug test suite is structurally blind to it, and every recipe above defaults to Debug. The AVX2 floor (the 4800U has no AVX-512) can only be verified on a **Release** `Astronomy.PCL.Native.dll` with `dumpbin`. Re-check it after any PCL toolset pass or re-snapshot; a green Debug run proves nothing about it. (Policy + history → `ARCHITECTURE.md` § *PCL local build*.)

**The managed hot paths have a hardware floor too — and it is checkable without a rebuild.** Core's
Meeus / `TargetGeometry` / `SkyBrightness` hot paths are lowered to `Math.FusedMultiplyAdd`, which
RyuJIT emits as a single `vfmadd*sd` only where hardware FMA3 exists; on a machine without it the
software fallback stays correct but runs ~10-50× slower than plain `a*b+c` — so the Zen 2 floor
(AVX2 + FMA, no AVX-512) that governs `Astronomy.PCL.Native` also governs whether the *pure-managed*
library performs as measured (→ `docs/2026-05-12-fma-benchmark-findings.md`). To verify what the JIT
actually emitted, no csproj change is needed (RyuJIT decides per method at JIT time):
- BenchmarkDotNet's `HardwareIntrinsics=` header line must show `+FMA`;
- `DOTNET_JitDisasm=<MethodName>` dumps the emitted x64 so `vfmadd*sd` can be confirmed;
- `DOTNET_EnableHWIntrinsic=0` / `DOTNET_EnableAVX512F=0` / `DOTNET_PreferredVectorBitWidth=256`
  reproduce a lower-ISA machine in place.
(Knob derivations: `archive/2026-06-21-simd-investigation.md` § *Toolchain & runtime*.)

**Assert with tolerances, never bit-exact.** `Astronomy.Core`'s polynomial and spherical-trig hot spots are FMA-lowered (`Math.FusedMultiplyAdd`, applied across Meeus / `TargetGeometry` / `SkyBrightness`). Round-once FMA is *strictly more accurate* than separate `mul; add`, so epsilon asserts hold while a bit-exact assert would not — and would break again on any future FMA or vectorization pass. (→ `docs/2026-05-12-fma-benchmark-findings.md`.)

## Parity baseline — the drift envelope

`Astronomy.Core.Tests`' 9-fixture parity suite is the Library's drift envelope. **Do not widen its tolerances.** The four constants (`ParityBaselineTests.cs`: 60 s dusk/dawn, 0.005 illumination, 30″ moon-altitude, 60″ separation) come from the underlying formulas' documented accuracy budgets — tightening catches only float noise, and loosening masks the real regressions the suite exists to catch. They are co-located for legibility, not as an invitation to edit.

After a **deliberate** behaviour change turns the parity cases red, re-baseline rather than re-tolerance: unskip `ParityBaselineTests._DumpBaselinesForRegeneration`, run it, paste the emitted initializers into `ParityFixtures.Baselines`, then re-apply the `Skip`. (Standing prohibition and procedure recorded in `archive/2026-05-18-library-review-recheck.md`; promoting the baseline to an independent NINA-sourced oracle is `ROADMAP.md` § *Open: Library-review residuals* F5.7.)

## Benchmark findings

Benchmark *conclusions* are journal-tier, not procedure: the standing FMA/SIMD findings from the
2026-05-12 investigation live in **`docs/2026-05-12-fma-benchmark-findings.md`** (field notes
archived at `archive/2026-06-21-simd-investigation.md`; open directions ranked in `ROADMAP.md`
§ *Open: SIMD / FMA deep dive*). Raw BenchmarkDotNet runs are not committed — they land in the
gitignored `BenchmarkDotNet.Artifacts/`. Re-run from `Astronomy.Core.Benchmarks` (see the
`dotnet run -c Release` invocations above).

**Writing an honest benchmark.** A BenchmarkDotNet number is only real if the JIT could not fold the
work away: **vary the input every iteration** (loop-invariant expressions get hoisted; literal
constant call arguments get constant-folded — the pre-FMA `TargetGeometry_AltitudeAtHourAngle`
"baseline" of 0.0036 ns/op was a constant-fold, not a measurement), treat any **sub-nanosecond
per-op result as an artifact until proven**, and suspect the cross-iteration `sum +=` accumulator
whenever twin benchmarks tie at a number close to a known pipe latency. (v1 of `FmaBenchmarks`
shipped a false "FMA is slower" conclusion from exactly these artifacts; the v1-vs-v2 derivation is
`archive/2026-06-21-simd-investigation.md` § *Microbench design*.)

## Cross-repo contract verification (constellation DRC)

The portfolio-level design-rule check is `..\build-all.ps1` (one level up from this repo, at the `Astronomy\` constellation root). It compiles the downstream consumers against the current Library and runs the contract tests in `Astronomy.Contracts.Tests` — run it to confirm a Library change hasn't broken a consumer's pinned pinout. The consumer contract itself is documented in `CONSUMERS.md`.

**The DRC's unit is each consumer's solution file** — the script builds the first `*.sln`/`*.slnx` at
depth 1 in each consumer directory, nothing else. A consumer project deliberately kept *out* of its
app's sln still ProjectReferences the Library while being invisible to the DRC: today that is XFM's
`tools/CompressionBench` ("standalone tool — deliberately NOT a member of XisfFileManager.sln"),
which pins `XisfImageReader.ReadImageAsync`, `XisfImageData`,
`XisfBlockCompression.Compress/Decompress/Shuffle/Unshuffle`, and `XisfHeaderReader.ReadAsync` — none
touched by any DRC-built project, so a breaking change to those members reads GREEN. Same blind-spot
class as the XFM-unchecked-until-2026-08-10 incident the script's header records, closed at consumer
granularity but not at sub-consumer granularity.
