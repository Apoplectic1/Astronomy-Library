# Proposal: publish-astronomy-library

## Why

AL is the portfolio's shared library, but it is the only active repo with no GitHub mirror —
its compiled DLLs already ship publicly inside TP and TSM installers while the source has no
public home, and statements across the portfolio ("AL stays unpublished") encode that gap as
if it were policy. The user created `github.com/Apoplectic1/Astronomy-Library` (2026-08-02) to
close it, and the sibling `github.com/Apoplectic1/PCL` mirror (published the same day) now
carries the native build dependency, so a fresh-machine reproduction story for every DLL is
finally possible — if AL publishes with the right docs.

## What Changes

- **Publish AL to `github.com/Apoplectic1/Astronomy-Library`**: wire `origin`, push `main`
  (ff from `dev`) plus the existing `v1.0.0` tag, and cut a fresh `v1.1.0` at the publish
  commit. Bare tags only — no GitHub Releases page, no assets (TP/TSM installers are the
  binary distribution channel).
- **New `README.md`** (public storefront): what the library is, the two-tier build story
  (managed tier self-sufficient; native tier requires the PCL mirror cloned at `Library\PCL`),
  consumers, workshop-dir labels, PCL acknowledgment note.
- **New `RELEASING.md`** on the shared TSM/TP/XFM conventions (charter, mirror, branch
  policy incl. the 2026-08-02 docs-only exception, content rules), with the distribution
  section reduced to tagged source snapshots — publish = `git push origin main vX.Y.Z`.
- **MinVer adoption** (7.0.0, `MinVerTagPrefix=v`, `alpha.0` prerelease ids) via
  `Directory.Build.props` so every shipped `Astronomy.*` DLL carries tag-derived version
  provenance inside consumer payloads.
- **`.gitignore` comment refresh**: `/PCL/` note points at the PCL mirror repo as canonical
  home (stale "~10 GB"/zip-pin wording corrected; the zip stays untracked).
- **Ripple edits in sibling repos**: TSM `RELEASING.md` "AL stays unpublished" statements
  corrected; TP `README.md` build-from-source section gets the real clone URL.
- Reference docs (`CHANGELOG.md`, `ROADMAP.md`, `CLAUDE.md` router) updated in the same
  change per portfolio convention.

## Capabilities

### New Capabilities

- `github-distribution`: what the public mirror is and what may/must never travel to it —
  branch policy (dev never pushes; every `main` push carries a tag, docs-only excepted),
  tag-derived assembly versioning (MinVer), content rules (PCL tree untracked by design, no
  LICENSE file, no consumer terminology), and the standing PCL-binary obligation (any product
  shipping `Astronomy.PCL.Native.dll` must carry the PCL notice + acknowledgment).

### Modified Capabilities

<!-- none — existing specs (contract-assumption-pinning, moon-brightness-gate) are untouched -->

## Impact

- **AL repo**: new `README.md`, new `RELEASING.md`, `Directory.Build.props` (MinVer),
  `.gitignore` comment, `CHANGELOG.md`/`ROADMAP.md`/`CLAUDE.md` updates, new `origin` remote,
  pushed `main` + `v1.0.0` + new `v1.1.0`.
- **Assembly versions change**: MinVer replaces default `1.0.0.0`-style versions with
  tag-derived ones on every shipped project — visible in TP/TSM payloads after their next
  releases. No consumer code change required (no back-compat concerns; portfolio rule 15).
- **Sibling repos** (ripple, docs-only): TSM `RELEASING.md`, TP `README.md` — each committed
  in its own repo on `dev`.
- **No behavior change** in any library API; no test impact.
