using Astronomy.Diagnostics;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract tests for CONSUMERS.md assumption #25 — the ObservationSession lifecycle both consumer
/// dialogs (TP DiagnosticsDialog, TSM DiagnosticsWindow) build on: one START per Begin, exactly one
/// idempotent terminator per id, IsTerminated latches, post-termination calls are no-ops.
///
/// IMPORTANT: this class must NOT call Log.Init — LogLifecycleContractTests is the bench's only
/// Init-caller. And because ObservationSession members DO call Log.* (silent no-ops only while
/// pre-Init), this class joins the "LogProcessGlobal" collection to serialize with the lifecycle
/// test: run in parallel, its phase-2 Init turns these calls into live appends into its temp log
/// and the writer handle races its File.ReadAllText (observed 2026-07-24 as an intermittent
/// IOException). Line-level log assertions live in Astronomy.Diagnostics.Tests (its own process).
/// </summary>
[Collection("LogProcessGlobal")]
public sealed class ObservationSessionContractTests
{
    // Zero-size bounds: ScreenCapture.ToPng's non-positive-size guard returns null BEFORE touching the
    // filesystem, so the real capture path is exercised with zero side effects (important pre-Init,
    // where the screenshot path would be relative to the working dir).
    private static ObservationSession MakeSession() =>
        ObservationSession.Begin(
            contextProvider: () => "k=v",
            ownerBounds: () => (0, 0, 0, 0),
            hideOverlay: () => { },
            showOverlay: () => { },
            settleDelayMs: 0);

    // ---------------------------------------------------------------------------
    // CONSUMERS.md assumption #25:
    //   "ObservationSession logs exactly one START (at Begin) and exactly one terminator
    //    (END via CompleteAsync or CANCEL via Cancel) per id; terminators are idempotent
    //    and latch IsTerminated; post-termination captures are no-ops. Delegates run on
    //    the CaptureAsync/CompleteAsync caller's synchronization context — call from
    //    the UI thread."
    // ---------------------------------------------------------------------------

    [Fact]
    public void Begin_MintsFourCharId_NotTerminated()
    {
        ObservationSession session = MakeSession();

        Assert.Equal(4, session.Id.Length);
        Assert.False(session.IsTerminated);
        Assert.Equal(0, session.CaptureCount);
    }

    [Fact]
    public void Cancel_Latches_AndIsIdempotent()
    {
        ObservationSession session = MakeSession();

        session.Cancel();
        Assert.True(session.IsTerminated);
        session.Cancel();                      // second terminator is a no-op, not an error
        Assert.True(session.IsTerminated);
    }

    [Fact]
    public async Task Complete_Latches_ThenCancelIsNoOp_AndSecondCompleteReturnsFalse()
    {
        ObservationSession session = MakeSession();

        Assert.True(await session.CompleteAsync("notes"));   // true = END path taken, caller closes
        Assert.True(session.IsTerminated);
        session.Cancel();                                    // no-op after END
        Assert.False(await session.CompleteAsync("again"));  // false = nothing happened
    }

    [Fact]
    public async Task Capture_AfterTermination_IsFailedNoOp_AndDelegatesAreNotInvoked()
    {
        int delegateCalls = 0;
        ObservationSession session = ObservationSession.Begin(
            contextProvider: () => "",
            ownerBounds: () => { delegateCalls++; return (0, 0, 0, 0); },
            hideOverlay: () => delegateCalls++,
            showOverlay: () => delegateCalls++,
            settleDelayMs: 0);
        session.Cancel();
        delegateCalls = 0;

        ObservationCapture cap = await session.CaptureAsync();

        Assert.False(cap.Succeeded);
        Assert.Equal(0, session.CaptureCount);
        Assert.Equal(0, delegateCalls);        // the dead dialog's delegates are never touched
    }
}
