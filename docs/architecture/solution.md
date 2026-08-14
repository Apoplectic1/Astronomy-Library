# Solution overview

*(ARCHITECTURE reference set — split out of the root `ARCHITECTURE.md` 2026-08-11, one file per buildable module; the root file is the index. Charter: subsystem mechanics — how this module is built and works.)*

`Astronomy.sln`: **x64 is the only solution config** (Debug/Release × x64) — since 2026-08-13, when the long-unmaintained AnyCPU/x86 alias entries were removed after VS 18.5's config checker flagged four projects whose alias rows mapped to project configurations that don't exist (Core.Tests, NINA.Tests, PCL, XISF.Tests — all pinned `<Platforms>x64</Platforms>`; the removal also dropped the eight PCL projects' dead x86→`Win32` rows and their stray Any CPU `Build.0` entries). Always build x64. The sln holds seventeen buildable projects, plus a `PCL` Solution Folder containing eight view-only PCL projects (`PCL.vcxproj`, the six 3rd-party `.lib`s — `cminpack`, `lcms`, `lz4`, `RFC6234`, `zlib`, `zstd` — and the `xisf.vcxproj` CLI utility) sourced from `Library\PCL\`. The PCL projects have `ActiveCfg` set but `Build.0` omitted: full source visibility and IntelliSense / F12 in the IDE, but `Build Solution` and `msbuild Astronomy.sln` skip them. PCL rebuilds happen manually via `Library\PCL\src\pcl\windows\vc18\PCL.sln`.

The seventeen buildable projects:
