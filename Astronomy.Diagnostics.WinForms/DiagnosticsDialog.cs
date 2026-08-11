using System.Drawing;
using System.Windows.Forms;

namespace Astronomy.Diagnostics.WinForms;

/// <summary>
/// Ctrl+N observation dialog — the WinForms shell over <see cref="ObservationSession"/>, which owns
/// the orchestration (id, USER_OBS START/CAP/END/CANCEL sequencing, single-terminator guarantee,
/// capture counting, status wording, hide → grab → reshow). This class keeps only framework glue:
/// the Form, its controls, the singleton focus-existing rule, and the four delegates the session
/// drives. Behaviors follow the portfolio's shared dialog conventions (converged 2026-07-24):
/// Enter = newline, Ctrl+Enter = OK, delayed-capture button, shared status text.
/// </summary>
/// <remarks>
/// <para>
/// Modeless + TopMost so the user can interact with the owner window while the dialog stays open.
/// The dialog's open period brackets the relevant diagnostic lines in the consumer's log:
/// </para>
/// <code>
///   USER_OBS_START id=4f2a build=1.0.0+abc1234
///   DIAG/… (the consumer's gated channels)
///   USER_OBS_CAP id=4f2a screenshot=…           (each mid-session Capture)
///   USER_OBS_END id=4f2a ctx=(…) screenshot=… notes="…"
/// </code>
/// <para>
/// <c>grep id=4f2a</c> in the consumer's log surfaces the full investigation window. Empty /
/// whitespace-only notes is the "all-okay checkpoint" gesture: the log line carries
/// <c>notes="(checkpoint)"</c> so those moments grep cleanly. Singleton: re-pressing the hotkey
/// while the dialog is open focuses the existing instance (no second dialog, no second START).
/// </para>
/// </remarks>
public sealed class DiagnosticsDialog : Form
{
    // Static instance tracker so re-trigger focuses the existing dialog
    // instead of stacking. Cleared on FormClosing.
    private static DiagnosticsDialog? sCurrent;

    private readonly ObservationSession mSession;
    private readonly Form? mOwnerForm;
    private readonly TextBox mNotes;
    private readonly Button mOk;
    private readonly Button mCancel;
    private readonly Button mCapture;
    private readonly Button mDelayedCapture;
    private readonly Label mStatus;

    private DiagnosticsDialog(Form? ownerForm, Func<string>? contextProvider)
    {
        mOwnerForm = ownerForm;

        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(600, 220);
        // Floor at the initial size: the sizable tool window must not shrink the single button
        // row into overlap (status label is AutoSize; OK/Cancel are right-anchored).
        MinimumSize = Size;
        Padding = new Padding(10);

        mNotes = new TextBox
        {
            Location = new Point(10, 8),
            Size = new Size(580, 162),
            Multiline = true,
            AcceptsReturn = true,   // Enter = newline (shared dialog convention)
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        // Ctrl+Enter commits (shared dialog convention).
        mNotes.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnOkClick(this, EventArgs.Empty);
            }
        };

        // Capture stays open and re-shows itself after the grab; OK / Cancel are terminal.
        mCapture = new Button
        {
            Text = "Capture",
            Location = new Point(10, 180),
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        mCapture.Click += OnCaptureClick;

        // Delayed capture for transient UI (menus, dropdowns): those are light-dismiss, so
        // they close the moment this dialog takes focus — an immediate Capture can never
        // contain one. Hides right away (focus returns to the owner), leaves 5 s to open
        // the transient state, then grabs with no focus change at capture time.
        mDelayedCapture = new Button
        {
            Text = "Capture in 5 s",
            Location = new Point(94, 180),
            Size = new Size(96, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        mDelayedCapture.Click += OnDelayedCaptureClick;

        mStatus = new Label
        {
            Location = new Point(196, 185),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };

        mOk = new Button
        {
            Text = "OK",
            Location = new Point(435, 180),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        mOk.Click += OnOkClick;

        mCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(515, 180),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        mCancel.Click += OnCancelClick;

        // No AcceptButton: Enter types a newline in the notes box (shared convention);
        // Ctrl+Enter and the OK button commit. Esc still cancels.
        CancelButton = mCancel;

        Controls.Add(mNotes);
        Controls.Add(mCapture);
        Controls.Add(mDelayedCapture);
        Controls.Add(mStatus);
        Controls.Add(mOk);
        Controls.Add(mCancel);

        FormClosing += OnFormClosing;

        // Session begins after every control exists: its delegates capture fields (mNotes, the
        // owner) that must be constructed by the time the session can invoke them.
        // settleDelayMs: 0 — WinForms Hide() is synchronous and the owner Refresh() below repaints
        // the occluded region before the grab; months of ghost-free captures say no DWM settle is
        // needed here (unlike WinUI's fade-out, which needs the session's 450 default).
        mSession = ObservationSession.Begin(
            contextProvider ?? (() => string.Empty),
            ownerBounds: () =>
            {
                if (mOwnerForm == null || mOwnerForm.IsDisposed) return (0, 0, 0, 0);
                Rectangle b = mOwnerForm.Bounds;
                return (b.X, b.Y, b.Width, b.Height);
            },
            hideOverlay: () =>
            {
                Hide();
                if (mOwnerForm != null && !mOwnerForm.IsDisposed) mOwnerForm.Refresh();
            },
            showOverlay: () =>
            {
                Show();
                mNotes.Focus();
            },
            capture: (bounds, path) => ScreenCapture.ToPng(bounds.X, bounds.Y, bounds.Width, bounds.Height, path),
            settleDelayMs: 0);

        Text = "Diagnostics (id=" + mSession.Id + ")";
    }

    /// <summary>
    /// Show modeless over the given owner, or focus the existing instance if one is already open.
    /// The <paramref name="contextProvider"/> is called at OK time to capture the owner app's
    /// current state for the USER_OBS_END line.
    /// </summary>
    public static void ShowOrFocus(Form owner, Func<string>? contextProvider)
    {
        if (sCurrent != null && !sCurrent.IsDisposed)
        {
            if (sCurrent.WindowState == FormWindowState.Minimized)
                sCurrent.WindowState = FormWindowState.Normal;
            sCurrent.BringToFront();
            sCurrent.Activate();
            return;
        }

        var dlg = new DiagnosticsDialog(owner, contextProvider);   // ObservationSession.Begin logs START
        sCurrent = dlg;
        dlg.Show(owner);
        dlg.mNotes.Focus();
    }

    // async void is safe here: ObservationSession members never throw after Begin
    // (each delegate is individually guarded library-side), so there is nothing
    // for the unobserved-exception path to catch.
    private async void OnCaptureClick(object? sender, EventArgs e)
    {
        ObservationCapture cap = await mSession.CaptureAsync();
        mStatus.Text = cap.StatusText;
    }

    private async void OnDelayedCaptureClick(object? sender, EventArgs e)
    {
        ObservationCapture cap = await mSession.CaptureAsync(delayMs: 5000, markDelayed: true);
        mStatus.Text = cap.StatusText;
    }

    private async void OnOkClick(object? sender, EventArgs e)
    {
        if (await mSession.CompleteAsync(mNotes.Text))   // false = capture in flight; stay open, retry
            Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        mSession.Cancel();
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        mSession.Cancel();   // idempotent: a no-op after OK/Cancel, the terminator on close-X
        if (ReferenceEquals(sCurrent, this)) sCurrent = null;
    }
}
