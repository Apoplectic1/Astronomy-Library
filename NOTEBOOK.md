# NOTEBOOK.md

**Charter.** Running **lab notebook** — small, dated, chronological empirical findings captured while doing the work (a measurement, a surprising behavior, a tried-and-rejected approach). Substantial standalone records (a design, a review, a decision) belong in `docs/YYYY-MM-DD-<slug>.md` instead; standing truths graduate up into the reference set (`ARCHITECTURE.md` / `ROADMAP.md` / `DOMAIN.md` / `VERIFICATION.md` / `CONSUMERS.md`).

## 2026-08-01 — SafeEpoch raw-casts TS epochcodes whose 0/1 mean the opposite of ours (latent)

Found by the IS-repo docs audit (round 4), reported here as a code finding — not fixed. NINA's
`Epoch` enum, whose ints TS persists in `target.epochcode`, orders **JNOW=0, B1950=1, J2000=2**
(+J2050=3); `Astronomy.Catalog`'s `epoch` lookup and `Schema/Enums.cs` order **B1950=0, JNow=1,
J2000=2**. `TargetResolver.SafeEpoch(int code)` (`Build/TargetResolver.cs:512`) passes the TS int
straight through with a cast, so a non-J2000 TS target would import with JNOW↔B1950 silently
swapped, and J2050 (3) coerces to J2000 via the unknown-code default. Latent — every real target
is J2000=2 — but it is a contract-grade silent mis-map at the TS boundary: the fix is a mapping,
not a cast. Full evidence: IS `docs/2026-08-01-audit-report.md`.

**Resolved same day** (user-directed): `SafeEpoch` is now an explicit translation table, a `[Theory]`
pins codes 0–3 (incl. J2050 → coerce + report), and the doc comments on `Epoch` and
`TsTarget.EpochCode` carry both conventions. See CHANGELOG 2026-08-01.

## 2026-07-24 — "pre-Init Log calls are safe no-ops" is only true until someone Inits mid-run

Intermittent (~1-in-3) `IOException` in `LogLifecycleContractTests`: the new
`ObservationSessionContractTests` never calls `Log.Init`, so its `Log.*` calls looked like safe
silent no-ops — but xUnit runs classes in parallel, and the moment the lifecycle test's phase-2
`Init` landed, those same calls became live `File.AppendAllText` writes into *its* temp log,
whose writer handle then raced its `File.ReadAllText` (writer holds `FileShare.Read` only).
Diagnosed by looping `dotnet test` with a TRX logger until the flake reproduced (green runs 6×
in a row first — never trust a rerun). Fix: both classes joined a shared `"LogProcessGlobal"`
xUnit collection (serializes exactly those two; the rest of the bench stays parallel). Rule
learned: with process-global static state, "doesn't Init" is not the same as "doesn't touch" —
any caller of the static surface must serialize with the lifecycle test.
