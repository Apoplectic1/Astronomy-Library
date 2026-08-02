# Astronomy Library

A Windows-only, x64-only .NET 10 multi-project library for astrophotography tooling: rigorous
astronomy/coordinate math, XISF file handling, imaging-catalog reconciliation, NINA planning
support, shared diagnostics, and a native wrapper over the PixInsight Class Library.

This is the shared foundation of a personal astrophotography software suite. It is developed
locally and mirrored here; the compiled `Astronomy.*` assemblies ship inside the installers of
its consumer apps — [TargetPlanner](https://github.com/Apoplectic1/TargetPlanner) and
[TargetSchedulerManager](https://github.com/Apoplectic1/TargetSchedulerManager). There are no
standalone binary releases of the library itself: tags mark versioned source snapshots.

## Projects

| Project | What it is |
|---|---|
| `Astronomy.Core` | Astronomy & coordinate math (Meeus-based algorithms: positions, rise/transit/set, twilight, moon) |
| `Astronomy.XISF` | XISF (PixInsight image format) header read support |
| `Astronomy.Catalog` | Imaging-library scan + catalog reconciliation store |
| `Astronomy.NINA` | NINA (Nighttime Imaging 'N' Astronomy) planning integration |
| `Astronomy.Diagnostics` | Shared logging / observation-capture contract for the consumer apps |
| `Astronomy.PCL` + `Astronomy.PCL.Native` | Managed P/Invoke wrapper + native C++ shim over the PixInsight Class Library (pixel-level XISF I/O) |
| `*.Tests`, `Astronomy.Contracts.Tests`, `Astronomy.Core.Benchmarks` | xUnit v3 test projects, the consumer-contract harness, and BenchmarkDotNet benchmarks |

## Building

Two tiers — most visitors only need the first.

**Managed tier (self-sufficient).** Everything except the PCL wrapper builds from a fresh
clone with the .NET 10 SDK alone:

```powershell
git clone https://github.com/Apoplectic1/Astronomy-Library Library
cd Library
dotnet build Astronomy.Core -c Release    # any managed project builds this way
```

Note: `dotnet build Astronomy.sln` will **not** work — the solution is a mixed C++/C# graph
and the .NET CLI cannot load the C++ project. Build the full solution with Visual Studio (or
`MSBuild.exe`), **x64 only** — AnyCPU/x86 solution entries are unmaintained aliases.

**Native tier (`Astronomy.PCL.Native`).** Requires the PixInsight Class Library, which is
deliberately not part of this repo. It lives in its own mirror —
[github.com/Apoplectic1/PCL](https://github.com/Apoplectic1/PCL) — and must be cloned *inside*
this repo's working tree, then built first:

```powershell
git clone https://github.com/Apoplectic1/PCL PCL     # from the Library directory
# then build PCL per its README (VS 18.x, v145 toolset, x64)
```

This repo gitignores `PCL/` entirely, so the two repos nest without interacting. Without the
nested clone, `Astronomy.PCL.Native` fails to build — that is documented behavior, not a
broken repo.

## Repo layout notes

`docs/` (dated engineering journal), `NOTEBOOK.md` (lab notebook), `openspec/` (normative
specs + change records), `archive/` (completed/superseded design records), and `.claude/`
(agent tooling) are the project's workshop — development history and machine-read working
docs, not user documentation. `CONSUMERS.md` is the library's public-contract datasheet.

## License & acknowledgments

No license is granted: source is published for reference, all rights reserved.

`Astronomy.PCL.Native` builds against and statically links the
[PixInsight Class Library](https://pixinsight.com/developer/pcl/) by Pleiades Astrophoto S.L.
Any product distributing `Astronomy.PCL.Native.dll` is based on software from the PixInsight
project, developed by Pleiades Astrophoto and its contributors
(<https://pixinsight.com/>).
