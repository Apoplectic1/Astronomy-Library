# 2026-08-11 — docs-architecture MAINTAIN sweep (graduate & prune)

A journal→reference graduation sweep over `NOTEBOOK.md`, `docs/`, `CHANGELOG.md`, `archive/`, and
`openspec/changes/archive/`, resuming from the 2026-07-29 sweep's round 3. **90 raw flags → 84
canonical items adjudicated: 25 graduates applied, 26 graduates HELD for the ARCHITECTURE split,
21 prune-only applied, 12 keeps, 1 code bug flagged (report-only), 1 CHANGELOG entry backfilled.**

## Coverage note

| Round | Workers | Model | Raw flags | Novel after merge |
|---|---|---|---|---|
| 1 | notebook+docs · changelog-recent · changelog-early · archive-review-set · openspec-archive · open-lists-prune | 3× sonnet + 3× opus, high | 42 | 39 |
| 2 | residual-by-document · residual-from-code | opus/high | 18 | 18 |
| 3 | same pair | opus/high | 17 | 16 |
| 4 | same pair | opus/high | 13 | 11 |

**The sweep again did not reach a dry round** — round 4 still produced 11 genuinely novel items
(cross-round re-discovery was rare: only 2 of 48 later-round flags duplicated round 1). The two seams
the 2026-07-29 report named (pre-2026-05 CHANGELOG, the 2026-05-18 review archive set) were covered
this time. A future sweep should resume with residual rounds 5+; the pattern of both sweeps says the
journal still holds ungraduated rationale, mostly embedded mid-entry in older CHANGELOG eras.

## Held graduates — 26, all targeting `ARCHITECTURE.md` (M14)

`ARCHITECTURE.md` is 53.7 KB, past the split line booked at the 2026-07-29 sweep
(`ROADMAP.md` § *Open: split ARCHITECTURE.md*). Per the bloated-target rule these promotions are
**held, not applied** — run the split, then land each in its new home. Claim + target + disposition
for every hold (sources and full evidence: the canonical worker output, referenced per-item by its
CHANGELOG/archive citation):

1. **wcs-orientation-surface** → Core → `Astrometry/` · stub. `WcsOrientation.FromCdMatrix` (2026-08-06): CD matrix → PA (N-toward-E, [0,360)), image-axis rotation, parity from determinant sign, per-axis arcsec/px; NINA as strategy reference; domain of validity = normal + single-mirrored only; solver offsets stay with the calling wrapper. *(The DOMAIN provenance half was applied now — see below; the Astrometry/ folder-map line + "all ten folders" recheck waits for the split.)*
2. **efficiency-bar** → Core API conventions · cross-ref (`xisf-codecs` design § Goals). Allocation posture: size-and-allocate-once per stage, plain `byte[]` APIs, no speculative Span/pooling until a profile says otherwise.
3. **rename-vs-reshape** → Core API conventions · cross-ref (`ks-dmag-moon-gate` § D2). Meaning change ⇒ rename outright (the constellation-wide compile break is the point); reshape-in-place only when meaning is stable.
4. **rotation-nullable-vs-not** → conventions · stub. `NINA.Target.RotationDeg` non-nullable (absence inexpressible, 0° is a real angle) vs the Catalog plane's nullable rotation (null = expresses none); bridges must map absence explicitly. Goes live at Phase D's `InputTargetAdapter`.
5. **signed-dec-double-flip** → conventions (hemisphere bullet) · stub. A caller holding a signed declination passes the sign **alone**; also passing a pre-derived southern flag double-flips hemispheres (bit once, pinned by the early `ReportToTargetAdapter` test).
6. **three-layer-test-strategy** → Core.Tests bullet · cross-ref (archived review § F1/F2/F4). Meeus worked examples via reflection (keep ephemeris types `internal`), closed-form identities over magic numbers, `TestLocations.All()` five-site MemberData.
7. **tier3-module-shape** → XISF · stub. Full Tier-3 mechanics paragraph (codec layer, `XisfXmlLoader`, image reader pipeline, verifier verdict, rewriter). *(A compact tier-status correction + spec route-ins landed now via prune P7; the mechanics depth waits for the split.)*
8. **block-codec-producer-interop** → XISF · cross-ref (design §§ D1-4, 7). LZ4 = raw block format (declared size required); checksums over stored bytes; `+sh` iff itemSize > 1; NINA-mirroring levels; SHA3 unguarded fail-fast; sub-block forms rejected; managed-only deps.
9. **checksum-alias-canonicalized-at-parse** → XISF · stub. Legacy non-hyphenated tokens are valid input canonicalized at the single `Parse` site (not inside compute), keeping `ChecksumName` spec-form and fail-fast intact for unknown tokens.
10. **write-side-sha1-and-no-no-gain-fallback** → XISF · stub. Read honors all five algorithms; every written block stamps `sha-1` (a re-store downgrades a sha-256 file's declared algorithm); `Compress` never falls back to stored-uncompressed on no-gain (would re-attempt every save).
11. **rewriter-textual-edit-contract** → XISF · stub. Byte preservation via literal text substitution (attribute must appear verbatim exactly once, else throw), iterative layout convergence (≤10 passes) honoring `BlockAlignmentSize`, temp deleted + target untouched on any failure.
12. **zstd-level-encode-side-only** → XISF · stub. Level is encode-side only; non-zstd families throw; measured: shuffled 16-bit frames ~11% smaller at zstd-19 vs zlib-SmallestSize, appearing only past the level-15 strategy switch — archival re-stores pay, interop writes keep level 1.
13. **epoch-translate-never-cast** → Catalog → `Build/` harden-rule bullet · stub (NOTEBOOK + CHANGELOG 2026-08-01). TS persists NINA's Epoch order (JNOW=0/B1950=1/J2000=2) — reversed from the Catalog's own for 0/1 — so `SafeEpoch` is an explicit translation table; a bare cast silently swapped JNow↔B1950; unknown codes (incl. J2050) coerce to J2000 + `FlagIfSuspect`.
14. **altitude-clamp-and-project-name** → Catalog (TsEditableSchema) · stub. TS asserts `minimumaltitude < 90` ⇒ schema Max 89.9; `project.name` editable (Text) so altitude clauses can be rewritten; `StripAltitudeClause` short "- N" form with end-anchored dash. *(Its prerequisite CHANGELOG backfill was applied now — see below. Its "CONSUMERS field-list half" was a no-op: the pinout doesn't enumerate schema fields.)*
15. **tsfield-sentinel-metadata** → Catalog → `TargetScheduler/` · stub. Negative sentinels = "defer to enclosing scope", modeled as `TsField.Sentinel`+`SentinelLabel` naming *where* it defers (template default / camera default / project default).
16. **directory-is-identity-header-consensus** → Catalog → `Scan/` · stub. Directory tree is authoritative identity; header facts are consensus-reduced across frames; a silent frame is not in disagreement. *(Carries code bug CB1 — below.)*
17. **both-planes-express-pairing** → Catalog → `Reconcile/` · stub. Pairing keys only dimensions both planes can express; camera is disk-side label + `CameraDisagrees`, never a pairing key; aggregate identity deliberately wider than the pairing key.
18. **sentinel-asymmetry-and-templatesentinel** → Catalog (`CaptureConfigPairing`) · stub. Gain/offset `-1` sentinel pairs with nothing (⇒ `TemplateSentinel` cue); binning has no sentinel — non-positive reads as 1 and does pair; designed deferral states never raise the flag. *(When landing: the 2026-08-04 CHANGELOG line including readout-mode in `TemplateSentinel` is stale vs code (gain/offset only, by design) — correct in the reference doc, don't edit the dated entry.)*
19. **write-back-groups-every-plan** → Catalog → `TargetScheduler/` · stub. Write-back groups every existing plan (not just `Both`-resolved), so stale counts on unshot targets heal to 0; `IgnoredMissing` counts disk-only targets only.
20. **hotkey-and-ok-time-capture-contract** → Diagnostics (WinForms) · stub. `DiagnosticsHotkey.Register` app-level `IMessageFilter` (menu-mode + modal coverage; native modal loops unreachable by Win32 design; register-once throws); portfolio contract = capture-at-OK-time-only, an invoke-time variant shipped and reverted same day to preserve cross-consumer uniformity.
21. **lasterror-same-thread** → PCL interop · cross-ref (archived review § E3). `GetLastErrorMessage` is `thread_local` — query on the failing call's thread; other threads read empty by design.
22. **no-in-place-xisf-write** → PCL · cross-ref (`PCL-WrapperRoadmap` constraint 1). `pcl::XISFWriter` has no in-place edit — any native write is read → mutate in memory → fresh file → atomic replace.
23. **pinvoke-buffer-granularity** → PCL "Adding new surfaces" · cross-ref (`PCL-InterOp` § Performance). ~20-50 ns per crossing ⇒ every export is buffer-shaped; nothing callable per pixel/row-element.
24. **c-abi-no-ownership-transfer** → PCL "Adding new surfaces" · cross-ref (`PCL-InterOp` § Caveats). No native memory ownership transfer, no `Free` export ever; caller-supplied buffers, two-call size-then-fetch, status-code returns; only native-owned resource is the opaque handle.
25. **bench-scope-rule** → Contracts.Tests · cross-ref (`contracts-tests-refresh` § Context/Non-Goals). The bench exists for its label and failure message; overlap with unit tests fine; only assumptions traceable to a numbered CONSUMERS line belong.
26. **promoted-spec-route-ins** → XISF + Core → `Astrometry/` · cross-ref. Bold **normative spec** pointers for `xisf-block-compression` / `xisf-image-read` / `wcs-orientation`, matching the `Moon/` bullet's phrasing. *(The XISF half landed now inside P7's corrected tier sentence; the `wcs-orientation` route-in waits for the split with hold #1.)*

## Graduates applied — 25

**→ `DOMAIN.md` (4 + 1 half).** NINA-as-strategy-reference (re-derive + pin by published vectors,
never copy; MPL vs MIT made it load-bearing at publish; `AstroUtil` the named shape-mirror
exception) *including the `WcsOrientation` provenance/domain-of-validity half of hold #1* ·
no-library-side-caching (consumers memoize; `NightCache` the sanctioned exception) ·
satellite-scope-no-app-utilities (framework glue only; `UiTask.FireAndLog` inlined-not-exported;
same rule parking `AppDialog`) · reconcile-Combined star-colour rationale.

**→ `CONSUMERS.md` (9).** In-place API evolution in the charter (pinout describes, never promises) ·
XFM's pinned XISF surface (both rewriter shapes, all three verifier arms, rewrite-result geometry) ·
XFM's pinned Core surface (`FromCdMatrix` + read members) · demand-driven keyword subset + the
coexisting `KeywordList` (convergence = open coordination item) · consumer-TFM floor beside the
WindowsAppSDK lockstep · assumption admission rule (one compiler-invisible semantic per number) ·
bench-boundary-stops-at-capture-backend (with the corrected six-of-nine count) · the two XFM contract
facts registered in § *Contract facts not yet numbered* as spec pointers · DRC sln-unit echo in the
structural-validation bullet. *(Adjudication: the G33-vs-G39 placement disagreement on tolerant-parse
resolved to registration-not-numbering — a numbered assumption needs a bench pin, which is code work
MAINTAIN cannot do.)*

**→ `VERIFICATION.md` (5).** "Writing an honest benchmark" trap block (vary inputs / sub-ns =
artifact / suspect the accumulator) · managed ISA verification knobs (BDN `+FMA` header, `JitDisasm`,
`DOTNET_Enable*` downgrade knobs) merged with the managed-FMA3-hardware-floor statement (~10-50×
software fallback) · MinVer-needs-git-history trap (an `-alpha` stamp trips the consumer release
gates) · DRC's-unit-is-the-consumer-sln (XFM `tools/CompressionBench` = unchecked Library surface,
named member list).

**→ `RELEASING.md` (3).** `github-distribution` spec named normative in the charter · ripple edits
are separate per-repo commits · break-a-consumer ⇒ dated in-repo migration note first (honest caveat
carried: one executed instance so far).

**→ `ROADMAP.md` (4).** FilterKind-deletion counter-case under public-surface retention ·
night-grid precompute as SIMD direction 5 · genuine-producer fixture hardening under Tiers 2 & 4 ·
the full declined-findings roster (E2, the 60-term tables, `HorizonDipDeg`, the tuple item marked
premise-stale pending re-adjudication under evolve-in-place).

**Journal backfill.** `CHANGELOG.md` gained the missing **2026-08-06 v1.4.0 entry**
(editable `project.name`, 89.9 clamp, `StripAltitudeClause` short form — commits
`769837c`..`b648e3a`), making hold #14's future stub legal.

## Prunes applied — 21

CONSUMERS: XFM struck from "Not consumers" (live self-contradiction) · 3-node/seven-edge sentence →
4-node, edge count de-valued · the false "Catalog does NOT depend on Core" note corrected · "XISF
`Compression`" struck from dead surface · "all five managed assemblies" → six-of-nine with the
boundary stated. ARCHITECTURE: project counts 16/14 → seventeen (12 AnyCPU + 5 x64) · the XISF
tier-status sentence rewritten (Tier 3 shipped; section name fixed; dead-surface pointer that routed
to a false claim removed) · the XISF.Tests bullet's three stale claims (OffsetNormalized coverage,
pre-Tier-3 scope, literal "34 tests") replaced with current coverage areas, count de-valued ·
Catalog.Tests "236 tests." de-valued · SQLitePCLRaw 3.0.3/10.0.9 → 3.0.5/10.0.10 · `.WinUI`
"TSM port pending" → ported 2026-08-10. VERIFICATION: "all six shipped csprojs" → nine, satellites
named, three `dotnet build` recipe lines added. ROADMAP: the ~90-line publish-to-GitHub section
collapsed to a closure note (CI/NuGet recorded as declined) with **one re-opened standalone item —
personal data in the public mirror** (coordinates verified live on `origin/main` in four test files;
scope traps carried forward) · Tier-3 "consumers queued" corrected (XFM landed, TSM still queued) ·
residuals bullet 2 retitled to its standing do-not-"fix" half · the Ctrl+N clause left the
UI-terminology item (hotkey became Library surface; ~8 sites remain) · digest's "TSM/TP window
pending" → landed. CHANGELOG: the Phase-A forward-roadmap block → pointer (matching the sibling
entry's convention); two "Tiers 2-4" pointers renamed to the current section title. RELEASING: both
two-consumer sentences → TP/TSM/XFM (XFM gate noted). DOMAIN: the dangling publish-caveat
parenthetical → live condition + RELEASING route. CLAUDE.md router: "Windows-only" → predominantly
Windows with the TFM-neutral Diagnostics core; "XISF read" → read + block re-store. README: three
storefront claims refreshed (three consumer apps incl. the verified-public XFM mirror link; XISF row;
layered Diagnostics row) — **not yet pushed**; a docs-only `main` push updates the storefront without
a tag when desired.

**Supersession note (in lieu of editing the dated 2026-07-29 report):** that report's code-bug table
rows for `Log.cs:22` and `ScreenCapture.cs:10` are superseded — Ctrl+N became documented Library
surface (v1.5.0/v1.7.0/v1.8.0), and `ScreenCapture` moved to `Astronomy.Diagnostics.Windows/`. The
dated report stays untouched; this note is the record.

## Code bug — report-only (MAINTAIN never edits code)

**CB1 — silent (0,0) coordinate fallback violates the scanner's required-keyword contract.**
`CHANGELOG.md` § 2026-05-18 Phase A lists RA/DEC among the **"Required-for-aggregation keywords"**,
and `ARCHITECTURE.md` § Catalog → `Scan/` names the non-fatal `SkippedFiles` path as "the *one*
deliberate exception to the portfolio's fail-fast-on-contract-violation default" — but a target whose
frames carry **no RA/DEC at all** is silently placed at RA 0h / Dec 0°, a real sky position, instead
of aborting: `Astronomy.Catalog/Scan/ImageLibraryScanner.cs:468-471` ("Fallback (0, 0) if no frames
carried coords — caller can sanity-check downstream") via `Median` returning `0.0` on an empty list
at `:480-483`. No caller sanity-checks it; the value flows into `TargetResolver` coordinate matching
and the reconcile join. Tracked in `ROADMAP.md` § *Open: silent (0,0) coordinate fallback*.

Two adjacent drifts were adjudicated **not** code bugs: `DiagnosticsDialog.cs:8-11`'s "three
delegates" remark (four since 2026-08-10 — plain drift, no guarantee language; ARCHITECTURE is the
correct side; fix the numeral when the file is next touched, e.g. the XML-neutrality sweep) and the
2026-08-04 CHANGELOG `TemplateSentinel` readout-mode mention (code is gain/offset-only by design;
correction rides hold #18 into the reference doc).

## Keeps of note

netstandard2.0/`DllImportAttribute` defect stays journal-only (no project targets it, none will) ·
the hemisphere-extension + `360.985647` non-actions stay ROADMAP-carried · `Nullable disable`
rationale stays at its point of use · the 2026-08-02 history-audit counts stay dated ·
UI-terminology item re-verified genuinely open (~8 sites, none fixed) · Diagnostics-satellites item
premise re-verified · all remaining `## Open:` sections individually re-verified still open ·
`archive/` followups' F5.7 nesting quirk left as-is (re-nest without altering content if ever
touched) · **the permitted-static-state audit recipe was re-run this sweep (its third run) and
passes exactly as written** — record the run count if the split touches that section ·
`PCL-WrapperRoadmap` constraint 2 (`pcl::Variant` marshaling fork) stays an open design question ·
`docs/2026-05-12-fma-benchmark-findings.md` stays put per its extraction decision (only its
hardware-floor half was procedural, applied above).

## Accounting (required slot)

**Prune / archive candidates found this sweep — 21 prune-only, all applied; 0 archive candidates**
(no journal record proposed for `archive/`; every archived record cited by a graduate stays
untouched per the immutable-record rule).

**Net reference-tier delta:** +25 applied statements across five reference docs (CONSUMERS +9,
VERIFICATION +5, DOMAIN +4½, RELEASING +3, ROADMAP +4) and the router/README corrections, against
−21 struck or corrected claims — among them two flatly false statements in the datasheet (the
XFM "not a consumer" bullet, the Catalog/Core dependency note), four stale derived counts de-valued
rather than re-cached, and one ~90-line superseded ROADMAP section collapsed with its single live
residual re-opened as a standalone exposure item. `ARCHITECTURE.md` took **zero promotions** (26
held) yet still shrank in falsehood: seven stale claims corrected in place. The journal grew by one
backfilled CHANGELOG entry (v1.4.0) and this report.
