# Proposal: diagnostics-winforms-satellite

## Why

The Ctrl+N observation dialog exists twice at the view layer: TP's WinForms `DiagnosticsDialog`
(lifted 2026-07-24 from TSM's WinUI model) and TSM's own. A second WinForms consumer arrives now —
XFM adopts diagnostics ahead of the astap-plate-solve debugging — which would make a third copy.
Decided 2026-08-06: extract the WinForms dialog into a per-framework satellite so both WinForms apps
share one implementation. The core `Astronomy.Diagnostics` stays UI-free (one project cannot serve
both WinForms and WinUI without polluting every consumer with the other framework's stack); the
WinUI equivalent is deliberately deferred (single consumer today; the real WinUI asset is TSM's
`AppDialog` behavior layer — parked as a ROADMAP note tied to ISM's arrival).

## What Changes

- New project **`Astronomy.Diagnostics.WinForms`**: WinForms-flavored shell(s) over the UI-free
  core. Initial content: `DiagnosticsDialog` moved verbatim from TP (visibility `internal` →
  `public`; namespace + doc-comment generalization per shared-library discipline — the dialog is
  already app-agnostic: `ObservationSession` + a `Func<string>` context provider are its only
  inputs). References `Astronomy.Diagnostics`; `<UseWindowsForms>`.
- Added to `Astronomy.sln`.
- ROADMAP: parked note for a future `Astronomy.Diagnostics.WinUI` (AppDialog-layer graduation,
  gated on ISM as the second WinUI consumer).
- No behavior change anywhere; TP's migration to consume this is its own change in the TP repo.

## Capabilities

### New Capabilities

_None — pure relocation of an existing, unchanged dialog into the shared library; `skip_specs: true`._

### Modified Capabilities

_None._

## Impact

- **Code**: new `Astronomy.Diagnostics.WinForms\DiagnosticsDialog.cs` (moved from TP); sln entry.
- **Dependencies**: satellite references core + WinForms framework only; no consumer changes in this release.
- **Consumers queued**: TP (`adopt-shared-diagnostics-dialog` — deletes its local copy), XFM
  (`adopt-diagnostics` — first-time adoption).
- **Docs**: CHANGELOG entry; ROADMAP WinUI-satellite parked note.
