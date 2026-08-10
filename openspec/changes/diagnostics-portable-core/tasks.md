# diagnostics-portable-core — tasks

## 1. New platform layer: Astronomy.Diagnostics.Windows

- [x] 1.1 Create `Astronomy.Diagnostics.Windows` project (`net10.0-windows`, `Platforms` `AnyCPU;x64`, zero-warning ratchet, `System.Drawing.Common`, project ref to core; no `xunit.v3`)
- [x] 1.2 Move `ScreenCapture.cs` from core into `.Windows` (namespace stays `Astronomy.Diagnostics`; adjust its `Log.Warn` usage — core ref direction is `.Windows` → core, so the call keeps working)
- [x] 1.3 Add the project to `Astronomy.sln` with x64 Debug/Release rows wired

## 2. Core neutralization: Astronomy.Diagnostics

- [x] 2.1 Retarget core to `net10.0` (explicit, commented override of the props default) and drop the `System.Drawing.Common` package reference
- [x] 2.2 Promote the capture seam: `ObservationSession.Begin` gains the required `capture` delegate parameter (internal ctor's existing type); remove the static `ScreenCapture.ToPng` wiring
- [x] 2.3 Add `Assembly? VersionAssembly` to `AppLogIdentity` (default null) and route `Log.TryGetBuildVersion` through `identity.VersionAssembly ?? Assembly.GetEntryAssembly()`
- [x] 2.4 Sweep core XML docs for the new shape (Begin's parameter docs, ScreenCapture cross-refs now pointing at `.Windows`)

## 3. WinForms shell rewire

- [x] 3.1 Add `.Windows` project ref to `Astronomy.Diagnostics.WinForms`; `DiagnosticsDialog`'s `Begin` call site passes `ScreenCapture.ToPng`

## 4. New WinUI shell: Astronomy.Diagnostics.WinUI

- [x] 4.1 Create `Astronomy.Diagnostics.WinUI` project (`net10.0-windows10.0.19041.0`, `UseWinUI`, `Microsoft.WindowsAppSDK` pinned to TSM's current version, `Platforms` `AnyCPU;x64`, refs core + `.Windows`); add to sln with x64 rows
- [x] 4.2 Port `DiagnosticsWindow` from TSM: namespace `Astronomy.Diagnostics.WinUI`, `ShowOrFocus(Window owner, Func<string> contextProvider, string? iconPath = null)` (null ⇒ no `SetIcon`), inline private fire-and-forget helper replacing `Shared.UiTask.FireAndLog`, capture wired to `ScreenCapture.ToPng`
- [x] 4.3 XML-doc the shell to the shared-dialog contract (consumer-agnostic wording — no TSM/ISM terminology)

## 5. Tests

- [x] 5.1 Update `Astronomy.Diagnostics.Tests` for the new `Begin` surface (existing tests use the internal ctor — verify they compile untouched; add one public-`Begin` wiring test with a fake capture delegate)
- [x] 5.2 Add `VersionAssembly` coverage: supplied assembly stamps its informational version; null falls back to entry assembly
- [x] 5.3 Resolve the design.md open question: add a `ScreenCapture` smoke test (real grab via a `.Windows` ref) or record the decision to rely on in-app coverage — RESOLVED: smoke tests added, written to the best-effort contract (never-throws + PNG-lands-iff-success) so a locked/headless session can't flake them

## 6. Build, verify, document

- [x] 6.1 Full x64 MSBuild.exe sln build + `dotnet test --no-build` — expect and fix the analyzer-delta pass on new/retargeted projects (no suppressions) — clean on first pass: 0 warnings, Diagnostics 22/22, Contracts 61 passed/6 documented skips
- [x] 6.2 Update docs riding the code commit: `ARCHITECTURE.md` (module census + `.Windows`/`.WinUI` sections), `CONSUMERS.md` (pinout + WindowsAppSDK lockstep entry), `ROADMAP.md` (rescope the parked `.WinUI` note — dialog ships here, AppDialog graduation stays parked for ISM; recently-shipped digest), `CHANGELOG.md`, `VERIFICATION.md` (WinUI satellite build note)
- [ ] 6.3 Commit (code + docs together), publish AL tag per `RELEASING.md` (AL-first rule)

## 7. Consumer window (sibling repos, after the AL tag)

- [x] 7.1 TSM: delete `Support\DiagnosticsWindow.cs`, reference `Astronomy.Diagnostics.WinUI`, re-wire Ctrl+N to `ShowOrFocus(owner, contextProvider, iconPath)`, raise TFM to `net10.0-windows10.0.26100.0`; build + run — TSM commit `406c284` (both call sites via one `ShowDiagnostics()` helper; Tests TFM raised too; 423/423, 0 warnings)
- [x] 7.2 TP: raise TFM to `net10.0-windows10.0.26100.0`; build + run — TP commit `63adfa0` (SkiaSharp floor rationale updated in csproj comment; 184/184, 0 warnings)
- [x] 7.3 Run `..\build-all.ps1` cross-repo DRC — GREEN: TP PASS, TSM PASS, contract tests 61 passed / 6 documented skips
- [ ] 7.4 Manual verification (feature-correct, not just code-correct): Ctrl+N flow in TSM and TP — open, capture, delayed capture, checkpoint OK, cancel/close-X terminator, singleton re-invoke — **user's pass; the ported WinUI shell's first on-screen render. AL tag publish waits on this (decided 2026-08-10: tag the verified commit)**
