# diagnostics-portable-core

## Why

`Astronomy.Diagnostics` is chartered as the portfolio's UI-free diagnostics core, but two Windows
couplings make that claim aspirational rather than compiler-enforced: the project targets
`net10.0-windows` (inherited from `Directory.Build.props`) and it contains `ScreenCapture`
(System.Drawing.Common — Windows-only, `PlatformNotSupportedException` elsewhere), which
`ObservationSession.Begin` wires statically. Two now-real future consumers break on this:
the planned **ISM Android port** cannot reference a `net10.0-windows` library at all (compile-time
wall, not degradation), and **IS** (the NINA-plugin scheduler, WPF guest in NINA's process) exposes
a latent identity bug — `Log`'s session `build=` stamp reads `Assembly.GetEntryAssembly()`, which in
a plugin host reports NINA's version, not the consumer's.

The change also ships the **`.WinUI` dialog satellite now** (folded in 2026-08-10, revising the
ROADMAP parking). Two findings changed the calculus: (1) code inspection shows TSM's
`DiagnosticsWindow` does **not** depend on TSM's `AppDialog` layer — it is a self-contained
`Window` whose only app couplings are a fire-and-forget helper, an icon path, and a system style —
so the "AppDialog graduation" blocker applies to a future general dialog layer, not to this dialog;
(2) TSM is in active development now but goes quiet once focus shifts to ISM/IS, so porting TSM to
the satellite is cheap today and expensive later. The consumer-port window is the same one the
`Begin` re-wire needs anyway — one TSM touch, not two.

## What Changes

- **`Astronomy.Diagnostics` core retargets to `net10.0`** (portable; overrides the props-level
  `net10.0-windows` default) and drops its `System.Drawing.Common` package reference. `Log`,
  `AppLogIdentity`, and `ObservationSession` are already pure BCL apart from the items below.
- **`ScreenCapture` moves to a new `Astronomy.Diagnostics.Windows` project** (`net10.0-windows`) —
  the shared Win32 pixel-grab layer that the WinForms/WinUI shells and programmatic Windows
  consumers (IS) sit on.
- **BREAKING: `ObservationSession.Begin` loses its static `ScreenCapture` wiring.** The capture
  delegate becomes caller-supplied (the existing internal test-seam ctor already models this — the
  change promotes that seam to the public factory; exact shape decided in design.md). All dialog
  shells re-wire.
- **New `Astronomy.Diagnostics.WinUI` project** (`net10.0-windows10.0.19041.0`, `UseWinUI`,
  `Microsoft.WindowsAppSDK` — version aligned with TSM, currently 2.3.1): `DiagnosticsWindow`
  ported from TSM as the library's WinUI Ctrl+N shell. Port de-apps the class: title-bar icon
  becomes a caller parameter, the `UiTask.FireAndLog` helper is lifted into the satellite or
  inlined (design.md), the `AccentButtonStyle` system resource stays. TSM deletes its app-side
  copy and consumes the satellite; ISM gets the dialog for free on day one. The TFM pins WinUI's
  **minimum** (19041), never an app's SDK version, so no consumer is forced to raise.
- **`Astronomy.Diagnostics.WinForms` references `.Windows`** for the capture backend; its dialog is
  otherwise unchanged.
- **`AppLogIdentity` gains a consumer-supplied build-version source** so the session `build=` stamp
  is correct when the consumer is not the process's entry assembly (plugin-host case). Entry-assembly
  remains the convention for standalone apps; the mechanism (explicit version vs. assembly reference)
  is a design.md decision.
- **Coordinated consumer-side errand (rides the same port window, own repos/commits):** unify the
  three apps' TFMs at `net10.0-windows10.0.26100.0` (TP `10.0.19041` → `26100.0`, TSM `19041.0` →
  `26100.0`; XFM already there). App TFMs float high while library TFMs floor low — they never need
  to match; this is consistency, not a compatibility requirement.
- Per portfolio rule: no back-compat shims — consumers rebuild against the new shape in one pass.

Out of scope (deliberately): the **AppDialog-layer graduation** (drag/centering/key conventions as
a general WinUI dialog substrate) — stays parked until ISM per ROADMAP, now correctly scoped as
independent of the diagnostics dialog; any `.Wpf` dialog shell for IS; any Android capture backend.

## Capabilities

### New Capabilities

- `diagnostics-platform-layering`: the layering contract for the diagnostics stack — the core
  assembly is TFM-neutral and references no UI framework or platform-only package; screen capture is
  a platform-supplied backend injected at the observation-session seam; per-framework dialog shells
  are satellites at the lowest TFM their framework permits (WinForms at `net10.0-windows`, WinUI at
  `net10.0-windows10.0.19041.0`) sharing one Windows capture layer; the log identity carries enough
  consumer identity to stamp sessions correctly whether the consumer owns the process or is a guest
  in a host (plugin) process.

### Modified Capabilities

_None — no existing spec covers diagnostics._

## Impact

- **Library projects**: `Astronomy.Diagnostics` (TFM, package removal, `Begin` surface,
  `AppLogIdentity`), new `Astronomy.Diagnostics.Windows` and `Astronomy.Diagnostics.WinUI`
  (+ SLN entries, x64 config wiring), `Astronomy.Diagnostics.WinForms` (new project ref, `Begin`
  call-site), `Astronomy.Diagnostics.Tests` (largely insulated — tests already use the delegate
  seam; add coverage for the version-stamp source).
- **New external dependency**: `Microsoft.WindowsAppSDK` enters the Library via the `.WinUI`
  satellite only — a version-lockstep with WinUI consumers (TSM, later ISM) to record in
  `CONSUMERS.md`. TP/XFM never reference the satellite, so the dependency stays quarantined to the
  WinUI leg.
- **Consumers**: TSM deletes `Support\DiagnosticsWindow.cs`, references `.WinUI` (+ transitively
  `.Windows`), re-wires the Ctrl+N accelerator to the satellite's entry point, and raises its TFM
  to `26100.0`; TP rebuilds via the WinForms satellite and raises its TFM to `26100.0`; XFM
  unaffected (no AL diagnostics dependency today). Impact-check via `CONSUMERS.md` pinout +
  `Astronomy.Contracts.Tests` + the `..\build-all.ps1` DRC, not a single-repo grep.
- **Docs**: `ARCHITECTURE.md` (module census + two new project sections), `CONSUMERS.md` (pinout +
  WindowsAppSDK lockstep), `ROADMAP.md` (rescope the parked `.WinUI` note: dialog ships here,
  AppDialog graduation remains parked for ISM; recently-shipped digest), `CHANGELOG.md`. Doc
  updates ride the code commit.
- **Release ordering**: AL-first rule applies as usual; the TSM/TP ports need a published AL tag
  carrying this change.
