using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Astronomy.Diagnostics.WinUI;

/// <summary>
/// Ctrl+N observation dialog — the WinUI shell over <see cref="ObservationSession"/>, which owns the
/// orchestration (id, USER_OBS START/CAP/END/CANCEL sequencing, single-terminator guarantee, capture
/// counting, status wording, hide → settle → grab → reshow). This class keeps only framework glue: the
/// <see cref="Window"/>, its controls, the singleton focus-existing rule, and the delegates the session
/// drives. Behaviors follow the portfolio's shared dialog conventions (converged 2026-07-24):
/// Enter = newline, Ctrl+Enter = OK, delayed-capture button, shared status text.
/// </summary>
/// <remarks>
/// <para>
/// A separate modeless always-on-top <see cref="Window"/> (NOT a ContentDialog — that would block the
/// main UI, defeating the point): the dialog's open period brackets the user's actions in the consumer's
/// log between USER_OBS_START and USER_OBS_END, so intervening DIAG lines are chronologically scoped.
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
/// <c>notes="(checkpoint)"</c> so those moments grep cleanly. Singleton: re-invoking while the dialog is
/// open focuses the existing instance (no second window, no second START). Built in code rather than
/// XAML — WinUI controls construct imperatively exactly like WinForms, keeping this shell close to its
/// <c>Astronomy.Diagnostics.WinForms</c> sibling, which drives the same session type.
/// </para>
/// </remarks>
public sealed class DiagnosticsWindow : Window
{
    // Static instance tracker so re-invoke focuses the existing window instead of stacking. Cleared on Closed.
    private static DiagnosticsWindow? sCurrent;

    private readonly ObservationSession mSession;
    private readonly TextBox mNotes;
    private readonly TextBlock mStatus;

    private DiagnosticsWindow(Window owner, Func<string> contextProvider, string? iconPath)
    {
        // The session logs START and owns the choreography; the delegates are this window's only
        // framework-specific contribution to a capture. 450 ms settle = the WinUI default (window
        // fade-out + DWM recomposite must leave the frame before the grab; 150 ms left a translucent
        // ghost of the dialog in the shot — the empirical basis lives in the session type's docs).
        mSession = ObservationSession.Begin(
            contextProvider,
            ownerBounds: () =>
            {
                PointInt32 pos = owner.AppWindow.Position;
                SizeInt32 size = owner.AppWindow.Size;
                return (pos.X, pos.Y, size.Width, size.Height);
            },
            hideOverlay: () => AppWindow.Hide(),
            showOverlay: () =>
            {
                AppWindow.Show();
                Activate();
                // Null-forgiving: the delegate can only run after the ctor finishes assigning mNotes.
                mNotes!.Focus(FocusState.Programmatic);
            },
            capture: (bounds, path) => ScreenCapture.ToPng(bounds.X, bounds.Y, bounds.Width, bounds.Height, path));

        Title = $"Diagnostics (id={mSession.Id})";
        // Caller-supplied title-bar icon (SetIcon resolves relative paths against the CWD, so callers
        // should pass an absolute path); null = system default.
        if (iconPath is not null) AppWindow.SetIcon(iconPath);
        AppWindow.Resize(new SizeInt32(660, 360));   // wide enough for the button row + "captured N (delayed) · hh:mm:ss"
        CenterOverOwner(owner);         // default placement can land on another monitor
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;     // stays over the main window while you drive it
            p.IsMinimizable = false;
            p.IsMaximizable = false;
        }

        mNotes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(mNotes, ScrollBarVisibility.Auto);
        // Ctrl+Enter commits from inside the notes box (Enter=newline). Handled in KeyDown because the
        // TextBox consumes Enter before a button KeyboardAccelerator would see it.
        mNotes.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && IsCtrlDown())
            {
                e.Handled = true;
                OnOkClick(this, new RoutedEventArgs());
            }
        };

        // Capture stays open and re-shows itself after the grab; OK / Cancel are terminal. Capture sits
        // left, visually apart from the terminal pair on the right.
        Button capture = new() { Content = "Capture", MinWidth = 100 };
        capture.Click += OnCaptureClick;

        // Delayed capture for transient UI (flyouts, context menus): those are light-dismiss, so they
        // close the moment this window takes focus — an immediate Capture can never contain one. This
        // hides the window right away (focus returns to the main window), leaves 5 s to open the
        // transient state, then grabs with no focus change at capture time.
        Button delayedCapture = new() { Content = "Capture in 5 s", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        delayedCapture.Click += OnDelayedCaptureClick;

        mStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            Margin = new Thickness(12, 0, 0, 0),
        };

        Button ok = new() { Content = "OK", MinWidth = 90, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        ok.Click += OnOkClick;

        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        cancel.Click += OnCancelClick;

        StackPanel leftButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        leftButtons.Children.Add(capture);
        leftButtons.Children.Add(delayedCapture);
        leftButtons.Children.Add(mStatus);

        StackPanel rightButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        rightButtons.Children.Add(ok);
        rightButtons.Children.Add(cancel);

        Grid buttons = new();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftButtons, 0);
        Grid.SetColumn(rightButtons, 1);
        buttons.Children.Add(leftButtons);
        buttons.Children.Add(rightButtons);

        Grid root = new() { Padding = new Thickness(12), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(mNotes, 0);
        Grid.SetRow(buttons, 1);
        root.Children.Add(mNotes);
        root.Children.Add(buttons);
        Content = root;

        Closed += OnClosed;
    }

    /// <summary>Open the diagnostics window over <paramref name="owner"/>, or focus the existing one.
    /// <paramref name="contextProvider"/> is called at OK time so the END line carries the consumer's
    /// state as of the moment the user committed the note, not the moment the window opened.
    /// <paramref name="iconPath"/> is an optional absolute path to the title-bar icon (typically the
    /// consumer's own app icon); null keeps the system default.</summary>
    public static void ShowOrFocus(Window owner, Func<string> contextProvider, string? iconPath = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(contextProvider);

        if (sCurrent is not null)
        {
            sCurrent.AppWindow.Show();
            sCurrent.Activate();
            return;
        }

        DiagnosticsWindow w = new(owner, contextProvider, iconPath);   // ObservationSession.Begin logs START
        sCurrent = w;
        w.Activate();
        w.mNotes.Focus(FocusState.Programmatic);
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e) => RunLogged(async () =>
    {
        ObservationCapture cap = await mSession.CaptureAsync();
        mStatus.Text = cap.StatusText;
    }, "diagnostics capture");

    private void OnDelayedCaptureClick(object sender, RoutedEventArgs e) => RunLogged(async () =>
    {
        ObservationCapture cap = await mSession.CaptureAsync(delayMs: 5000, markDelayed: true);
        mStatus.Text = cap.StatusText;
    }, "diagnostics delayed capture");

    private void OnOkClick(object sender, RoutedEventArgs e) => RunLogged(async () =>
    {
        if (await mSession.CompleteAsync(mNotes.Text))   // false = capture in flight; stay open, retry
            Close();
    }, "diagnostics OK");

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        mSession.Cancel();
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        mSession.Cancel();   // idempotent: a no-op after OK/Cancel, the terminator on close-X
        if (ReferenceEquals(sCurrent, this)) sCurrent = null;
    }

    // Fire-and-forget with the diagnostics best-effort contract: a fault is logged, never thrown into
    // the dispatcher. ObservationSession members don't throw after Begin, so this guards only the
    // UI-side continuations (e.g. touching mStatus on a torn-down window).
    private static async void RunLogged(Func<Task> work, string label)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log.Warn(label + " failed", ex);
        }
    }

    private static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // Center over the owner window so the dialog appears where the user is looking (and never on a
    // different monitor); both AppWindow rects are physical pixels, so the math is direct.
    private void CenterOverOwner(Window owner)
    {
        try
        {
            PointInt32 oPos = owner.AppWindow.Position;
            SizeInt32 oSize = owner.AppWindow.Size;
            SizeInt32 mine = AppWindow.Size;
            AppWindow.Move(new PointInt32(
                oPos.X + ((oSize.Width - mine.Width) / 2),
                oPos.Y + ((oSize.Height - mine.Height) / 2)));
        }
        catch
        {
            // Positioning is cosmetic; never fail the window over it.
        }
    }
}
