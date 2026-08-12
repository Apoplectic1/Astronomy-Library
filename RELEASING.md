# RELEASING.md — publishing AL to GitHub

> **Charter:** the rules for pushes to the public GitHub mirror. **The local repo is ground
> truth; GitHub is the public face** — a distribution channel, never the canonical location.
> Nothing here changes how development works; it only governs what the public sees and when.
> **Normative contract: `openspec/specs/github-distribution/`** — branch/tag policy, tag-derived
> versioning, PCL-never-publishes, the README promise, the PCL-binary attribution obligation, and
> license scope are single-sourced there; this file is the operational procedure.

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
  git tag -a vX.Y.Z -m "one-line release summary"
  git push origin main vX.Y.Z
  git checkout dev
  ```
  Tags are **annotated** (`-a -m`, portfolio convention 2026-08-06) so the summary shows in
  `git tag -n` and GUI clients; earlier tags are lightweight — cosmetic only, MinVer treats
  both alike.
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
TargetPlanner, TargetSchedulerManager, and XisfFileManager installers, built locally in those
repos (XFM since its v2.4.0 AL adoption).

- **Versions come from the tag** via MinVer (`Directory.Build.props`:
  `<MinVerTagPrefix>v</MinVerTagPrefix>`, same as TSM/TP/XFM) — every managed assembly is
  stamped with the tag-derived version, so consumer payloads identify which AL they carry.
  Untagged commits shape as `-alpha` prereleases. The C++ `Astronomy.PCL.Native.dll` is not
  MinVer-stamped (native project); its provenance rides its managed wrapper.

- **Consumer coordination — AL releases first.** TP/TSM/XFM installers embed this library's
  *working tree* at their pack time, unpinned; the MinVer stamp on the embedded DLLs is the
  only linkage. Whenever AL has moved since its last published tag (or its tree is dirty),
  publish here **before** cutting a consumer release, so consumer payloads carry clean
  `X.Y.Z` stamps that exist on this mirror. Consumer `release.ps1` scripts enforce this with
  an abort gate (dirty Library tree, or `-alpha` in the embedded stamp; XFM's gate armed at
  its v2.4.0 adoption).
  **Ripple edits are separate per-repo commits** (decided 2026-08-02, `publish-astronomy-library`
  § D5; re-enacted by `diagnostics-portable-core` § D6): consumer-repo fallout from an AL change
  (doc corrections, TFM raises, shell ports) lands as its own commits on each sibling repo's `dev`
  and publishes on that repo's schedule — never inside AL's commit; AL's commits stay single-repo.
  **When an AL change breaks a consumer's build, the first task after the break is a dated
  migration note committed into that consumer's repo** (what changed library-side, the new type
  shapes, the call sites and UI surfaces to touch, the operational steps, plus a one-line pointer
  from the consumer's own CLAUDE.md) — so a future session opening the consumer to a red build
  finds its map in-repo instead of reconstructing it from AL's history (decided 2026-07-24,
  `ks-dmag-moon-gate` § D6; one fully-executed instance so far).

Latest published tag: **`v1.10.0`** (2026-08-12: the **intent store** — openspec
`add-intent-store`, the TS-replacement program's AL critical path. New `Astronomy.Catalog`
`Intent/` area: baseline DDL as migration 0001, `IntentMigrations` (the portfolio's first
migration framework — the store is migrated, never rebuilt), `IntentStore` (local-only guard,
checkpoint-on-close sync-safety), and the one-time TS lift with pinned R13 translation maps,
per-row provenance, and the env-gated `TsImportDriver` operational test-host. Specs
`intent-store` / `intent-store-ts-import` promoted at archive. First consumer: ISM (its group-3
operational import unblocks on this publish). DRC GREEN at publish; derived-catalog surface
untouched). Prior:
`v1.9.0` (the 2026-08-11 batch: the three IS-motivated Core additions —
interval algebra `Time.UtcInterval` + `Intervals` with the session/moon/sun producers converged
onto it (**BREAKING**, consumers survived via member-name compatibility — pinned in
`CONSUMERS.md`), `Session.Meridian` + `MeridianSide`, and the injectable clock `IClock` +
`SystemClock` (portfolio-wide single clock source) — plus the scanner's fail-fast on
coordinate-less units (the (0,0) fallback removed), contract assumptions #26–#29 pinned +
#15 reconciled (bench 83), the ARCHITECTURE per-module split, and the XML-doc neutrality sweep.
DRC GREEN at publish);
`v1.8.0` (`DiagnosticsHotkey.Register` — shared app-level Ctrl+N
message filter for WinForms consumers, hoisted from TP: menu-mode + modal-dialog hotkey
coverage by construction, register-once. The same day's invoke-time-capture ship-and-revert is
recorded in CHANGELOG — the uniform cross-consumer contract stays capture-at-OK-time. First
consumers: TP + XFM swapped same day);
`v1.7.1` (docs-only: `CONSUMERS.md` records XFM as the third consumer; library code unchanged
from v1.7.0 — tagged so all three app payloads realign on one stamp);
`v1.7.0` (diagnostics platform layering, `diagnostics-portable-core` — TFM-neutral
`Astronomy.Diagnostics` core, `ScreenCapture` extracted to new `Astronomy.Diagnostics.Windows`,
`ObservationSession.Begin` takes the platform capture delegate, `AppLogIdentity.VersionAssembly`
for plugin-hosted consumers, new `Astronomy.Diagnostics.WinUI` shell);
`v1.6.1` (docs-only: v1.6.0 release bookkeeping; library code
unchanged from v1.6.0 — tagged so consumer payloads stamp clean while XFM v2.4.1 packs. This
tag rides *on* the bookkeeping commit, adopting fold-bookkeeping-into-the-released-commit so
post-release height-1 states stop forcing docs-only tags). Prior:
`v1.6.0` (`Astronomy.XISF.XisfBlockRewriter` — surgical re-store of a
monolithic XISF's primary block under a new codec, XML byte-preserved except the block
attributes; plus the checksum verifier + optional zstd `Compress` level that landed with the
same arc. First consumers: XFM v2.4.0's browse hygiene + solver temp-XISF input);
`v1.5.2` (docs-only: ROADMAP queues `WcsOrientation.FramingAngleDegrees`
for the second orientation consumer; library code unchanged from v1.5.1 — tagged so consumer
payloads stamp clean while XFM v2.3.0 packs); `v1.5.1` (legacy XISF checksum aliases
accepted on read — `sha1`/`sha256`/`sha512` canonicalized to the spec tokens; unblocked XFM's
solve path on 2019-era SGP files); `v1.5.0` (XISF Tier 3 — symmetric zlib/lz4/lz4hc/zstd ±shuffle block
codecs, all five spec checksums, `XisfImageReader` verified image read; `Astronomy.Core.Astrometry.WcsOrientation`
CD-matrix → position angle/parity; new `Astronomy.Diagnostics.WinForms` satellite carrying the
shared Ctrl+N `DiagnosticsDialog`. First consumers: XFM's ASTAP plate solving + codec adoption, TP's
dialog swap); `v1.4.0` (`project.name` editable schema with altitude-clause awareness;
89.9 clamp); `v1.3.0` (write-back credits by the capture-config
pairing rule — `Reconcile.CaptureConfigPairing`); `v1.2.0` (guarded row-insert primitive
`TryInsertRows`); `v1.1.0` (first public tag); `v1.0.0` is pre-public history, pushed for
completeness.

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
