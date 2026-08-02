# RELEASING.md — publishing AL to GitHub

> **Charter:** the rules for pushes to the public GitHub mirror. **The local repo is ground
> truth; GitHub is the public face** — a distribution channel, never the canonical location.
> Nothing here changes how development works; it only governs what the public sees and when.

## The mirror

`origin` = https://github.com/Apoplectic1/Astronomy-Library (public; created 2026-08-02).
No other remotes. The sibling mirror https://github.com/Apoplectic1/PCL carries the vendored
PixInsight tree (see Content rules).

## Branch policy

- **`dev` = working branch.** All work lands here. **`dev` never pushes.**
- **`main` = distribution-ready ref, and every push of `main` carries a tag** — `vX.Y.Z`
  (semver, `v`-prefixed; the portfolio convention). Publish = fast-forward `main` to the
  chosen `dev` commit, tag it, push both:
  ```bash
  git checkout main && git merge --ff-only dev
  git tag vX.Y.Z
  git push origin main vX.Y.Z
  git checkout dev
  ```
- Publish at natural completion points (a shipped unit of work, docs riding the same commit) —
  not on a schedule, and never mid-change. The working tree must be clean and the build/tests
  green at the published commit (see `VERIFICATION.md`). No tag → no push: the tag is what
  makes a `main` state a published state.
- **Docs-only exception (2026-08-02):** a `main` push may omit the tag when the delta contains
  only documentation/images — nothing that changes the built library — so the GitHub
  storefront (README) can update without minting a release. Any change to code or build
  inputs keeps the full no-tag-no-push rule.

## Distribution: tagged source snapshots — nothing else

A library has no installer: **publish *is* the push.** No release script, no GitHub Releases
page, no uploaded assets — a Releases page with nothing to download would only mislead.
Binary distribution happens downstream: the compiled `Astronomy.*` DLLs ship inside the
TargetPlanner and TargetSchedulerManager installers, built locally in those repos.

- **Versions come from the tag** via MinVer (`Directory.Build.props`:
  `<MinVerTagPrefix>v</MinVerTagPrefix>`, same as TSM/TP/XFM) — every managed assembly is
  stamped with the tag-derived version, so consumer payloads identify which AL they carry.
  Untagged commits shape as `-alpha` prereleases. The C++ `Astronomy.PCL.Native.dll` is not
  MinVer-stamped (native project); its provenance rides its managed wrapper.

- **Consumer coordination — AL releases first.** TP/TSM installers embed this library's
  *working tree* at their pack time, unpinned; the MinVer stamp on the embedded DLLs is the
  only linkage. Whenever AL has moved since its last published tag (or its tree is dirty),
  publish here **before** cutting a consumer release, so consumer payloads carry clean
  `X.Y.Z` stamps that exist on this mirror. Consumer `release.ps1` scripts enforce this with
  an abort gate (dirty Library tree, or `-alpha` in the embedded stamp).

Latest published tag: **`v1.1.0`** (first public tag; `v1.0.0` is pre-public history, pushed
for completeness).

## Content rules (what is deliberately public — and not)

- **`PCL/` never publishes.** The vendored PixInsight tree is untracked here by design; its
  canonical versioned home is https://github.com/Apoplectic1/PCL, cloned nested at
  `Library\PCL\`. This repo contains no Pleiades-authored source — the `Astronomy.PCL*`
  projects are original wrapper code.
- **PCL-binary obligation (standing):** any product that ships `Astronomy.PCL.Native.dll`
  (it statically links PCL) must reproduce the PCL copyright notice in its distribution
  materials and carry the acknowledgment: *"This product is based on software from the
  PixInsight project, developed by Pleiades Astrophoto and its contributors
  (https://pixinsight.com/)."* No current consumer release ships it; TP's planned pixel-data
  feature will be the first — its release checklist inherits this rule.
- **MIT-licensed** (`LICENSE`, © Dan Stark; adopted 2026-08-02, superseding the initial
  no-license posture). MIT covers this repo's code only — the PCL-binary obligation above is
  unaffected, and the vendored PCL material is governed by its own license.
- **`archive/` is versioned design history** — published deliberately; `archive/README.md`
  indexes it.
- **Shared-library neutrality:** public docs and XML doc comments describe the abstract
  contract — no consumer app terminology.
- **Never in the repo, so never published:** tokens/credentials (none exist).
- History publishes whole. Anything that must not be public must never be committed — there
  is no post-hoc scrub step.
