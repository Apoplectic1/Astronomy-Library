# diagnostics-platform-layering

## Purpose

Defines the layering contract for the diagnostics stack: a platform-neutral core that any consumer
on any OS can reference, platform-supplied capture backends injected at the observation seam, and
per-UI-framework dialog shells shipped as satellite assemblies at the lowest TFM their framework
permits.

## ADDED Requirements

### Requirement: Core diagnostics assembly is platform-neutral

The core diagnostics assembly SHALL target a platform-neutral TFM (`net10.0`) and SHALL NOT
reference any UI framework or platform-only package. Logging, log identity, and observation-session
orchestration SHALL be fully usable from this assembly alone.

#### Scenario: Non-Windows consumer references the core

- **WHEN** a project targeting a non-Windows TFM (e.g. `net10.0-android` or plain `net10.0`)
  references the core diagnostics assembly
- **THEN** the reference compiles, and logging plus observation-session orchestration are available
  without any Windows-only type entering the consumer's dependency closure

#### Scenario: Core cannot silently regain a platform coupling

- **WHEN** a platform-only API or package is introduced into the core assembly
- **THEN** the build fails (the platform-neutral TFM does not provide the API), rather than
  producing an assembly that throws at runtime on other platforms

### Requirement: Screen capture is a platform-supplied backend

Screen-pixel capture SHALL live outside the core, in a Windows-targeted assembly. Observation-session
orchestration SHALL take its capture behavior from the caller rather than binding to any platform's
capture implementation, so a future platform supplies its own backend without changing the core.

#### Scenario: Windows consumer uses the provided backend

- **WHEN** a Windows consumer starts an observation session wired to the Windows capture backend
- **THEN** captures grab the owner window's physical-pixel rectangle to PNG with the existing
  best-effort semantics (null path + failure status on any error, never an exception into the caller)

#### Scenario: Non-Windows consumer supplies its own capture

- **WHEN** a consumer on another platform starts an observation session with its own capture
  callback
- **THEN** the session's orchestration (id minting, START/CAP/END/CANCEL sequencing, capture
  counting, single-terminator guarantee, status wording) behaves identically to the Windows case

### Requirement: Dialog shells are per-framework satellites at their minimum TFM

Each UI framework's Ctrl+N observation dialog SHALL ship as a separate satellite assembly targeting
the lowest TFM that framework permits — WinForms at `net10.0-windows`, WinUI at
`net10.0-windows10.0.19041.0` — so no consumer is ever forced to raise its own TFM to reference a
shell, and non-consumers of a framework never inherit that framework's dependencies.

#### Scenario: WinUI consumer at the floor TFM references the WinUI shell

- **WHEN** a WinUI app targeting Windows SDK 10.0.19041 (or any later SDK) references the WinUI
  dialog satellite
- **THEN** the reference compiles and the dialog is usable without the app changing its TFM

#### Scenario: WinForms consumers stay free of WinUI dependencies

- **WHEN** a WinForms consumer references only the core, the Windows capture backend, and the
  WinForms shell
- **THEN** no WindowsAppSDK or WinUI dependency enters its build output

### Requirement: WinUI observation dialog offers the shared observation flow

The WinUI dialog satellite SHALL provide the portfolio's Ctrl+N observation dialog with the same
observable behavior as the WinForms shell: modeless always-on-top window centered over its owner,
singleton (re-invoking focuses the existing instance instead of logging a second START), immediate
and delayed capture with shared status wording, Enter-for-newline / Ctrl+Enter-commit note entry,
blank notes recorded as the all-okay checkpoint, and exactly one session terminator however the
window closes. App-specific presentation (e.g. the title-bar icon) SHALL be supplied by the caller,
not baked into the satellite.

#### Scenario: WinUI consumer opens the dialog

- **WHEN** a WinUI app invokes the satellite's dialog over its main window
- **THEN** an observation session starts (USER_OBS_START), the dialog supports capture / delayed
  capture / OK / Cancel with the shared status texts, and closing the window by any path logs
  exactly one terminator

#### Scenario: Re-invocation while open

- **WHEN** the app invokes the dialog again while an instance is open
- **THEN** the existing instance is focused and no second observation session starts

### Requirement: Session identity is correct in host-process (plugin) consumers

The log identity SHALL let a consumer supply its own build-version source, and session lines SHALL
stamp that version when supplied. Reading the process entry assembly SHALL remain the default for
consumers that own their process.

#### Scenario: Plugin guest in a host process

- **WHEN** a consumer running as a plugin inside a host application's process initializes logging
  with a consumer-supplied version source and starts a new session
- **THEN** the session's `build=` stamp reflects the plugin's version, not the host executable's

#### Scenario: Standalone app default

- **WHEN** a standalone app initializes logging without supplying a version source
- **THEN** the session's `build=` stamp reflects the app's entry-assembly version, unchanged from
  today's behavior
