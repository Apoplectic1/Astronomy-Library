# CLAUDE.md

**Charter / router.** `Astronomy` is an x64-only, Windows-only .NET 10 **multi-project library** (managed astronomy/coordinate math + XISF read + catalog reconciliation + NINA planning + a native PCL wrapper) consumed by the user's astrophotography apps. This file is the always-loaded **router** — it points at the canonical docs and carries only the gotchas load-bearing enough to know *before* touching the build.

## Docs — where to look

**Reference docs** (current truth, edited in place — route by name):

- **`ARCHITECTURE.md`** — subsystem mechanics, *how each module works*. Organized one section per buildable **module** — test/benchmark projects are covered inside their module's section (Solution overview, then Astronomy.Core + its API conventions/thread-safety/code-organization, .XISF, .Diagnostics, .Catalog, .NINA, .Contracts.Tests, .PCL/.Native + the PCL local-build and interop story). Grep by module name.
- **`ROADMAP.md`** — forward-looking design (open work, planned direction) + a three-line recently-shipped digest. Whole-library direction. Shipped history is *not* here — it's `CHANGELOG.md`.
- **`VERIFICATION.md`** — how to **build / test / benchmark** and the build traps. Read before claiming a build or tests pass. The cross-repo contract DRC is `..\build-all.ps1`.
- **`CONSUMERS.md`** — the Library's de-facto public **contract / datasheet**: what each downstream consumer depends on (the "pinned pinout").
- **`DOMAIN.md`** — the domain layer's home (science/unit conventions, algorithm provenance, multi-consumer strategy): *why* the library models things this way. Charter'd-thin — routes to where each domain truth currently lives.

**Journal** (dated capture — by convention, not an enumerated list). Three homes, by what you're recording:

- **`docs/YYYY-MM-DD-<slug>.md`** — substantial standalone records (reviews, decisions, investigations). `glob docs/*.md` then grep; see `docs/README.md`.
- **`NOTEBOOK.md`** — small chronological lab-notebook findings from doing the work (a measurement, a surprise, a rejected approach).
- **`CHANGELOG.md`** — shipped units of work: append-only, dated, newest first. The library's full shipped history.

Standing truths graduate up out of the journal into the reference docs.

**`archive/`** holds completed/superseded records — *not* current truth, kept for history. Indexed in `archive/README.md`; the one worth knowing cold is `archive/PCL-InterOp.md` (*why* PCL is wrapped Option 3 / Hybrid, P/Invoke not C++/CLI).

**Scope-exclusions** — never scaffold/audit docs into these trees: `PCL/` (vendored ~19 GB PixInsight Class Library, gitignored — has its own upstream READMEs), `BenchmarkDotNet.Artifacts/`, `bin`/`obj`, `.vs/`, and the tooling trees `.claude/`, `.superpowers/`, `openspec/changes/` (workflow churn). `openspec/specs/` is different: **promoted normative specs** (e.g. `moon-brightness-gate`, `contract-assumption-pinning`) — excluded from doc scaffolding but citable, and reference docs may route into it. `Astronomy.Contracts.Tests` is the (already-charter'd) contract harness behind `CONSUMERS.md`.

## Load-bearing gotchas (detail in the docs above)

- **The SLN is a mixed C++/C# graph — build it with `MSBuild.exe`, never `dotnet build Astronomy.sln`.** dotnet errors out on the vcxproj (MSB4278 — it can't load the C++ targets), leaving no native DLL, and any `Astronomy.PCL` test then throws `DllNotFoundException`. Pure-managed projects build fine with `dotnet build` individually. (→ `VERIFICATION.md`)
- **x64 is the only fully-wired config** (Debug/Release × x64) — **always build x64.** The AnyCPU/x86 sln entries are unmaintained aliases that map onto configurations the projects don't declare. (Project-by-project census → `ARCHITECTURE.md`; the gitignored `PCL/` tree's re-extract procedure → `VERIFICATION.md`.)
- **Every test project is xUnit v3** (`OutputType=Exe`; v3 generates the entry point). **Never let `xunit.v3` land on a non-test project** — a "Manage NuGet for Solution → all projects" action forces `OutputType=Exe` and breaks the build later (bit four projects 2026-06-21). A non-test project needing xUnit types uses `xunit.v3.extensibility.core`. (→ `VERIFICATION.md`)
