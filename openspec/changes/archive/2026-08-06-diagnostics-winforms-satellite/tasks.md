# Tasks: diagnostics-winforms-satellite

_Design skipped (conditional): pure relocation, decisions in the proposal._

## 1. Satellite project

- [x] 1.1 `Astronomy.Diagnostics.WinForms.csproj` (AL conventions: warning ratchet, AnyCPU;x64, docs file; `<UseWindowsForms>`; ProjectReference to core) + add to `Astronomy.sln`
- [x] 1.2 Move TP's `DiagnosticsDialog.cs` in: namespace `Astronomy.Diagnostics.WinForms`, `public sealed`, doc comments generalized (no app names in contract wording; capture history note retained)

## 2. Docs + verify (same commit)

- [x] 2.1 CHANGELOG entry; ROADMAP parked note for `Astronomy.Diagnostics.WinUI` (AppDialog-layer graduation, gated on ISM)
- [x] 2.2 Build clean (satellite + full sln)
