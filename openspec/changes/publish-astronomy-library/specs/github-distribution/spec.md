# Delta spec: github-distribution

## Purpose

Defines the contract for AL's public GitHub mirror (`github.com/Apoplectic1/Astronomy-Library`):
what publishes and what must never travel, how versions derive from tags, and the standing
license obligation that follows the native PCL wrapper into any shipped product.

## ADDED Requirements

### Requirement: Local repo is ground truth; the mirror distributes

The local repo SHALL be the canonical source of truth. `origin`
(`github.com/Apoplectic1/Astronomy-Library`) SHALL be a distribution channel only. Work SHALL
land on `dev`, which never pushes; every push of `main` SHALL carry a `vX.Y.Z` tag, with one
exception: a `main` push MAY omit the tag when the delta contains only documentation/images.
`main` SHALL move only by fast-forward from `dev`.

#### Scenario: Publishing a release
- **WHEN** a unit of work is complete on `dev` with a clean tree and green build/tests
- **THEN** publishing consists of fast-forwarding `main`, tagging `vX.Y.Z`, and pushing `main`
  plus the tag — nothing else (no assets, no GitHub Release objects, no release script)

#### Scenario: Docs-only update
- **WHEN** the delta between `main` and the chosen `dev` commit touches only documentation or
  images
- **THEN** `main` MAY be pushed without a tag so the public storefront updates without minting
  a release

### Requirement: Assembly versions derive from git tags

Every shipped `Astronomy.*` assembly SHALL carry a version derived from the latest reachable
`v`-prefixed git tag, so binaries embedded in consumer payloads (TP/TSM installers) identify
the AL state they were built from. Builds at untagged commits SHALL shape as prerelease
versions distinguishable from tagged releases.

#### Scenario: Building at a tagged commit
- **WHEN** the library is built at a commit tagged `vX.Y.Z`
- **THEN** the assemblies' informational version SHALL be `X.Y.Z`

#### Scenario: Building past a tag
- **WHEN** the library is built at a commit after the latest tag
- **THEN** the assemblies' informational version SHALL be a prerelease form (e.g.
  `X.Y.Z+1-alpha.…`) that cannot be mistaken for a tagged release

### Requirement: Vendored PCL tree never publishes

The `PCL\` directory (PixInsight source, headers, built libs, snapshot zip) SHALL remain
untracked in AL — its canonical versioned home is the separate `github.com/Apoplectic1/PCL`
mirror, cloned nested at `Library\PCL\`. AL SHALL contain no Pleiades-authored source.

#### Scenario: Publishing the repo
- **WHEN** AL pushes to the public mirror
- **THEN** no file under `PCL\` travels, and the repo's own `Astronomy.PCL*` projects contain
  only original wrapper code

### Requirement: Public docs state the two-tier build reality

The public `README.md` SHALL distinguish the managed tier (buildable from a fresh clone with
the .NET SDK alone) from the native tier (`Astronomy.PCL` / `Astronomy.PCL.Native`, which
additionally requires the PCL mirror cloned at `Library\PCL` and the pinned C++ toolset), so
a fresh clone that cannot build the native wrapper is documented behavior, not a broken repo.

#### Scenario: Fresh clone, managed build
- **WHEN** a visitor clones only Astronomy-Library and builds the managed projects
- **THEN** the build SHALL succeed with the .NET SDK and NuGet restore alone, matching what
  the README promises

#### Scenario: Fresh clone, native build attempted without PCL
- **WHEN** a visitor attempts to build `Astronomy.PCL.Native` without the nested PCL clone
- **THEN** the failure is documented in the README along with the recovery instruction (clone
  `github.com/Apoplectic1/PCL` into `Library\PCL`)

### Requirement: Shipping the native wrapper obligates PCL attribution

Any product that distributes `Astronomy.PCL.Native.dll` (which statically links PCL code)
SHALL reproduce the PCL copyright notice in its distribution materials and SHALL carry the
end-user acknowledgment required by the PixInsight Class Library License ("This product is
based on software from the PixInsight project, developed by Pleiades Astrophoto and its
contributors (https://pixinsight.com/)."). AL's release documentation SHALL record this
standing obligation so future consumer releases inherit it.

#### Scenario: Future consumer ships the native DLL
- **WHEN** a consumer app (e.g. TP's planned pixel-data feature) first includes
  `Astronomy.PCL.Native.dll` in its installer
- **THEN** that app's release checklist SHALL include the PCL notice + acknowledgment, per
  the obligation recorded in AL's `RELEASING.md`

#### Scenario: Current releases
- **WHEN** TP/TSM ship only managed `Astronomy.*` DLLs (no PCL wrapper)
- **THEN** no PCL attribution is required

### Requirement: License posture is all-rights-reserved

The repo SHALL publish without a LICENSE file (all rights reserved by default,
portfolio-consistent): source is visible for reference, with no reuse grant implied. The
README SHALL NOT imply an open-source license.

#### Scenario: Visitor checks reuse terms
- **WHEN** a visitor looks for a license
- **THEN** none is present and no doc implies a reuse grant
