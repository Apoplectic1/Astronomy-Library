# Astronomy Library — Roadmap

## Open: publish to GitHub

Captured 2026-05-08. The library currently lives only on disk; sibling
TargetPlanner consumes it via local `ProjectReference`. At some point the user
wants the astronomy code in the open. Not gating any active work — recorded
here so it doesn't drift out of memory.

### Three scope options

- **Option A — Core only, public** *(recommended)*. Spin out `Astronomy.Core`
  + `Astronomy.Core.Tests` (the xUnit half) into its own public repo. Leave
  `Astronomy.PCL` / `Astronomy.PCL.Native` in the existing private layout (or
  a separate private sibling). Smallest scope, no PCL-license entanglement,
  gets the pure-Meeus astronomy code into the open. Estimated 1–2 sessions.
- **Option B — whole Library, public**. One public repo with all four
  projects. PCL adds friction: third-party SDK dependency, build docs,
  license-compatibility check (PCL Open License vs. whichever license is
  picked in step 1). Estimated 2–4 sessions.
- **Option C — public mirror, dev stays private**. Keep working in the
  current `E:\…\Astronomy\Library\` and publish a periodic snapshot to a
  public repo (e.g. `git push public main`). Lowest one-time cost; ongoing
  maintenance burden of remembering to push.

### Prep checklist (applies to A or B)

1. **License.** Pick one and add a `LICENSE` file at the repo root. Typical
   for libraries: MIT, Apache 2.0, BSD-3. For Option B, verify chosen
   license is compatible with PCL Open License (see `PCL/COPYING.md`).
2. **Personal-data scrub.** Same kind of pass as TargetPlanner's
   2026-05-08 scrub:
   - `Astronomy.Core.Tests/Tests/Astrometry/ParityFixtures.cs` has inline
     Penns Park lat/lon (`40.282835`, `74.997369`) in named DST regression
     cases (`PennsParkSpring`, `PennsParkDstFall`, `PennsParkDstSpring`,
     `PennsParkSummerSolstice`). Parameterize them: rename to neutral
     names (e.g. `MidLatNorthSpring`) or move the personal coordinates
     into the test's `TestLocations.PennsPark` fixture (which already
     exists for the rest of the suite as of 2026-05-08).
   - ~14 test comments mention "Penns Park" / "M31 at Penns Park" — keep
     them or rephrase as "the 40°N test fixture"; either is defensible.
   - Audit `CLAUDE.md` for personal paths, machine names, or Windows-user
     specifics that won't make sense to a public reader.
3. **README.** New `README.md` at repo root: one-paragraph "what this is"
   (pure-managed Meeus + closed-form session placement + K-S sky brightness
   + optional XISF read via PCL P/Invoke), build/test instructions, link
   to existing CLAUDE.md as the deeper reference. ~80 lines.
4. **Build prerequisites.** Document MSBuild + VS2022+ for the C++/C#
   mixed solution; `dotnet build` for `Astronomy.Core` alone. For Option B,
   document where to drop the PCL SDK
   (`Library\PCL\` snapshot from `PCL-master.zip`, pinned 2025-02-22) so
   `Astronomy.PCL.Native.vcxproj` can find its static libs.
5. **Git history.** `git log -p` against the lib's history for personal
   paths in commit diffs. The library was extracted from TargetPlanner in
   `b28ef9e` (2026-04-23), so the surface area to audit is small.
   `git filter-repo` if anything sensitive turns up.
6. **CI** *(optional, defer for v1)*. GitHub Actions workflow that runs
   `dotnet test Astronomy.Core.Tests` on push. Skippable if Option A
   ships without `Astronomy.PCL` / `Astronomy.PCL.Native` (no native
   build needed → trivial CI).
7. **NuGet** *(optional, defer for v1)*. `Astronomy.Core` could become a
   published NuGet for downstream consumption. Adds versioning discipline;
   skippable for an initial public-source release.

### TargetPlanner downstream impact

The user's workflow treats local disk as source of truth and GitHub as a
distribution mirror, so publishing the Library doesn't change anything
about *the user's* dev experience — TP keeps consuming the local sibling
checkout exactly as it does today. The question is what *public TP
consumers* would do, since they don't have the user's local layout. Two
paths:

- Keep the `ProjectReference` and document "clone the Library repo next
  to TargetPlanner" in `TargetPlanner/CLAUDE.md` (already partially
  there). Public TP consumers clone two repos.
- Switch TP to a `PackageReference` against a published NuGet (requires
  step 7 above). Public TP consumers clone one repo; NuGet handles the
  rest. Cleaner long-term; no work needed if Option A skips NuGet for v1.
