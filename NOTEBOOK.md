# NOTEBOOK.md

**Charter.** Running **lab notebook** — small, dated, chronological empirical findings captured while doing the work (a measurement, a surprising behavior, a tried-and-rejected approach). Substantial standalone records (a design, a review, a decision) belong in `docs/YYYY-MM-DD-<slug>.md` instead; standing truths graduate up into the reference set (`ARCHITECTURE.md` / `ROADMAP.md` / `DOMAIN.md` / `VERIFICATION.md` / `CONSUMERS.md`).

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
