# Astronomy.Diagnostics

*(ARCHITECTURE reference set — split out of the root `ARCHITECTURE.md` 2026-08-11, one file per buildable module; the root file is the index. Charter: subsystem mechanics — how this module is built and works.)*

Since 2026-08-10 (`diagnostics-portable-core`) Diagnostics is a **four-assembly layered stack** —
a TFM-neutral core, a Windows capture backend, and per-UI-framework dialog shells. Layering
contract: `openspec/specs/diagnostics-platform-layering/`. Library layers pin the *lowest* TFM
they need (so no consumer is ever forced to raise its own); consumer apps float high — the two
never need to match.

- **`Astronomy.Diagnostics`** — **`net10.0` (TFM-neutral — the one deliberate override of the
  props-level `net10.0-windows` default)**, `Nullable enable`, `ImplicitUsings enable`, zero
  package refs. The portfolio's shared **logging + observation contract** (built 2026-06-11; made
  platform-neutral 2026-08-10 so future non-Windows consumers — an Android app, a Linux host — can
  reference it; a platform API creeping in fails the *build*, which is the enforcement). **Why it
  is its own assembly and not part of `Astronomy.Catalog`:** shared observation tooling stays out
  of Catalog on purpose — Catalog is a schema/build contract, not a grab-bag utility library
  (boundary rule predating Diagnostics itself: `..\TargetSchedulerManager` archive review
  2026-06-10 §4.4; graduated here 2026-08-03). *Convention-as-code*: `Log` (always-on
  Info/Warn/Error severity + gated `Diag` channels — the default comes from
  `AppLogIdentity.DiagDefault`, the build-derived value the app passes to `Init` since a shared
  library compiles once and can't read the consumer's `#if DEBUG`; the app's env var is the
  *override*, read first by `ResolveEnabledCategories`; session rotation; `%APPDATA%\<app>\Logs\`
  structure; USER_OBS protocol — all configured once per app via `AppLogIdentity`).
  `AppLogIdentity.VersionAssembly` (2026-08-10) covers the plugin-host case: a consumer that is a
  guest in another process (IS inside NINA) names its own assembly so the session `build=` stamp
  doesn't report the host's version; null = entry assembly, the standalone-app default.
  **`ObservationSession`** (2026-07-24) is the app-agnostic observation orchestration — id
  minting, START/CAP/END/CANCEL sequencing with the exactly-one-terminator guarantee, capture
  counting, the guarded context-provider call, status-text wording, and the
  hide → settle → grab → reshow choreography (session-level `settleDelayMs`: 450 for WinUI's DWM
  fade, 0 for WinForms' synchronous `Hide()`+`Refresh()`). The shells hand `Begin` **four**
  delegates (owner bounds, hide, show, and — since 2026-08-10 — the platform's `capture`); awaits
  keep the caller's `SynchronizationContext` so the delegates always run on the UI thread.
  `InternalsVisibleTo` exposes the internal ctor (fake capture + path seam) to
  `Astronomy.Diagnostics.Tests`. No `Astronomy.*` deps; pure-managed → builds with `dotnet build`.
- **`Astronomy.Diagnostics.Windows`** — `net10.0-windows` (+ `System.Drawing.Common`), refs core.
  The Windows capture backend: `ScreenCapture.ToPng` (Win32 `Graphics.CopyFromScreen` — grabs the
  literal rendered pixels regardless of UI framework, unlike framework draw-to-bitmap APIs that
  return blank under some compositors). Best-effort: path on success, null on any failure, never a
  throw. This is the platform piece the core deliberately lacks; each future platform supplies its
  own equivalent behind the session's `capture` delegate.
- **`Astronomy.Diagnostics.WinForms`** — `net10.0-windows`, `UseWindowsForms`, refs core +
  `.Windows`. `DiagnosticsDialog`: the WinForms Ctrl+N shell (2026-08-06) — framework glue only
  (Form, controls, singleton focus-existing rule, the session delegates). Consumed by TP and XFM.
  **`DiagnosticsHotkey.Register(owner, contextProvider)`** (2026-08-11) is the shared app-level
  wiring: an application-level `IMessageFilter` that opens/focuses `DiagnosticsDialog` on Ctrl+N,
  covering MenuStrip menu-mode and modal WinForms dialogs that a consumer-side `ProcessCmdKey`
  override misses (native modal loops stay out of reach by Win32 design). Register-once — a second
  call throws. **The capture contract is capture-at-OK-time-only, uniformly across every consumer
  (WinForms and WinUI alike)**: an invoke-time-capture variant was shipped and reverted the same day
  specifically to preserve that cross-consumer uniformity; transient-UI shots stay on the
  delayed-capture workflow (`CHANGELOG.md` §§ 2026-08-11).
- **`Astronomy.Diagnostics.WinUI`** — **`net10.0-windows10.0.19041.0`** (the WinUI *floor*, not
  any app's SDK version), `UseWinUI`, `Microsoft.WindowsAppSDK` (pin recorded in `CONSUMERS.md` —
  WinUI consumers must reference ≥ the pin), refs core + `.Windows`. `DiagnosticsWindow`: the
  WinUI Ctrl+N shell, ported from TSM 2026-08-10 (`ShowOrFocus(owner, contextProvider,
  iconPath?)`; icon caller-supplied; TSM's `UiTask` helper inlined) — same shared dialog
  conventions as the WinForms sibling (Enter=newline, Ctrl+Enter=OK, delayed capture, checkpoint
  notes, singleton). Consumers: TSM (ported 2026-08-10), ISM when built. The
  shells stay unit-test-free by design: testable logic lives in the core session (a WinUI test
  project would force a packaged MSTest host — the standing reason to push logic down instead).
- **`Astronomy.Diagnostics.Tests`** — `net10.0-windows` x64, `OutputType=Exe`, refs core +
  `.Windows`. xUnit v3 tests for the `ObservationSession` state machine (delegate ordering,
  terminator idempotency, mid-countdown cancel, busy-overlap no-ops, status wording), the
  `VersionAssembly` stamp, and `ScreenCapture` smoke coverage (contract-shaped: never-throws +
  PNG-lands-iff-success, so a locked/headless session can't flake it) — against a real `Log`
  pointed at a temp `RootOverride`. The **only** Diagnostics test project — no per-shell test
  projects. Separate from the contract bench because `Log` is process-global and
  `LogLifecycleContractTests` must remain the bench's only **`Log.Init` caller** (it is *not* the
  bench's only `Log` toucher — see the `LogProcessGlobal` rule under *Astronomy.Contracts.Tests*).
