# Tasks: publish-astronomy-library

## 1. Versioning (MinVer)

- [x] 1.1 Add MinVer to `Directory.Build.props`: `<MinVerTagPrefix>v</MinVerTagPrefix>`,
      `<MinVerDefaultPreReleaseIdentifiers>alpha.0</MinVerDefaultPreReleaseIdentifiers>`, and a
      `PackageReference Include="MinVer" Version="7.0.0"` (PrivateAssets=all) inside the
      existing `'$(MSBuildProjectExtension)' == '.csproj'` condition
- [x] 1.2 Build `Astronomy.sln` (Release|x64) + run tests; verify a managed DLL's
      `ProductVersion` reads `1.0.1-alpha.0.N+sha` (past `v1.0.0`, prerelease shape per spec)

## 2. Public docs

- [x] 2.1 Write `README.md`: what AL is, per-project map, consumers (TP/TSM installers carry
      the DLLs), two-tier build story (managed = clone + `dotnet build`; native = nested
      `github.com/Apoplectic1/PCL` clone at `Library\PCL` + v145 toolset, link PCL README),
      workshop-dir labels (openspec/, docs/, archive/), PCL acknowledgment note, no license
      grant implied
- [x] 2.2 Write `RELEASING.md`: shared charter/mirror/branch-policy text (incl. docs-only
      exception 2026-08-02), distribution = tagged source snapshots (publish = ff `main`,
      tag, `git push origin main vX.Y.Z`; bare tags, no Releases page, no assets), content
      rules: PCL/ untracked (canonical home = PCL repo), archive/ = versioned design notes,
      no consumer terminology, no LICENSE file, PCL-binary obligation (notice +
      acknowledgment when any product ships `Astronomy.PCL.Native.dll`)
- [x] 2.3 Refresh `.gitignore` `/PCL/` comment: canonical home is
      `github.com/Apoplectic1/PCL` cloned nested; drop stale "~10 GB"/zip-pin wording (zip
      stays untracked)
- [x] 2.4 Update reference docs riding the change: `CHANGELOG.md` entry, `ROADMAP.md`,
      `CLAUDE.md` router row for RELEASING.md/README.md

## 3. Publish

- [ ] 3.1 Pre-flight: clean `dev`, warning-free build, tests green
- [ ] 3.2 `git remote add origin https://github.com/Apoplectic1/Astronomy-Library.git`
- [ ] 3.3 ff `main` to `dev`, tag `v1.1.0` at the publish commit, push `main` + `v1.0.0` +
      `v1.1.0`; verify default branch = `main` on GitHub and README renders (incl. PCL repo
      link)
- [ ] 3.4 Verify a build at the `v1.1.0` commit stamps assemblies `1.1.0` exactly

## 4. Ripples (sibling repos, separate commits)

- [ ] 4.1 TSM `RELEASING.md`: correct "AL's source stays unpublished" content rule and the
      "sibling Library repo stays unpublished" local-build rationale (local-build stays; its
      reason is now simplicity, not secrecy); commit on TSM `dev`
- [ ] 4.2 TP `README.md` build-from-source: real clone URL for Astronomy-Library (and the
      nested PCL clone note for the future native path); commit on TP `dev`
