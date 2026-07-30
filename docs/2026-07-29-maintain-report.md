# 2026-07-29 — docs-architecture MAINTAIN sweep (graduate & prune)

A journal→reference graduation sweep over `NOTEBOOK.md`, `docs/`, `CHANGELOG.md`, `archive/`, and the
archived openspec change records. **34 items adjudicated: 28 graduates applied, 4 prune-only applied,
1 code-bug flagged (report-only), 1 stray working-tree edit folded in.**

## Coverage note

| Round | Workers | Model | New items |
|---|---|---|---|
| 1 | journal · CHANGELOG · openspec-archive · archive+open-lists | sonnet | 9 |
| 2 | journal · archive+router | opus | 17 |
| 3 | residual-by-document · residual-from-code | opus | 14 |

No worker died; no span went uncovered. **The sweep did not reach a dry round** — round 3 was still
productive (14 new items, including the only code-bug of the run). Rounds 1 and 2 disagreed heavily,
which is the expected model-diversity effect rather than a defect: the sonnet round found the recent-work
gaps, the opus rounds found the older embedded rationale. A future sweep should resume from round 4
rather than treat this file as exhaustive; the richest remaining seams are the pre-2026-05 CHANGELOG eras
and the `archive/2026-05-18-library-review*` set.

All eight `ROADMAP.md` `## Open:` items were verified **genuinely still open** against code and git
history — no prune candidates there.

## Graduates applied

**→ `ARCHITECTURE.md` (14).** The `LogProcessGlobal` bench-collection rule + correcting ":68 only `Log`
toucher" to "only `Init`-caller" · non-sidereal + `Calibration/` scan exclusion at `ScanTargetAsync` ·
scanner parallelism and non-fatal `SkippedFiles` (the deliberate fail-fast exception) ·
`FramingCluster.ServesPlanRotation` credit gate · footprint pricing + `OnFootprintFraction` ·
`FieldFootprint` in the Core map · `<Image geometry>` / XPIXSZ-no-binning-multiplier ·
`static readonly` canonical singletons + reference identity · CS1591-as-error split (4 enforcing, 2
documented exemptions) · thread-safety audit recipe + rewording the now-false "zero singletons" ·
the NaN half of the delegate contract · resolver coordinate scope key · the post-anchor outranking pass ·
write-back's four-part join key · `TsEditableSchema` as SQL whitelist + `PRAGMA` drift guard +
read-back verify · `SQLitePCLRaw` CVE pin · PCL re-snapshot ABI-churn caveat · explicit-pixel-I/O
convention · route to the promoted `moon-brightness-gate` spec.

**→ `DOMAIN.md` (3).** Moon-gate tolerance calibration (why 1.0 NB / 0.30 BB; the Lorentzian was never an
iso-quality contour) · filter-preset provenance (Astrodon/Chroma values, SII 672.4 doublet centering,
single-letter naming end-to-end) · `AstroUtil` as the NINA mirror surface (don't split; the
`GetMoonPhaseName` bucketing divergence). *The file previously named no science domain at all.*

**→ `CONSUMERS.md` (6).** The pin-don't-patch adjudication rule · `RotationExpression` + `Fold180` added to
the Catalog pinout (TSM folds the plan plane with the library's own fold) · `ObstructionTableHorizonProfile`
and `SingleTargetPlanner` added to the dead-surface inventory · removed `LocationExtensions` /
`TargetExtensions` from it (both are `internal`, so they inflated the "large fraction of the *public* API
is uncalled" premise the ROADMAP retention decision rests on) · corrected
`Log.ScreenshotsFolderPath` from "no caller at all" to *no external caller* · the two unnumbered
contract facts (cancellation-throws; write-back join key).

**→ `VERIFICATION.md` (4).** OS-version-less TFM trap · Debug is structurally blind to native ISA
regressions (verify the AVX2 floor at Release with `dumpbin`) · assert-with-tolerance because Core is
FMA-lowered · a new § *Parity baseline* carrying the re-baseline procedure and the do-not-widen rule.

**→ `ROADMAP.md` (3).** The gate put the ~1,425 ns moon path on the per-minute placement loop, raising
SIMD direction 2's payoff · PCL's license is permissive BSD-style (mostly closes publish-prep step 1) ·
the C2/D1 deliberately-declined findings (and that C4 is obsolete).

## Source dispositions

Every graduate carried one. `stub` where the dated derivation stays as evidence (the NOTEBOOK flake
entry, the 2026-05-27 sweeps, the framing commits' new CHANGELOG entries). `cross-ref` for everything
lifted out of `archive/` and — per the immutable-record rule — for **all** graduates sourced from
`openspec/changes/archive/`, written as pointers in the reference doc, with nothing under that tree
edited. No dated entry lost its why/when; nothing was deleted.

## Code bug — report-only (MAINTAIN never edits code)

**Consumer UI terminology has leaked into public XML docs**, violating the rule stated at `DOMAIN.md`
§ *Multi-consumer strategy* ("no consumer **UI terminology** … in the public surface or its XML docs",
decided 2026-07-24). The 2026-07-24 audit recorded this axis as report-only and the sweep never ran.
Sites, all in `///` docs on public surface:

| File:line | Leaked wording |
|---|---|
| `Astronomy.Core/Night/NightCache.cs:16` | "the multi-target Graph path" |
| `Astronomy.Core/Night/NightCache.cs:21` | "build it once per Graph click" |
| `Astronomy.Core/Night/NightCache.cs:22` | "hand it to every target's `AltitudeSeries`" (TP type; dangling `<c>` ref) |
| `Astronomy.Core/Night/NightCache.cs:80-81` | "`LocationsCacheEquivalent` in TP" (TP member name) |
| `Astronomy.Core/Night/NightCache.cs:85` | "Year / Sessions chart x-axis labels" (TP chart names) |
| `Astronomy.Core/Astrometry/ObserverInfo.cs:22` | "the chart-cache prepare loop" |
| `Astronomy.Core/Session/BestSession.cs:269` | "matches the chart's \"Symmetric\" semantics" |
| `Astronomy.Diagnostics/Log.cs:22` | "while a Ctrl+N window was open" (consumer keybinding) |
| `Astronomy.Diagnostics/ScreenCapture.cs:10` | "the Ctrl+N observation flow" |
| `Astronomy.Catalog/Scan/FramingCluster.cs:48` | "the framing badge" (weakest of the set) |

Same leak class the user caught previously ("Optimal-chart series" in `CoarseVisibility.cs`). Cleared as
**not** violations: `BestSession.cs:60` ("consumers (chart UIs, schedulers)") and `AltitudeCurve.cs:24`
("below chart pixel resolution") — both generic. Suggested neutral wording: "a caller's multi-target
planning pass", "the caller's cache-prepare loop", "the caller's symmetric-session mode", "an observation
window". Tracked in `ROADMAP.md`.

## Verified-and-cleared (so a later round need not re-walk)

`contract-assumption-pinning` holds — all 25 assumptions are cited by a bench test or registered.
`MoonLimitProfile` matches its promoted spec exactly. `Location.Default`'s old `DateTime.Now`
nondeterminism is gone (Core has no ambient-clock read outside `ObservationMoment.Now`). PCL's two
deliberately-bare exports, no `SELECT *` in the TS reader, no journal-mode mutation in
`TargetSchedulerWriter`, the `[0, 360)` azimuth fold, `TargetSource` ordinals vs `GetShotTargets`'
`IN (0, 2)`, the 0.5°/0.1° resolver defaults, and `global.json` at `10.0.203` all check out. Every
journal claim with contract force that was tested against code was honoured — the drift was
consistently doc-side, which is why this run produced one code-bug and not a list.

## Accounting (required slot)

**Prune / archive candidates found this sweep — 4, all applied, 0 archived:**

1. `docs/README.md` — "_No entries yet_" was false (the FMA findings file exists). Struck.
2. `CLAUDE.md` § gotcha 2 — the fourteen-projects/eight-PCL census duplicated `ARCHITECTURE.md:7`
   near-verbatim, and the re-extract clause isn't an x64 fact at all. Trimmed to the actionable rule
   plus two routes; the router's other two gotchas were at the right altitude and were left alone.
3. `CHANGELOG.md` — a pointer at the pre-archive `openspec/changes/ks-dmag-moon-gate/`. Repointed.
4. `CONSUMERS.md` — two `internal` types listed as public dead surface. Removed.

Two derived counts were **de-valued rather than re-cached** (a rationale-free number re-caches drift
instead of curing it): the XISF accessor usage ("17 of 39" → "a subset") and "the 21 `XisfHeader`
members". No archive candidates: all seven `archive/` records are still cited from the live reference
set, and every `ROADMAP.md` open item is genuinely open.

**Target-bloat outcome.** `ARCHITECTURE.md` passed the promote-into-it content test at the start of this
sweep (38.4 KB, entirely on-charter, one section per buildable module — no off-charter mass a split would
relocate), so its promotions were applied rather than held. It ended the sweep at **48.9 KB (+27%)**, with
Core 14.0 / Catalog 13.7 / PCL 10.5 KB carrying 38 of 49 KB. That is past the line for the *next* sweep:
a split job is now booked in `ROADMAP.md` § *Open: split `ARCHITECTURE.md`* and should run before any
further promotion into those three sections. No other reference doc is near its limit; the `CLAUDE.md`
router got *smaller* (4424 → 4273 bytes).

**Net reference-tier delta:** +30 statements across five reference docs (ARCHITECTURE +19, CONSUMERS +6,
VERIFICATION +4, DOMAIN +3, ROADMAP +3, minus overlap), against −4 struck/removed claims, −2 de-valued
counts, and −2 corrected-in-place falsehoods (ARCHITECTURE's "only `Log` toucher" and "zero … singletons",
both of which had become literally untrue). The journal itself grew by 7 backfilled `CHANGELOG.md`
entries covering the 2026-07-23 and 2026-07-26..29 shipped batches — that backfill is what makes this
sweep's `stub` dispositions legal, since a stub needs a dated record to point back at.
