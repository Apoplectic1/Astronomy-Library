# Design: publish-astronomy-library

## Context

See `proposal.md — Why`. Current state: AL has `dev`/`main` branches, no remote, one local tag
`v1.0.0`; `github.com/Apoplectic1/PCL` (the nested `Library\PCL` working mirror with the v145
patches) published earlier today and its README already documents the nested-clone layout AL's
README will link. Sibling precedent: XFM/TP RELEASING.md carry the exact shared-rules text to
reuse; TP/TSM adopted MinVer 7.0.0 with `MinVerTagPrefix=v` + `alpha.0`. All decisions below
were settled in the 2026-08-02 explore session.

## Goals / Non-Goals

**Goals:** publishable-today mirror with honest docs; tag-derived assembly provenance;
standing PCL obligation recorded where future releases will find it.

**Non-Goals:** NuGet packaging, GitHub Releases/assets, CI, a release script, any library
behavior change, publishing the PCL tree (has its own repo), a LICENSE file.

## Decisions

1. **MinVer via `Directory.Build.props`, not per-csproj.** One `<MinVerTagPrefix>` +
   `<MinVerDefaultPreReleaseIdentifiers>` + one `PackageReference` conditioned to
   `.csproj` projects covers all shipped projects and future ones; per-csproj (TSM/TP style)
   would be 7+ copies. The props file already gates on `'$(MSBuildProjectExtension)' ==
   '.csproj'`, so the C++ `.vcxproj` is naturally excluded (MinVer is a NuGet/MSBuild-for-
   managed mechanism; the native DLL's version provenance rides the managed wrapper).
   Alternative considered: per-project references — rejected as pure duplication.
2. **Fresh tag `v1.1.0` at the publish commit; `v1.0.0` pushes as history.** The publish
   commit contains README/RELEASING/MinVer, so the first MinVer-stamped release is the one
   the public sees; v1.0.0 remains honest pre-public history. Alternative (retag v1.0.0):
   rejected — moving tags falsifies history.
3. **Bare tags, no Releases page.** Release objects with no assets invite "where's the
   download?"; the README points binary-seekers at TP/TSM installers instead.
4. **RELEASING.md distribution section = "publish is the push".** No script — a script that
   wraps `git push origin main vX.Y.Z` is ceremony. Content rules absorb the AL-specific
   truths (PCL untracked, archive/ = versioned design notes, PCL-binary obligation,
   no-consumer-terminology, no LICENSE).
5. **Ripple edits are separate per-repo commits** (TSM RELEASING, TP README) — each repo's
   docs ride its own `dev`, published by that repo's next push, keeping single-repo commit
   hygiene.

## Risks / Trade-offs

- [MinVer changes assembly versions consumers embed] → No consumer reads AL versions
  programmatically; rule-15 clean-rebuild applies. TP/TSM pick the new stamps up at their
  next releases naturally.
- [MinVer needs git history at build time] → All builds happen in the repo (local-only
  workflow); a zip-download build would get MinVer's fallback version — acceptable, README
  says clone.
- [History publishes whole — 288 files, 1.36 MiB pack] → Reviewed in explore: no secrets, no
  third-party source ever committed; site data follows TP precedent.
- [`v1.1.0` while consumers reference no version] → No coordination needed; tags are
  provenance only.

## Migration Plan

Single sequence, no rollback needed (a bad push can be corrected by force-pushing `main`
before anyone consumes it — solo workflow): docs + MinVer on `dev` → verify build/tests →
ff `main`, tag, wire origin, push `main` + both tags → ripple commits in TSM/TP.

## Open Questions

None.
