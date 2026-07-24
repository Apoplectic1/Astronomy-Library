using Astronomy.Diagnostics;
using Xunit;

// Log is process-global static state: every test re-Inits with a fresh RootOverride temp dir (Init is
// idempotent by design — the TSM app tests set the precedent), and all session tests live in this one
// class so xUnit's same-class serialization keeps them from interleaving on the shared Log.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Astronomy.Diagnostics.Tests;

public sealed class ObservationSessionTests : IDisposable
{
    private readonly string mRoot;

    public ObservationSessionTests()
    {
        mRoot = Path.Combine(Path.GetTempPath(), "obs-session-tests-" + Guid.NewGuid().ToString("N"));
        Log.Init(new AppLogIdentity("BenchApp", "bench.log", "BENCH_DIAG", DiagDefault.None, RootOverride: mRoot));
        // No StartNewSession: rotation is irrelevant here and Append creates the folder on demand.
    }

    public void Dispose()
    {
        try { Directory.Delete(mRoot, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private string LogText() =>
        File.Exists(Log.FilePath) ? File.ReadAllText(Log.FilePath) : string.Empty;

    // A session over recording fake delegates. capture: null => simulate grab failure.
    private ObservationSession MakeSession(
        List<string> calls,
        Func<string> ctx = null,
        Func<(int, int, int, int), string, string> capture = null,
        int settleDelayMs = 0)
    {
        return new ObservationSession(
            contextProvider: ctx ?? (() => "k=v"),
            ownerBounds: () => { calls.Add("bounds"); return (1, 2, 3, 4); },
            hideOverlay: () => calls.Add("hide"),
            showOverlay: () => calls.Add("show"),
            settleDelayMs: settleDelayMs,
            newScreenshotPath: id => { calls.Add("path"); return Path.Combine(mRoot, $"obs-{id}.png"); },
            capture: capture ?? ((bounds, path) => { calls.Add("capture"); return path; }));
    }

    // ------------------------------------------------------------------ Begin validation (public path)

    [Fact]
    public void Begin_NullArguments_Throw()
    {
        Func<string> ctx = () => "";
        Func<(int, int, int, int)> bounds = () => (0, 0, 1, 1);
        Action nop = () => { };

        Assert.Throws<ArgumentNullException>(() => ObservationSession.Begin(null, bounds, nop, nop));
        Assert.Throws<ArgumentNullException>(() => ObservationSession.Begin(ctx, null, nop, nop));
        Assert.Throws<ArgumentNullException>(() => ObservationSession.Begin(ctx, bounds, null, nop));
        Assert.Throws<ArgumentNullException>(() => ObservationSession.Begin(ctx, bounds, nop, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObservationSession.Begin(ctx, bounds, nop, nop, settleDelayMs: -1));
    }

    [Fact]
    public void Begin_MintsFourCharId_AndLogsStart()
    {
        ObservationSession session = ObservationSession.Begin(
            () => "", () => (0, 0, 1, 1), () => { }, () => { });

        Assert.Equal(4, session.Id.Length);
        Assert.Contains($"USER_OBS_START id={session.Id}", LogText());
        Assert.False(session.IsTerminated);
        Assert.Equal(0, session.CaptureCount);
    }

    // ------------------------------------------------------------------ Capture

    [Fact]
    public async Task Capture_Success_DelegateOrder_Count_Status_CapLine()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls);

        ObservationCapture cap = await session.CaptureAsync();

        Assert.True(cap.Succeeded);
        Assert.Equal(new[] { "hide", "bounds", "path", "capture", "show" }, calls);
        Assert.Equal(1, session.CaptureCount);
        Assert.Matches(@"^captured 1 · \d{2}:\d{2}:\d{2}$", cap.StatusText);
        Assert.Contains($"USER_OBS_CAP id={session.Id} screenshot={cap.Path}", LogText());
    }

    [Fact]
    public async Task Capture_MarkDelayed_AddsDelayedToStatus()
    {
        ObservationSession session = MakeSession([]);

        ObservationCapture cap = await session.CaptureAsync(delayMs: 1, markDelayed: true);

        Assert.Matches(@"^captured 1 \(delayed\) · \d{2}:\d{2}:\d{2}$", cap.StatusText);
    }

    [Fact]
    public async Task Capture_Failure_NoCount_NoCapLine_StatusNamesLogFile()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls, capture: (bounds, path) => null);

        ObservationCapture cap = await session.CaptureAsync();

        Assert.False(cap.Succeeded);
        Assert.Equal(0, session.CaptureCount);
        Assert.DoesNotContain("USER_OBS_CAP", LogText());
        Assert.Equal("capture failed — see bench.log", cap.StatusText);
        Assert.Contains("show", calls);   // overlay reshown even on failure
    }

    [Fact]
    public async Task Capture_BoundsDelegateThrows_DegradesToFailedCapture()
    {
        ObservationSession session = new ObservationSession(
            contextProvider: () => "",
            ownerBounds: () => throw new InvalidOperationException("boom"),
            hideOverlay: () => { },
            showOverlay: () => { },
            settleDelayMs: 0,
            newScreenshotPath: id => "x.png",
            capture: (bounds, path) => path);

        ObservationCapture cap = await session.CaptureAsync();

        Assert.False(cap.Succeeded);
        Assert.Contains("capture threw", LogText());   // WARN, not an escaped exception
    }

    // ------------------------------------------------------------------ Complete

    [Fact]
    public async Task Complete_FinalShotNotCounted_EndLineCarriesCtxNotesPath()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls, ctx: () => "rows=5");

        bool closed = await session.CompleteAsync("line1\nline2");

        Assert.True(closed);
        Assert.True(session.IsTerminated);
        Assert.Equal(0, session.CaptureCount);                 // final shot not counted
        Assert.DoesNotContain("USER_OBS_CAP", LogText());      // and no CAP line
        Assert.DoesNotContain("show", calls);                  // no reshow on the final shot
        Assert.Contains($"USER_OBS_END id={session.Id} ctx=(rows=5) screenshot=", LogText());
        Assert.Contains("notes=\"line1\\nline2\"", LogText()); // escaped to one line
    }

    [Fact]
    public async Task Complete_BlankNotes_LogAsCheckpoint()
    {
        ObservationSession session = MakeSession([]);

        await session.CompleteAsync(null);

        Assert.Contains("notes=\"(checkpoint)\"", LogText());
    }

    [Fact]
    public async Task Complete_ContextProviderThrows_EndStillWritten_WithWarn()
    {
        ObservationSession session = MakeSession([], ctx: () => throw new InvalidOperationException("ctx boom"));

        bool closed = await session.CompleteAsync("notes");

        Assert.True(closed);
        string log = LogText();
        Assert.Contains("WARN Observation contextProvider threw", log);
        Assert.Contains($"USER_OBS_END id={session.Id} ctx=() ", log);
    }

    // ------------------------------------------------------------------ Terminator idempotency

    [Fact]
    public void Cancel_Twice_LogsOneCancel()
    {
        ObservationSession session = MakeSession([]);

        session.Cancel();
        session.Cancel();

        Assert.Equal(1, CountOf($"USER_OBS_CANCEL id={session.Id}"));
        Assert.True(session.IsTerminated);
    }

    [Fact]
    public async Task Complete_ThenCancel_LogsEndOnly()
    {
        ObservationSession session = MakeSession([]);

        Assert.True(await session.CompleteAsync("n"));
        session.Cancel();

        Assert.Contains("USER_OBS_END", LogText());
        Assert.DoesNotContain("USER_OBS_CANCEL", LogText());
    }

    [Fact]
    public async Task Capture_AfterTermination_NoOps_WithoutTouchingDelegates()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls);
        session.Cancel();
        calls.Clear();

        ObservationCapture cap = await session.CaptureAsync();

        Assert.False(cap.Succeeded);
        Assert.Empty(calls);                                    // hide/bounds/capture/show all skipped
        Assert.DoesNotContain("USER_OBS_CAP", LogText());
    }

    [Fact]
    public async Task Complete_AfterTermination_ReturnsFalse()
    {
        ObservationSession session = MakeSession([]);
        session.Cancel();

        Assert.False(await session.CompleteAsync("n"));
        Assert.DoesNotContain("USER_OBS_END", LogText());
    }

    // ------------------------------------------------------------------ Mid-countdown termination + overlap

    [Fact]
    public async Task Cancel_DuringDelayedCapture_NoCapLine_NoReshow()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls);

        Task<ObservationCapture> pending = session.CaptureAsync(delayMs: 250);
        session.Cancel();                                        // during the countdown
        ObservationCapture cap = await pending;

        Assert.False(cap.Succeeded);
        Assert.DoesNotContain("USER_OBS_CAP", LogText());
        Assert.DoesNotContain("show", calls);                    // dead window never touched
        Assert.DoesNotContain("capture", calls);
    }

    [Fact]
    public async Task Capture_WhileCaptureInFlight_NoOps()
    {
        var calls = new List<string>();
        ObservationSession session = MakeSession(calls);

        // No SynchronizationContext here, so CaptureAsync runs synchronously up to its await and the
        // busy flag is latched on this thread before the first task is returned — deterministic overlap.
        Task<ObservationCapture> first = session.CaptureAsync(delayMs: 100);
        ObservationCapture second = await session.CaptureAsync();

        Assert.False(second.Succeeded);                          // warned no-op
        ObservationCapture firstResult = await first;
        Assert.True(firstResult.Succeeded);
        Assert.Equal(1, session.CaptureCount);
        Assert.Contains("capture ignored (busy)", LogText());
    }

    private int CountOf(string needle)
    {
        string text = LogText();
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
        return count;
    }
}
