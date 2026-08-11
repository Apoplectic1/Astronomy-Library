using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Register of CONSUMERS.md "Semantic assumptions" that are NOT cleanly unit-testable
/// in this bench, with the reason each resists a deterministic, self-contained assertion.
/// These are documentation, not coverage gaps to fix: each is verified (if at all) by a
/// consumer integration path, a benchmark, or code review — see the per-item note.
///
/// Covered elsewhere in the bench (for orientation):
///   #1  RaDegrees = degrees ............ XisfUnitsContractTests
///   #2  NamedSite serialization shape .. NamedSitePersistenceContractTests (the JSON
///                                        property names + lossless round-trip; the
///                                        "minutes" *meaning* stays a naming convention
///                                        — that half was registered here until 2026-07-24)
///   #3  ObserveAt geometric alt ........ MoonContractTests
///   #4  KsAt 10-param order ............. SkyBrightnessContractTests
///   #5  CatalogGraph FK/mosaic order ... CatalogGraphOrderingContractTests
///   #6  Log pre-init = silent no-op .... LogLifecycleContractTests
///   #7  NightCache pure statics ........ NightCacheContractTests
///   #8  Reader/Editor open-in-ctor ..... TargetSchedulerContractTests
///   #9  HasRequiredColumns gates writes  TargetSchedulerContractTests
///   #10 Edit key guid-or-Id (TryParse) . TargetSchedulerContractTests
///   #13 MoonEphemeris.Sample exact count MoonContractTests
///   #14 ScanAsync missing-root throws .. ImageLibraryScannerContractTests
///   #15 Polyline fail-fast inputs ...... PolylineHorizonContractTests (moved out of this
///                                        registry 2026-08-11 — the "does not validate" note
///                                        below predated the fail-fast ctor; code adjudicated
///                                        as the contract)
///   #16 LunarAge non-UTC throws ........ MoonContractTests
///   #19 EffectiveExposure rule ......... EffectiveExposureContractTests
///   #20 ReadPlanEffectiveExposure ...... TargetSchedulerContractTests
///   #21 TsEditableSchema enum codes .... TsEditableSchemaContractTests
///   #22 Cadence-clear gating ........... TsEditableSchemaContractTests (classification)
///                                        + TsCadenceClearContractTests (DB behavior)
///   #23 Writer update-only + ratchet ... TargetSchedulerWriterContractTests
///   #24 Δmag bandwidth-independent ..... MoonContractTests (deterministic half; the
///                                        gate-internal refraction half is registered below)
///   #25 ObservationSession lifecycle ... ObservationSessionContractTests (state machine;
///                                        log-line assertions live in Astronomy.Diagnostics.Tests)
///   #26 Catalog cancellation throws .... CatalogCancellationContractTests (reader per-row,
///                                        resolver phase-boundary, scanner; promoted 2026-08-11
///                                        from "Contract facts not yet numbered")
///   #27 Write-back join key + pairing .. WriteBackJoinKeyContractTests (duration buckets,
///                                        never-fold, real-zero write, case-insensitive filter)
///   #28 XISF codec semantics ........... XisfCodecContractTests (checksum-over-stored, raw-LZ4
///                                        declared size, tolerant-parse/strict-use)
///   #29 WcsOrientation conventions ..... WcsOrientationContractTests (determinant-sign parity,
///                                        NINA real-matrix PA vector, 180° mirror ambiguity)
///
/// NOT cleanly unit-testable (placeholders below mirror this list):
///
///   #11 BestSession.PlaceBest(..., altitudeQuality: null) takes the sin(alt) closed-form
///       fast path (~25× faster) that TP relies on via the null default.
///       — A PERFORMANCE characteristic. Functionally null and a supplied quality fn agree
///         (that part *is* assertable), but "~25× faster" is a benchmark claim, not a unit
///         assertion — timing is machine/JIT/load dependent and flaky as a pass/fail gate.
///         Belongs in a BenchmarkDotNet harness, not here.
///
///   #12 AltitudeCurve.Sample / MoonEphemeris.Sample (Meeus core) are thread-safe / lock-free
///       (TP parallelizes per-target).
///       — THREAD-SAFETY / absence of a data race. A passing concurrent test proves nothing
///         (races are nondeterministic; a green run is not absence of the bug), and a failing
///         one is flaky. Real assurance is by inspection (no shared mutable state in the Meeus
///         path) — a stress test could only raise suspicion, never establish the contract.
///
///   #15 (MOVED to the covered list above, 2026-08-11) ~~PolylineHorizonProfile preconditions
///       are the caller's to honor~~ — this note predated the fail-fast ctor (length mismatch
///       and empty input throw ArgumentException; azimuths normalized + sorted internally);
///       adjudicated 2026-08-11: the code is the contract, and PolylineHorizonContractTests
///       now pins it. The number's registry slot is kept so notes referencing it reconcile.
///
///   #17 ObservationMoment.Zone must stay in lockstep with Location.TimeZoneInfo.
///       — A CROSS-OBJECT INVARIANT maintained by construction across two types over the
///         lifetime of a consumer's object graph; it is the consumer's assembly responsibility
///         (how TP builds an ObservationMoment from a Location), not a single Library call with
///         an observable in/out. No isolated unit boundary expresses "they were kept in sync".
///
///   #18 (RETIRED 2026-07-06) ~~TsEditGate / editor calls SqliteConnection.ClearAllPools() after
///       every verified write~~.
///       — No longer a contract at all: TSM's sync-model rework (commit 9e8ec19) deleted the call —
///         edits now hit a LOCAL working copy (pull at open / push-as-replay), so the stale-SMB-read
///         concern the call defended against no longer exists. The number is kept (per CONSUMERS.md,
///         the assumption list is append-only) so notes referencing #18 stay reconcilable.
/// </summary>
public sealed class NotCleanlyTestableAssumptions
{
    [Fact(Skip = "#11 PlaceBest null-altitudeQuality fast path is a ~25× PERFORMANCE claim — belongs in a BenchmarkDotNet harness, not a pass/fail unit assertion.")]
    public void Assumption11_PlaceBest_FastPath_IsPerformance() { }

    [Fact(Skip = "#12 Meeus Sample thread-safety is absence-of-a-race — a green concurrent run proves nothing and a red one is flaky; assured by inspection (no shared mutable state).")]
    public void Assumption12_Sample_ThreadSafety_NotDeterministicallyTestable() { }

    [Fact(Skip = "#17 ObservationMoment.Zone ↔ Location.TimeZoneInfo lockstep is a cross-object invariant maintained by the CONSUMER's construction, not a single Library in/out.")]
    public void Assumption17_ObservationMoment_ZoneLockstep_IsConsumerInvariant() { }

    [Fact(Skip = "#18 RETIRED 2026-07-06 — the ClearAllPools() call was deleted with TSM's local-working-copy sync rework; the number is kept because the assumption list is append-only.")]
    public void Assumption18_Retired_ClearAllPoolsCallDeleted() { }

    [Fact(Skip = "#24 (refraction/site half) — the moon gate's internal Saemundsson correction and Location-derived site params live inside internal MoonClearIntersect; externally observable only as ~2-minute window-boundary shifts against a real ephemeris, which is a brittle assertion. The bandwidth-independence half IS deterministically pinned (MoonContractTests.KsMoonDeltaMag_MatchesKsAtDifference_AtEveryBandwidth); the convention itself is pinned indirectly by #3 (ObserveAt stays geometric) plus the Core-side gate tests.")]
    public void Assumption24_GateInternalRefraction_ObservableOnlyAsBoundaryShift() { }
}
