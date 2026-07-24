# 2026-05-12 — FMA / `Math.FusedMultiplyAdd` benchmark findings

Standing conclusions from the 2026-05-12 SIMD/FMA investigation, shipped as the hygiene pass in
commit `b83a0d8` (full field notes archived at `archive/2026-06-21-simd-investigation.md`; extracted
here from `VERIFICATION.md` on 2026-07-24 — this is measured-conclusion record, not build/test
procedure). Raw BenchmarkDotNet runs are not committed — they land in the gitignored
`BenchmarkDotNet.Artifacts/`. Re-run from `Astronomy.Core.Benchmarks` (see `VERIFICATION.md` for
the `dotnet run -c Release` invocations).

- **What was adopted.** `Math.FusedMultiplyAdd` was applied to every polynomial and spherical-trig
  hot spot in `Astronomy.Core` — Meeus (`MeeusUtility`, `MoonPosition`, `SunEphemeris`),
  `TargetGeometry`, and `SkyBrightness`. All tolerance-based tests still pass (round-once FMA is
  strictly *more* accurate than separate `mul; add`, so epsilon asserts hold; bit-exact asserts
  would not).
- **No `.csproj` changes needed.** RyuJIT detects FMA3 at runtime and lowers
  `Math.FusedMultiplyAdd` to a single `vfmadd*sd`; the only relevant property is
  `<Platforms>x64</Platforms>`, already present. Requires hardware FMA3 (`Fma.IsSupported`); the
  software fallback preserves semantics but is ~10–50× slower than vanilla `a*b+c`, so confirm
  hardware support before relying on it for perf.
- **Where FMA paid off** (real-workload `HotPathBenchmarks`): chained polynomial code the trig
  calls don't already dominate — `IntegratedQuality_OverSession` **-4.2%** (Simpson with 20+
  `AltitudeAtHourAngle` evals), `Sun_AltAzAt` **-1.8%** (Horner ×3 + nutation chains). Headline
  real-workload range: **1.5–4.2%** on Sun / Simpson paths.
- **Where it didn't** (noise): the transcendental-dominated moon path — `AstroUtil_GetMoonAltitude`,
  `MoonSeparation_ObserveAt` via `MoonPosition.ApparentEcliptic`'s two 60-term `sin`/`cos` loops.
  FMA savings on the arg computation are buried under ~50-cycle trig latency. Speeding the moon
  path needs vectorised trig, not FMA.
- **Isolated microbench wins** (`FmaBenchmarks`, FMA chains the JIT can't constant-fold): Horner4
  **23%**, Horner8 **44%**, SphericalAlt **15%**. Caveat: single-FMA microbenches are deceptive —
  the JIT may hoist vanilla `a*b+c` but not FMA, making FMA look slower; measure with chained
  Horner polynomials and per-iter-varied inputs.
- **Not adopted / future work.** Specialized non-`params` `Horner` overloads (kill the `double[]`
  alloc), `Vector<double>` SIMD on the Moon 60-term tables (needs hand-rolled/vectorised sin/cos),
  Estrin's-scheme polynomials, and explicit `System.Runtime.Intrinsics.X86` intrinsics — ranked in
  `ROADMAP.md` § *Open: SIMD / FMA deep dive*.
