# CLAUDE.md

**Charter / router.** `Astronomy` is an x64-only, Windows-only .NET 10 **multi-project library** (managed astronomy/coordinate math + XISF read + catalog reconciliation + NINA planning + a native PCL wrapper) consumed by the user's astrophotography apps. This file is the always-loaded **router** — it points at the canonical docs and carries only the gotchas load-bearing enough to know *before* touching the build.

## Docs — where to look

**Reference docs** (current truth, edited in place — route by name):

- **`ARCHITECTURE.md`** — subsystem mechanics, *how each module works*. Organized one section per buildable project (Astronomy.Core + its API conventions/thread-safety/code-organization, .XISF, .Diagnostics, .Catalog, .NINA, .PCL/.Native + the PCL local-build and interop story). Grep by project name.
- **`ROADMAP.md`** — forward-looking design + "Recently shipped" digest. Whole-library direction.
- **`VERIFICATION.md`** — how to **build / test / benchmark** and the build traps. Read before claiming a build or tests pass. The cross-repo contract DRC is `..\build-all.ps1`.
- **`CONSUMERS.md`** — the Library's de-facto public **contract / datasheet**: what each downstream consumer depends on (the "pinned pinout").
- **`DOMAIN.md`** — the domain layer's home (science/unit conventions, algorithm provenance, multi-consumer strategy): *why* the library models things this way. Charter'd-thin — routes to where each domain truth currently lives.

**Journal** (dated capture — by convention, not an enumerated list): `docs/YYYY-MM-DD-<slug>.md` for substantial standalone records (reviews, decisions, investigations) — `glob docs/*.md` then grep; and **`NOTEBOOK.md`** for small chronological lab-notebook findings. Standing truths graduate up into the reference docs.

**`archive/`** holds completed/superseded records — *not* current truth, kept for history. Includes the PCL interop decision record (`archive/PCL-InterOp.md`: *why* PCL is wrapped Option 3 / Hybrid, P/Invoke not C++/CLI), the 2026-05-18 library review set, the SIMD/FMA investigation, and the parked PCL wrapper-extension plan (`archive/PCL-WrapperRoadmap.md`). See `archive/README.md`.

**Scope-exclusions** — never scaffold/audit docs into these trees: `PCL/` (vendored ~10 GB PixInsight Class Library, gitignored — has its own upstream READMEs), `BenchmarkDotNet.Artifacts/`, `bin`/`obj`, `.vs/`, `.claude/`, `.superpowers/`. `Astronomy.Contracts.Tests` is the (already-charter'd) contract harness behind `CONSUMERS.md`.

## Load-bearing gotchas (detail in the docs above)

- **The SLN is a mixed C++/C# graph — build it with `MSBuild.exe`, never `dotnet build Astronomy.sln`.** dotnet silently produces a managed-only build (skips the vcxproj) and any `Astronomy.PCL` test then throws `DllNotFoundException`. Pure-managed projects build fine with `dotnet build` individually. (→ `VERIFICATION.md`)
- **x64 is the only fully-wired config** (Debug/Release × x64) — always build x64. The solution also exposes AnyCPU/x86 sln entries, but they're unmaintained aliases (most projects map them to AnyCPU/Win32, a few newer ones to x64). The vendored `PCL/` tree is gitignored — re-extract from `PCL\PCL-master.zip` on a fresh clone. (→ `ARCHITECTURE.md`)
- **Every test project is xUnit v3** (`OutputType=Exe`; v3 generates the entry point). **Never let `xunit.v3` land on a non-test project** — a "Manage NuGet for Solution → all projects" action forces `OutputType=Exe` and breaks the build later (bit four projects 2026-06-21). A non-test project needing xUnit types uses `xunit.v3.extensibility.core`. (→ `VERIFICATION.md`)
