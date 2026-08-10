# diagnostics-portable-core — design

## Context

See `proposal.md` — Why. Current state that shapes the approach:

- `ObservationSession` already has the right seam: the internal test ctor takes
  `newScreenshotPath` and `capture` delegates; only the public `Begin` factory hard-wires
  `ScreenCapture.ToPng`. The change promotes an existing seam, not a new abstraction.
- TSM's `DiagnosticsWindow` is self-contained WinUI (no AppDialog dependency); its only app
  couplings are `Shared.UiTask.FireAndLog` (3 call sites), the title-bar icon path, and the
  system `AccentButtonStyle` resource.
- `Directory.Build.props` stamps `net10.0-windows` on every csproj; per-project TFM overrides are
  the supported escape hatch.
- Portfolio constraints: MSBuild.exe for the sln (mixed C++/C# graph), x64 the only wired config,
  xUnit v3 test projects only, `TreatWarningsAsErrors` everywhere, no back-compat shims.

## Goals / Non-Goals

**Goals:**

- Compiler-enforced platform neutrality for the core (TFM `net10.0`, zero platform packages).
- One public wiring shape for observation capture that serves dialogs, programmatic Windows
  consumers (IS), and future non-Windows backends identically.
- The WinUI shell ships de-apped: zero TSM-specific residue in the satellite.

**Non-Goals:**

- No general WinUI dialog substrate (AppDialog graduation stays parked for ISM).
- No multi-targeting anywhere — each project has exactly one TFM (floors for libraries).
- No new test projects; no WinUI test hosting.

## Decisions

### D1 — `Begin` takes the capture delegate as a required parameter

`ObservationSession.Begin` gains `Func<(int X, int Y, int Width, int Height), string, string?> capture`
(the internal ctor's existing delegate type, unchanged). Screenshot-path minting stays internal —
`Begin` keeps using `Log.NewObservationScreenshotPath`; only the platform-dependent piece moves to
the caller. The dialog satellites pass `ScreenCapture.ToPng` themselves (they reference
`.Windows`), so **app** consumers of the dialogs never see the capture parameter; programmatic
consumers (IS) and future platforms wire it explicitly.

- *Alternative — `Begin` moves to `.Windows` as a factory:* rejected; core's public surface would
  be unusable standalone, and every future platform would need its own factory for one line of wiring.
- *Alternative — `ICaptureBackend` interface:* rejected; interface ceremony over a single method
  the delegate already models. Cleanest target state wins.

### D2 — `AppLogIdentity` gains `Assembly? VersionAssembly` (default null = entry assembly)

`Log.TryGetBuildVersion` reads `AssemblyInformationalVersionAttribute` (falling back to
`AssemblyName.Version`) from `identity.VersionAssembly ?? Assembly.GetEntryAssembly()`. A plugin
passes `typeof(<its plugin type>).Assembly`; standalone apps change nothing.

- *Alternative — explicit `string? BuildVersion`:* rejected; every plugin consumer would
  re-implement the informational-version extraction (MinVer stamps land in the attribute), and a
  hand-typed string can silently go stale.

### D3 — `UiTask.FireAndLog` is inlined, not lifted

The WinUI shell gets a private fire-and-forget helper (`try { await work(); } catch (Exception ex)
{ Log.Warn(label, ex); }`) covering its three handlers. TSM keeps its own `UiTask` for its other
uses.

- *Alternative — export the helper from the satellite:* rejected; an app-framework utility in a
  diagnostics package is the same scope leakage the AppDialog parking avoids.

### D4 — WinUI shell entry point: `ShowOrFocus(Window owner, Func<string> contextProvider, string? iconPath = null)`

Ported semantics unchanged (singleton focus-existing, center-over-owner, always-on-top, delayed
capture, Ctrl+Enter commit). `iconPath` null ⇒ no `SetIcon` call; TSM passes its asset path. The
`AccentButtonStyle` lookup stays — it is a system resource present in every WinUI app.

### D5 — Project & TFM mechanics

| Project | TFM | Key items |
|---|---|---|
| `Astronomy.Diagnostics` | `net10.0` (explicit override, commented) | drop `System.Drawing.Common`; keep `InternalsVisibleTo` |
| `Astronomy.Diagnostics.Windows` (new) | `net10.0-windows` | `System.Drawing.Common`; `ScreenCapture` moves verbatim; refs core |
| `Astronomy.Diagnostics.WinUI` (new) | `net10.0-windows10.0.19041.0` | `UseWinUI`; `Microsoft.WindowsAppSDK` pinned to TSM's current (2.3.1); refs core + `.Windows` |
| `Astronomy.Diagnostics.WinForms` | unchanged | adds `.Windows` ref; `Begin` call site passes `ScreenCapture.ToPng` |
| `Astronomy.Diagnostics.Tests` | unchanged (props default) | stays the **only** test project; adds `VersionAssembly` cases; optional `ScreenCapture` smoke test via a `.Windows` ref |

Both new projects: `Platforms` `AnyCPU;x64`, x64 sln rows wired, zero-warning ratchet inherited,
**no `xunit.v3` package** (the known non-test-project trap). Shells stay unit-test-free by design —
testable logic lives in core (per the 2026-07-24 lift); a WinUI test project would force a packaged
MSTest host, breaking the xUnit-v3 convention for glue-only coverage.

### D6 — Consumer-side sequencing (TSM port + TFM unification)

After the AL tag: TSM deletes `Support\DiagnosticsWindow.cs`, references `.WinUI`, re-wires Ctrl+N
to `ShowOrFocus(owner, contextProvider, iconPath)`, and raises its TFM to
`net10.0-windows10.0.26100.0`. TP raises to `26100.0` in the same window (one-line csproj edits;
XFM already there). The WindowsAppSDK version lockstep (satellite 2.3.1 ⇄ TSM/ISM ≥ 2.3.1) is
recorded in `CONSUMERS.md`.

## Risks / Trade-offs

- [WindowsAppSDK drift: TSM upgrades ahead of the satellite (or vice versa)] → single recorded
  lockstep in `CONSUMERS.md`; NuGet unifies on the higher version at app build, so the failure mode
  is a restore warning, not silent breakage. Upgrade the satellite pin alongside the first consumer
  that moves.
- [WinUI class library inside the mixed C++/C# sln misbehaves under `dotnet build`] → already the
  portfolio rule: sln builds go through MSBuild.exe; `VERIFICATION.md` gains the satellite note.
- [Core's TFM drop surfaces a hidden Windows API use at compile time] → that is the point
  (spec: "cannot silently regain a platform coupling"); fix by moving the offender to `.Windows`,
  never by raising the core TFM.
- [TSM 19041 → 26100 TFM raise changes Windows SDK projections] → compile-time surface only; all
  dev/runtime machines are Windows 11. Build TSM before committing the raise.
- [Analyzer deltas: `net10.0` core loses Windows-only analyzers, new projects gain recommended-mode
  findings under `TreatWarningsAsErrors`] → expect a small warning-fix pass on first build; budget
  it in tasks rather than suppressing.

## Migration Plan

Per portfolio rule there is no back-compat path — one pass, clean rebuild:

1. Library: retarget core, add `.Windows` + `.WinUI` projects, re-wire `.WinForms`, extend
   `AppLogIdentity`/`Log`, port `DiagnosticsWindow` in, update `.Tests`. Full x64 MSBuild + tests.
2. Docs ride the same commit (`ARCHITECTURE.md`, `CONSUMERS.md`, `ROADMAP.md` rescope, `CHANGELOG.md`).
3. Publish AL tag (AL-first rule).
4. TSM port + TFM raise; TP TFM raise; verify via `..\build-all.ps1` DRC. Dialogs verified
   manually in-app (Ctrl+N flow) — build alone does not prove the shells.

Rollback: revert the commit(s); no persisted-data or schema surface is touched.

## Open Questions

- ~~Include the `ScreenCapture` smoke test in `.Tests` (real screen grab on the build machine) or
  leave `ScreenCapture` covered by in-app use?~~ **Resolved at apply time (2026-08-10): smoke tests
  added** (`ScreenCaptureSmokeTests`), asserting the best-effort contract — never throws, PNG exists
  iff a path is returned — so a locked/headless session cannot flake them while the encode +
  folder-creation path still runs every test pass.
