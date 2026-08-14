# ARCHITECTURE.md — module index

**Charter.** Subsystem mechanics — *how each module of the `Astronomy` library is built and works*.
Since 2026-08-11 this file is the **index**: the mechanics live in one file per buildable module
under [`docs/architecture/`](docs/architecture/) (the doc crossed the single-file size where it
still helped; split per `ROADMAP.md`'s adjudicated job). A reference elsewhere to
"`ARCHITECTURE.md` § *Astronomy.X*" resolves through the row below. Forward-looking design lives in
`ROADMAP.md`; build/test/benchmark in `VERIFICATION.md`; the consumer contract in `CONSUMERS.md`;
domain rationale routes through `DOMAIN.md`.

**Cross-cutting facts worth knowing before any module:** x64 is the only build config the sln
declares (the old AnyCPU/x86 alias entries were removed 2026-08-13 — *Solution overview*); the sln is a
mixed C++/C# graph (build with `MSBuild.exe`, never `dotnet build Astronomy.sln`); every test
project is xUnit v3.

| Module file | What it covers |
|---|---|
| [Solution overview](docs/architecture/solution.md) | `Astronomy.sln` config census: seventeen buildable projects, x64-only sln configs, the view-only PCL solution folder. |
| [Astronomy.Core](docs/architecture/core.md) | The dependency-free base: Meeus math, targets/locations/night/session/moon/sun/brightness. **Architectural conventions** (units, hemisphere/angle rules, immutability), **thread safety** (permitted static state + audit recipe), **code organization** (the folder map), and `Astronomy.Core.Tests` / `.Benchmarks`. |
| [Astronomy.XISF](docs/architecture/xisf.md) | XISF read + block re-store: header/keyword reader, `<Image geometry>` handling, the Tier-3 codec layer (`XisfBlockCompression`, `XisfImageReader`, `XisfChecksumVerifier`, `XisfBlockRewriter`), and `Astronomy.XISF.Tests`. |
| [Astronomy.Diagnostics](docs/architecture/diagnostics.md) | The four-assembly layered stack: TFM-neutral core (`Log`, `ObservationSession`), `.Windows` capture backend, `.WinForms`/`.WinUI` Ctrl+N shells, `DiagnosticsHotkey`, and `Astronomy.Diagnostics.Tests`. |
| [Astronomy.Catalog](docs/architecture/catalog.md) | Scanner + catalog + reconciliation: `Scan/` (directory walk, framing clusters, fail-fast boundaries), `Build/` (`TargetResolver` anchoring), `Schema/`, `TargetScheduler/` (reader/editor/writer, editable schema, write-back), `Reconcile/`, and `Astronomy.Catalog.Tests`. |
| [Astronomy.NINA](docs/architecture/nina.md) | NINA-integration/planning library: the Phase B `Target` shape, `Persistence/` DTOs, `ReportToTargetAdapter`, and `Astronomy.NINA.Tests`. |
| [Astronomy.Contracts.Tests](docs/architecture/contracts-tests.md) | The contract bench pinning the `CONSUMERS.md` pinout: covered-or-registered rule, bench scope, the `LogProcessGlobal` collection rule. |
| [Astronomy.PCL / .Native](docs/architecture/pcl.md) | The managed P/Invoke wrapper + native C++ shim over the vendored PixInsight PCL: local-build story, interop/C-ABI rules, ISA floor policy. |
