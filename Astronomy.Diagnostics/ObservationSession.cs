namespace Astronomy.Diagnostics;

/// <summary>
/// The result of one observation capture attempt: the PNG's path (null when the grab failed) and the
/// ready-to-display status line the consumer's dialog shows verbatim (<c>captured 2 · 21:14:05</c>,
/// <c>captured 3 (delayed) · 21:14:33</c>, or <c>capture failed — see tsm.log</c>). The library owns the
/// wording so both apps' dialogs read identically.
/// </summary>
public sealed record ObservationCapture(string? Path, string StatusText)
{
    /// <summary>True when the screenshot landed on disk (and a USER_OBS_CAP line was written).</summary>
    public bool Succeeded => Path is not null;
}

/// <summary>
/// One Ctrl+N observation: mints the short id, logs <c>USER_OBS_START</c>, and owns the app-agnostic
/// orchestration both consumer dialogs used to hand-roll — the hide → settle → grab → reshow capture
/// choreography, capture counting, the guarded context-provider call, and the exactly-one-terminator
/// guarantee (<c>USER_OBS_END</c> via <see cref="CompleteAsync"/> or <c>USER_OBS_CANCEL</c> via
/// <see cref="Cancel"/>). The dialog object, its singleton, and its controls stay app-side: the app hands
/// <see cref="Begin"/> three delegates (owner bounds, hide overlay, show overlay) because this library
/// references neither WinForms nor WinUI.
///
/// <para><b>Threading contract.</b> Call <see cref="CaptureAsync"/> / <see cref="CompleteAsync"/> from the
/// UI thread. The awaits deliberately keep the caller's <see cref="SynchronizationContext"/> (no
/// <c>ConfigureAwait(false)</c>) — it is the only framework-agnostic marshal available, and both
/// WinForms and WinUI restore correctly through it — so the delegates always run on the caller's context.</para>
///
/// <para><b>Settle delay.</b> <c>settleDelayMs</c> is a property of the app's overlay-hide behavior, fixed
/// per session: WinUI needs ~450 ms for the window fade-out + DWM recomposite to leave the frame (150 ms
/// left a translucent ghost of the dialog in the shot, observed 2026-06-10), while WinForms
/// <c>Hide()</c> + owner <c>Refresh()</c> is synchronous, so 0 is correct there. The per-call
/// <c>delayMs</c> override on <see cref="CaptureAsync"/> exists for the delayed capture (typically
/// 5000 ms), whose hidden countdown lets the user open a light-dismiss flyout or menu that would
/// otherwise die on focus change before the grab.</para>
///
/// <para><b>Failure policy.</b> <see cref="Begin"/> throws on null/invalid arguments — a wiring-time
/// programmer error should fail fast. Every member after that is best-effort and never throws into UI
/// code: a throwing delegate is caught, logged via <see cref="Log.Warn(string, Exception)"/>, and
/// degraded to a failed capture / empty context, mirroring <see cref="Log"/>'s own policy.</para>
/// </summary>
public sealed class ObservationSession
{
    private readonly Func<string> mContextProvider;
    private readonly Func<(int X, int Y, int Width, int Height)> mOwnerBounds;
    private readonly Action mHideOverlay;
    private readonly Action mShowOverlay;
    private readonly int mSettleDelayMs;
    private readonly Func<string, string> mNewScreenshotPath;                          // id -> fresh path
    private readonly Func<(int X, int Y, int Width, int Height), string, string?> mCapture; // bounds, path -> path?

    private bool mBusy;   // UI-thread precondition makes a plain bool sufficient (no interlock).

    /// <summary>
    /// Start a session: mints the 4-char id and logs <c>USER_OBS_START</c>. The app calls this once per
    /// opened dialog (its window singleton is what prevents a second concurrent session).
    /// <paramref name="ownerBounds"/> returns the owner window's screen rectangle in physical pixels;
    /// <paramref name="hideOverlay"/> / <paramref name="showOverlay"/> hide and restore the observation
    /// dialog itself (show typically also re-activates and refocuses the notes box).
    /// </summary>
    public static ObservationSession Begin(
        Func<string> contextProvider,
        Func<(int X, int Y, int Width, int Height)> ownerBounds,
        Action hideOverlay,
        Action showOverlay,
        int settleDelayMs = 450)
    {
        ArgumentNullException.ThrowIfNull(contextProvider);
        ArgumentNullException.ThrowIfNull(ownerBounds);
        ArgumentNullException.ThrowIfNull(hideOverlay);
        ArgumentNullException.ThrowIfNull(showOverlay);
        ArgumentOutOfRangeException.ThrowIfNegative(settleDelayMs);

        var session = new ObservationSession(
            contextProvider, ownerBounds, hideOverlay, showOverlay, settleDelayMs,
            Log.NewObservationScreenshotPath,
            (bounds, path) => ScreenCapture.ToPng(bounds.X, bounds.Y, bounds.Width, bounds.Height, path));
        Log.UserObservationStart(session.Id);
        return session;
    }

    // Test seam: fake newScreenshotPath/capture let unit tests drive the state machine without a real
    // screen grab. Does NOT log START — Begin owns that.
    internal ObservationSession(
        Func<string> contextProvider,
        Func<(int X, int Y, int Width, int Height)> ownerBounds,
        Action hideOverlay,
        Action showOverlay,
        int settleDelayMs,
        Func<string, string> newScreenshotPath,
        Func<(int X, int Y, int Width, int Height), string, string?> capture)
    {
        mContextProvider = contextProvider;
        mOwnerBounds = ownerBounds;
        mHideOverlay = hideOverlay;
        mShowOverlay = showOverlay;
        mSettleDelayMs = settleDelayMs;
        mNewScreenshotPath = newScreenshotPath;
        mCapture = capture;
        Id = Guid.NewGuid().ToString("N")[..4];
    }

    /// <summary>The short observation id shared by every USER_OBS line and screenshot filename of this
    /// session — <c>grep id=&lt;this&gt;</c> surfaces the whole investigation window.</summary>
    public string Id { get; }

    /// <summary>Successful mid-session captures so far (the final <see cref="CompleteAsync"/> shot is not
    /// counted — it lives only in the END line, matching the historical dialogs).</summary>
    public int CaptureCount { get; private set; }

    /// <summary>Latched true once END or CANCEL has been logged; every later capture/terminate call is a
    /// no-op — the exactly-one-terminator guarantee is structural, not a caller obligation.</summary>
    public bool IsTerminated { get; private set; }

    /// <summary>
    /// One mid-session capture: hide the overlay → wait <paramref name="delayMs"/> (session settle when
    /// null) → grab the owner rectangle → reshow. On success bumps <see cref="CaptureCount"/> and logs
    /// <c>USER_OBS_CAP</c>; on failure logs nothing (the status text tells the user where to look).
    /// Pass <paramref name="delayMs"/> ≈ 5000 with <paramref name="markDelayed"/> for the delayed capture.
    /// No-op (failed result) when terminated or a capture is already in flight.
    /// </summary>
    public async Task<ObservationCapture> CaptureAsync(int? delayMs = null, bool markDelayed = false)
    {
        if (IsTerminated || mBusy)
        {
            Log.Warn($"ObservationSession id={Id}: capture ignored ({(IsTerminated ? "terminated" : "busy")})");
            return new ObservationCapture(null, FailedStatusText());
        }

        mBusy = true;
        try
        {
            string? path = await HideGrabAsync(delayMs ?? mSettleDelayMs, reshow: true);
            if (path is null)
                return new ObservationCapture(null, FailedStatusText());

            CaptureCount++;
            Log.UserObservationCapture(Id, path);
            string delayed = markDelayed ? " (delayed)" : string.Empty;
            return new ObservationCapture(path, $"captured {CaptureCount}{delayed} · {DateTime.Now:HH:mm:ss}");
        }
        finally
        {
            mBusy = false;
        }
    }

    /// <summary>
    /// Finish the session: one final capture (session settle, no reshow, not counted, no CAP line), the
    /// context provider called defensively (a throw logs a WARN and yields empty context), then
    /// <c>USER_OBS_END</c> with the escaped notes — and the terminator latches. Returns true when END was
    /// logged (caller closes its dialog); false when nothing happened (already terminated, or a capture
    /// in flight — stay open and let the user retry).
    /// </summary>
    public async Task<bool> CompleteAsync(string? notes)
    {
        if (IsTerminated || mBusy) return false;

        mBusy = true;
        try
        {
            string? path = await HideGrabAsync(mSettleDelayMs, reshow: false);

            string ctx = string.Empty;
            try
            {
                ctx = mContextProvider();
            }
            catch (Exception ex)
            {
                Log.Warn("Observation contextProvider threw", ex);
            }

            Log.UserObservationEnd(Id, ctx, notes ?? string.Empty, path ?? string.Empty);
            IsTerminated = true;
            return true;
        }
        finally
        {
            mBusy = false;
        }
    }

    /// <summary>
    /// Abandon the session: logs <c>USER_OBS_CANCEL</c> once and latches the terminator. Idempotent — wire
    /// it to both the Cancel button and the window's Closed/FormClosing fallback; whichever fires first
    /// wins and the other is a no-op, so every START gets exactly one terminator.
    /// </summary>
    public void Cancel()
    {
        if (IsTerminated) return;
        Log.UserObservationCancel(Id);
        IsTerminated = true;
    }

    // Hide → settle → grab → (reshow). Each delegate individually guarded: this method never throws.
    private async Task<string?> HideGrabAsync(int delayMs, bool reshow)
    {
        try
        {
            mHideOverlay();
        }
        catch (Exception ex)
        {
            Log.Warn($"ObservationSession id={Id}: hideOverlay threw", ex);
        }

        if (delayMs > 0)
            await Task.Delay(delayMs);   // keeps the caller's SynchronizationContext — see class docs

        // A delayed capture can outlive the dialog (Cancel/close during the countdown): bail without
        // grabbing or touching the (possibly dead) window.
        if (IsTerminated) return null;

        string? path = null;
        try
        {
            path = mCapture(mOwnerBounds(), mNewScreenshotPath(Id));
        }
        catch (Exception ex)
        {
            // ScreenCapture.ToPng is itself best-effort; this guard covers the ownerBounds/path delegates.
            Log.Warn($"ObservationSession id={Id}: capture threw", ex);
        }

        if (reshow)
        {
            try
            {
                mShowOverlay();
            }
            catch (Exception ex)
            {
                Log.Warn($"ObservationSession id={Id}: showOverlay threw", ex);
            }
        }
        return path;
    }

    private static string FailedStatusText()
    {
        string logName = Log.FilePath.Length == 0 ? "log" : Path.GetFileName(Log.FilePath);
        return $"capture failed — see {logName}";
    }
}
