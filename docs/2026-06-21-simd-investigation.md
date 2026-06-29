# SIMD / FMA Investigation — Field Notes

Captured 2026-05-12. Documents what was learned during the FMA hygiene pass
(Library commit `b83a0d8`) so a future self can pick the topic up cold. This
is investigation notes, not a tutorial — the framing is "what we did, what we
measured, what to try next."

## TL;DR

- `Math.FusedMultiplyAdd` was applied to every polynomial and spherical-trig
  hot spot in `Astronomy.Core` (Meeus + TargetGeometry + SkyBrightness). All
  321 xUnit tests pass.
- Real-workload impact: **1.5–4.2%** on Sun / Simpson paths, **noise** on
  transcendental-dominated paths (moon). FMA savings are masked when each
  loop iter has a `Math.Sin` / `Math.Cos` in series — the ~10–20 ns trig
  call dominates.
- **No `.csproj` settings are needed.** RyuJIT detects FMA3 at runtime and
  lowers `Math.FusedMultiplyAdd` to `vfmadd*sd` automatically.
- The microbench file (`Astronomy.Core.Benchmarks/FmaBenchmarks.cs`)
  is the on-ramp for the deeper SIMD work: shows 23–44% wins on isolated
  FMA chains and documents how to design microbenches that the JIT can't
  defeat via constant-folding / hoisting.
- Bigger SIMD opportunities (not yet pursued): specialized non-`params`
  Horner overloads, `Vector<double>` on the MoonPosition 60-term tables,
  Estrin's-scheme polynomial parallelization, explicit `Avx2`/`Fma`
  intrinsics.

## What is FMA

`Math.FusedMultiplyAdd(a, b, c)` computes `a*b + c` in **one hardware
instruction with one round-to-nearest at the end**. Two properties matter:

1. **Round-once is strictly more accurate** than separate `mul; add`. The
   full-precision product `a*b` (which won't fit in 53 bits in general)
   is held internally before the addition; only the final `a*b + c` gets
   rounded to a double. The vanilla path rounds the product first
   (losing bits below the 53rd), then rounds the sum.

2. **One uop instead of two.** On x64 with FMA3 (Haswell / Zen+ and later)
   the JIT lowers `Math.FusedMultiplyAdd` to a single `vfmadd*sd`
   instruction (scalar double) or `vfmadd*pd` (packed). Mul = 3 cyc, add =
   3 cyc, FMA = 4 cyc on Zen 4 — naively FMA costs one extra cycle, but in
   chained code the mul-then-add pair must serialize (~6 cyc), while the
   FMA does it in 4. In throughput-limited code, FMA consumes one
   execution-unit slot instead of two.

The JIT **does not** auto-contract `a*b + c` into FMA. Doing so would
violate IEEE-754's round-to-nearest semantics (the answer would change in
the last bit). Opt-in is explicit; the caller rewrites the expression.

### Hardware-fallback gotcha

If `System.Runtime.Intrinsics.X86.Fma.IsSupported` is false (no FMA3
hardware, e.g. pre-Haswell Intel, AMD pre-Zen), the runtime falls back to a
**software emulation** that preserves round-once semantics. That emulation
is much slower than vanilla `a*b + c` — typically 10–50× — because it has
to compute the full-precision product carefully. **Always verify hardware
support before adopting in shipping code.** The check is a single static
boolean, cheap to read.

## Toolchain & runtime

### `.csproj` settings — nothing to change

The JIT picks up FMA / AVX2 / AVX-512 automatically. The only csproj
property that's relevant is the platform:

```xml
<Platforms>x64</Platforms>
```

…which `Astronomy.Core` and `Astronomy.Core.Tests` already have. No other
property gates intrinsic emission. RyuJIT decides at JIT time, per-method,
based on the runtime CPU.

What WOULD require csproj changes (not relevant for the local JIT path,
but worth knowing for AOT/R2R):

- `<PublishReadyToRun>true</PublishReadyToRun>` + crossgen2's
  `--instruction-set` flag — picks a baseline ISA for pre-JIT'd output.
  Without an explicit ISA flag, crossgen2 targets the lowest common
  denominator (x64 baseline, no AVX). For an R2R build you'd add e.g.
  `--instruction-set=avx2` to crossgen2 to bake AVX2/FMA into the
  precompiled image.
- `<TieredCompilation>false</TieredCompilation>` would skip the tier-0
  quick JIT and go straight to tier-1 optimized. Not needed for correctness;
  affects how fast the JIT decides to inline / SIMD-vectorize.

### Runtime knobs (env vars, not csproj)

| Env var | Effect |
|---|---|
| `DOTNET_EnableHWIntrinsic=0` | Disable **all** hardware intrinsics. Forces the pure-managed software path. Useful for repro on old hardware or when chasing a JIT bug. |
| `DOTNET_EnableAVX512F=0` | Disable AVX-512 specifically. JIT falls back to AVX2 / FMA. |
| `DOTNET_PreferredVectorBitWidth=256` | Cap `Vector<T>` width to 256 bits (AVX2) even on AVX-512 hardware. Useful when you want to test the AVX2 codegen without rebooting into a different CPU. |
| `DOTNET_JitDisasm=MethodName` | Dump the JIT-emitted x64 assembly for the named method to stdout. The most reliable way to confirm `vfmadd*sd` is actually being emitted. |

Example to verify FMA is reaching the metal for our hot path:

```cmd
set DOTNET_JitDisasm=AltitudeAtHourAngle
dotnet run -c Release ...
```

Look for `vfmadd*sd` (or `vfmadd*ss` for floats) instructions in the
output. Without the env var, the JIT runs normally and you see nothing
about its codegen.

### Verification during benchmarks

BenchmarkDotNet prints `HardwareIntrinsics=...` in its header, e.g.

```
HardwareIntrinsics=AVX512 BITALG+VBMI2+VNNI+VPOPCNTDQ,AVX512 IFMA+VBMI,
AVX512 F+BW+CD+DQ+VL,AVX2+BMI1+BMI2+F16C+FMA+LZCNT+MOVBE,AVX,SSE3+SSSE3+
SSE4.1+SSE4.2+POPCNT,X86Base+SSE+SSE2,AES+PCLMUL VectorSize=256
```

`+FMA` in there means the runtime saw FMA3 support. The microbench's
`[GlobalSetup]` also prints `[FmaBenchmarks] Fma.IsSupported = True` as
a belt-and-braces check.

## Performance model (Zen 4 reference)

The dev machine is a Ryzen 9 7950X (Zen 4, 4.5 GHz nominal). Relevant
latencies/throughputs for double-precision scalar code:

| Op | Latency | Throughput | Pipes |
|---|---|---|---|
| `mulsd` | 3 cyc | 1/cyc | 2 (mul/FMA shared) |
| `addsd` | 3 cyc | 1/cyc | 2 (add pipes) |
| `vfmadd*sd` | 4 cyc | 1/cyc | 2 (same as mul) |
| `Math.Sin` / `Math.Cos` | ~50 cyc | varies | 1 (software, runs on standard pipes) |
| `Math.Atan2` / `Math.Asin` | ~30–60 cyc | varies | 1 |

(Numbers from AMD Zen 4 software optimization guide, software-path
estimates for transcendentals. Trig is "scalar-only" on x64 .NET — the
runtime doesn't yet vectorize `Math.Sin` / `Math.Cos` calls.)

### Implications

- **Pure-arithmetic loops** (Horner polynomials, FMA chains) are where FMA
  wins. The chain inside one polynomial is sequential — each FMA depends
  on the previous one — so FMA latency directly translates to per-poly
  cost. Two FMA pipes mean back-to-back independent polynomials can
  pipeline.
- **Transcendental-heavy loops** (MoonPosition's 60-term table, anywhere
  with `sin` / `cos` in series) bury FMA savings under trig latency. A
  Horner step gets faster, but the next instruction is `Math.Sin(arg)` so
  the savings overlap with trig and effectively vanish.
- **Single FMA in isolation** is hard to measure. See v1 microbench
  experience below — the JIT can hoist a vanilla `r = a*b + r` to
  `r = const + r` if `a, b` are loop-invariant, while FMA blocks that
  hoist. The result was Single_Fma reporting *slower* than vanilla in v1.

## Microbench design — v1 vs v2 lessons

The benchmark file is `Astronomy.Core.Benchmarks/FmaBenchmarks.cs`.
What's there now is v2; v1 was discarded after teaching what NOT to do.

### v1: what went wrong

v1 measured constants. Examples:

```csharp
[Benchmark]
public double Horner4_Vanilla()
{
    double x = _x;          // _x is a field, set once
    double r = 0.001813;    // literal
    r = r * x + (-0.00059);
    r = r * x + (-46.8150);
    r = r * x + 84381.448;
    return r;
}
```

The JIT hoisted `_x` to a local, recognized the whole polynomial as
loop-invariant, and reduced the inner loop body to `acc += precomputed`.
Both vanilla and FMA reported ~0.2 ns/op — the JIT was running a
sum-of-constants, not a polynomial.

Similar failure on the single-FMA case:

```csharp
double r = _ha;
for (int i = 0; i < 100; i++)
    r = _phi * _delta + r;   // _phi, _delta invariant
```

The JIT hoisted `_phi * _delta` to a const outside the loop, leaving
`r = const + r` (one add per iter). The FMA twin couldn't be hoisted
(round-once semantics block the optimization), so FMA reported **slower**
— 0.40 ns vs 0.32 ns. The conclusion "FMA is slower" was a measurement
artifact, not a real result.

### v2: design fixes

Two changes made the measurements honest:

1. **Vary the input per iter.** Adding `x += 1e-9` per loop iteration
   creates a tiny loop-carried dep on `x` that the JIT can't collapse.
   Then the polynomial actually executes each iter.
2. **Keep the cross-iter accumulator simple.** `sum += polynomialResult`
   is the only loop-carried dep; iterations are otherwise independent so
   OoO can pipeline multiple polynomials at once.

v2 results (all twins, 1000 ops per invocation, vary `x` by 1e-9 per iter):

| Bench | Vanilla | FMA | Speedup |
|---|---:|---:|---:|
| Horner4 | 0.7065 ns | 0.5422 ns | **23%** |
| Horner8 | 1.4404 ns | 0.8079 ns | **44%** |
| SphericalAlt | 0.6218 ns | 0.5276 ns | **15%** |

Predictions matched theory:
- Horner8 vanilla: 8 × (mul+add) = 8 × ~3 cyc per uop = ~24 cyc per poly
  (with mul/add throughput limit). Measured 1.44 ns × 4.5 GHz = 6.5 cyc.
  ~4× ILP (iterations overlapping) explains the gap.
- Horner8 FMA: 8 × FMA = 8 × ~1 cyc throughput = ~8 cyc per poly with
  ILP. Measured 0.81 ns × 4.5 GHz = 3.6 cyc — ~2× ILP. FMA saturates the
  two FMA pipes.

### Takeaways for future microbenches

- **Never trust sub-nanosecond measurements** without confirming the JIT
  actually executed work each iter. ~0.3 ns/op is suspicious; <0.1 ns is
  definitely an artifact.
- **Vary inputs per iter** with a cheap perturbation (`x += 1e-9`,
  array-index lookup, etc) to defeat hoisting / constant-folding.
- **Independent iterations let OoO pipeline; loop-carried critical paths
  expose latency.** Choose which one you're measuring deliberately. For
  FMA throughput → independent iters. For FMA latency → carry `r` through.
- **Single-FMA microbenches are deceptive.** The JIT may hoist vanilla
  but not FMA, making FMA look slow. Use chained polynomials (Horner) to
  get an honest comparison.
- **A `sum += result` loop-carried accumulator can mask the FMA delta.**
  If the cross-iter `sum +=` (~3-cyc latency) is slower than the
  per-iter polynomial body, OoO pipelines several polynomials inside
  one accumulator latency window — both vanilla and FMA report the same
  number, governed by the `add` pipe, not the arithmetic being measured.
  v1 SphericalAlt fell into this (vanilla and FMA both ~0.77 ns). Watch
  for it: if your twin benchmarks tie *and* both report close to a known
  pipe latency, suspect the accumulator.
- **Disable inlining selectively** with `[MethodImpl(MethodImplOptions.NoInlining)]`
  if you want to measure call overhead vs body cost separately.

## HotPathBenchmarks — real-workload measurements

The full real-workload comparison. Baseline = pre-FMA `dev`, post-FMA =
this investigation's commit (`b83a0d8`).

```
| Method                                   | Baseline     | Post-FMA     | Δ          | %       |
|------------------------------------------|--------------|--------------|------------|---------|
| TargetGeometry_AltitudeAtHourAngle       |   0.0036 ns  |   5.155 ns   |  +5.151 ns |   *N/A* |
| TargetGeometry_AzimuthAtHourAngle        |  26.7054 ns  |  26.619 ns   |    -0.09   |   -0.3% |
| SiderealTime_Local                       |   2.5261 ns  |   2.514 ns   |    -0.01   |   -0.5% |
| Sun_AltAzAt                              | 146.598  ns  | 143.901 ns   |    -2.70   |   -1.8% |
| AstroUtil_GetMoonAltitude                |1327.469  ns  |1324.104 ns   |    -3.36   |   -0.3% |
| MoonSeparation_ObserveAt                 |1425.630  ns  |1424.105 ns   |    -1.53   |   -0.1% |
| SkyBrightness_KsAt                       |   5.6206 ns  |   5.643 ns   |    +0.02   |   +0.4% |
| IntegratedQuality_OverSession            | 674.970  ns  | 646.610 ns   |   -28.4    |   -4.2% |
| IntegratedQuality_SinAltitudeOverSession |  23.6638 ns  |  23.531 ns   |    -0.13   |   -0.6% |
| BestSession_For_MoonBlind                | 806.762  ns  | 793.184 ns   |   -13.6    |   -1.7% |
| BestSession_For_Narrowband               |83391.020 ns  |82477.819 ns  |  -913      |   -1.1% |
```

### Annotation: the AltitudeAtHourAngle entry

The baseline `0.0036 ns/op` for `TargetGeometry_AltitudeAtHourAngle` is
faster than a memory load — physically impossible to evaluate the
function in that time. The benchmark passes literal constants:

```csharp
[Benchmark]
public double TargetGeometry_AltitudeAtHourAngle()
    => TargetGeometry.AltitudeAtHourAngle(2.5, 40.28, 41.27);
```

…and the baseline JIT inlined the call and constant-folded the entire
result to a literal. The post-FMA 5.155 ns is the honest evaluation cost
— FMA happens to block the constant-fold (the JIT's fold pass doesn't
constant-evaluate intrinsic FMA calls). Both numbers are real; they
just measure different things. Real callers (e.g. inside `IntegratedQuality`)
always pass variable args, so the honest measurement is what matters.

### Annotation: why the moon path didn't move

`AstroUtil_GetMoonAltitude` and `MoonSeparation_ObserveAt` both go through
`MoonPosition.ApparentEcliptic`, which has two 60-iter loops. Each
iteration has a `Math.Sin(arg)` and/or `Math.Cos(arg)` call. Trig latency
is ~50 cycles, ~10–20 ns. FMA savings on the arg computation (a few
cycles per iter) overlap with the trig latency and effectively disappear.

Per-call breakdown (rough):
- 60 × Math.Sin + 60 × Math.Cos in LR loop = ~120 trig calls × ~10 ns = ~1200 ns of pure trig
- 59 × Math.Sin in B loop = ~590 ns of pure trig
- Plus the apparent→equatorial conversion (~6 more trig calls)

Total trig ≈ 1800 ns of ~1325 ns total — but that overcounts because
multiple sin/cos calls overlap on the same execution unit. Realistic:
trig dominates 80–90% of the wallclock cost. FMA can save at most 60
ns total on the arg computation (4 mul-adds → 1 FMA × 60 iters), but the
critical path stays on the trig pipe, so most of those savings are
hidden.

**Implication for the deep dive:** if you want the moon path faster, you
need to (a) reduce the trig call count, (b) vectorize them (compute 4
sins in one AVX2 op — possible with hand-rolled approximations or
SLEEF), or (c) reduce iteration count via truncation. FMA alone won't
move it.

### Annotation: where FMA actually paid off

`IntegratedQuality_OverSession` shaved 28 ns (-4.2%) — Simpson with 20+
quality-function evaluations, each calling `AltitudeAtHourAngle` whose
inner kernel is now an FMA. 20 × ~1.4 ns saved per call ≈ 28 ns, matches
exactly.

`Sun_AltAzAt` shaved 2.7 ns (-1.8%) — Horner × 3 (3-4 coeffs each) +
equation-of-centre + nutation chains. ~20 FMAs per call × ~0.15 ns each
≈ 3 ns, matches.

These are the canonical cases where FMA wins: chained polynomial code
that the trig calls don't already dominate.

## Gotchas to remember

1. **FMA can block JIT optimizations that vanilla `a*b+c` allowed.** The
   v1 single-FMA microbench was slower than vanilla because the JIT
   hoisted vanilla but not FMA. Don't sprinkle `Math.FusedMultiplyAdd`
   on loop-invariant expressions.
2. **Round-once changes last-bit results.** Tests that assert bit-exact
   doubles will break. Tests with epsilon tolerance should pass since
   FMA is strictly *more* accurate — but verify after a change. The
   Astronomy.Core test suite uses tolerance-based asserts; all 321 passed
   after this pass.
3. **Software fallback is slow.** Always confirm `Fma.IsSupported` at
   startup or via BDN's `HardwareIntrinsics` header before relying on
   FMA for performance.
4. **RyuJIT does not auto-contract `a*b+c` to FMA.** Strict IEEE-754
   semantics. The opt-in is explicit. (Some compilers — gcc with
   `-ffp-contract=fast`, MSVC with `/fp:fast` — do auto-contract, but
   .NET does not.)
5. **FMA latency = 4 cyc, mul latency = 3 cyc.** Single isolated FMAs
   can be slower than a vanilla mul+add if the mul and add can issue in
   parallel (no chain). The win comes from chained code where the mul→add
   serialization would have cost 6 cycles. Use FMA in chains, not on
   isolated expressions.

## Open directions for the deep dive

Four follow-on opportunities, ranked by likely impact:

### 1. Specialized non-`params` Horner overloads — easy, real win

`MeeusUtility.Horner(double x, params double[] coeffs)` allocates a
`double[]` every call. The HotPathBenchmarks show `Sun_AltAzAt` allocates
144 B / call and `BestSession_For_MoonBlind` allocates 168 B; these are
the Horner array allocations. At chart-paint scale, that's MB/sec of
short-lived garbage.

Add specialized overloads:

```csharp
public static double Horner(double x, double c0, double c1, double c2, double c3)
    => Math.FusedMultiplyAdd(
         Math.FusedMultiplyAdd(
           Math.FusedMultiplyAdd(c3, x, c2), x, c1), x, c0);

public static double Horner(double x, double c0, double c1, double c2, double c3, double c4)
    => /* 4 chained FMAs */;
```

Call sites in `SunEphemeris.Apparent` / `MoonPosition.ApparentEcliptic`
become non-allocating. Expected impact: eliminates ~150 B/call × every
call. Wall-clock impact is probably small (allocation is cheap on
short-lived objects in Gen0), but the GC pressure reduction is real.

Effort: ~1 session.

### 2. `Vector<double>` SIMD on MoonPosition table loops — medium, biggest perf upside

The two 60-iter loops in `MoonPosition.ApparentEcliptic` are the perfect
SIMD target: same operation on each iter, independent except for the
final accumulator. AVX2 gives 4 doubles per vector → 15 SIMD iters
replace 60 scalar iters. Each SIMD iter does 4 `Math.Sin(arg)` /
`Math.Cos(arg)` calls though, and **scalar trig doesn't vectorize
automatically** in .NET.

Two sub-approaches:

a. **Hand-rolled sin/cos approximation** — `System.MathF` doesn't help
   (single precision is too coarse for arcsecond-accurate moon position).
   Roll a polynomial-approximation sin/cos accurate to ~12 decimal digits
   over `[-π, π]`, vectorize via `Vector<double>`. This is well-trodden
   territory (see SLEEF, MIPP).
b. **Lookup-table sin/cos** — precompute sin/cos at 0.001° resolution,
   linear-interpolate inside the lookup. Lookups vectorize cleanly with
   AVX2 gather. ~10× faster than scalar `Math.Sin` if the table fits
   in L2.

Either path needs careful precision verification — the moon position
test suite is the safety net (test cases at known JDs with known
geocentric positions).

Effort: ~3–5 sessions. Biggest perf upside in the whole portfolio.

### 3. Estrin's-scheme polynomial parallelization — easy, modest win

A Horner chain serializes the polynomial: `((c3*x + c2)*x + c1)*x + c0`.
Each step depends on the previous → no ILP within one polynomial.

Estrin's scheme rewrites as two independent half-chains:

```
A = c0 + c1*x          // computed in parallel with...
B = c2 + c3*x          // ...this
result = A + B*x²      // x² computed once, used here
```

For a 4-coefficient polynomial, the critical path drops from 3 chained
FMAs (~12 cyc) to 2 chained FMAs + 1 mul (~10 cyc), and the two halves
pipeline. The gain grows roughly logarithmically with polynomial length.

For our 4-term Horners (mean obliquity, mean anomaly, ecliptic
longitude) the win is small. For longer polynomials (the lunar mean-
longitude is 4 terms, but in higher-precision Meeus there are 6-term
polynomials), Estrin's pays.

Effort: ~1 session. Adds a `HornerEstrin` overload alongside the existing
Horner.

### 4. Explicit `Avx2` / `Fma` intrinsics — hard, niche

When `Vector<T>` doesn't express what you need (e.g. specific shuffle
patterns, fused-multiply-subtract variants, masked operations), drop
into `System.Runtime.Intrinsics.X86.{Fma,Avx2,Avx512F}` and hand-write
the SIMD. The Astronomy.Core public API would stay the same; the
intrinsics path goes behind an `if (Fma.IsSupported) { ... } else { ... }`
fence.

This is what you'd do for absolute peak perf on a specific platform.
Not worth doing until #2 hits its limits.

Effort: per kernel, ~1 session including precision validation.

## References

- **Library commit `b83a0d8`** — the FMA hygiene pass. Touches
  MeeusUtility, MoonPosition, SunEphemeris, TargetGeometry, SkyBrightness.
- **`Astronomy.Core.Benchmarks/FmaBenchmarks.cs`** — v2 microbench
  with Horner4 / Horner8 / SphericalAlt twins.
- **`Astronomy.Core.Benchmarks/HotPathBenchmarks.cs`** — the
  realistic workload suite, where the per-call costs above were measured.
- AMD Software Optimization Guide for Zen 4 (PDF, search "vfmadd
  latency") — definitive numbers for the per-uop costs on this dev
  machine.
- .NET runtime source: `src/coreclr/jit/hwintrinsic*` — the JIT's FMA
  lowering. Search for `NI_X86Base_FusedMultiplyAdd`.
- Microsoft docs: `System.Runtime.Intrinsics.X86.Fma` namespace — the
  managed intrinsics surface; not used in this pass (only the JIT-
  recognised `Math.FusedMultiplyAdd`) but the entry point for direction
  #4 above.
- BenchmarkDotNet docs: `[DisassemblyDiagnoser]` attribute — automated
  way to dump the JIT-emitted asm next to the benchmark results, no
  env var dance.

## Resuming the deep dive

When you next pick this up:

1. **Re-read this file** + the FMA pass commit (`git show b83a0d8` in the
   Library repo).
2. **Run the existing benchmarks** to confirm the post-FMA numbers still
   hold on whatever hardware you're on: `Astronomy.Core.Tests.exe
   --filter '*FmaBenchmarks*'` and `--filter '*HotPathBenchmarks*'`.
3. **Dump the JIT asm** for one of the FMA'd methods to confirm
   `vfmadd*sd` is still being emitted:
   ```cmd
   set DOTNET_JitDisasm=AltitudeAtHourAngle
   Astronomy.Core.Tests.exe --filter '*HotPath*AltitudeAt*'
   ```
4. **Pick a direction** from the "Open directions" section. #1
   (specialized Horner overloads) is the lowest-risk warm-up; #2
   (vectorized moon table) is the biggest upside.
