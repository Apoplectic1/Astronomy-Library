using System.Windows.Forms;

namespace Astronomy.Diagnostics.WinForms;

/// <summary>
/// Application-level Ctrl+N wiring for <see cref="DiagnosticsDialog"/>. Call
/// <see cref="Register"/> once, from the consumer's main-window construction — every WinForms
/// consumer gets identical hotkey coverage by construction instead of hand-rolling its own
/// keystroke routing.
/// </summary>
/// <remarks>
/// <para>
/// The wiring is an <see cref="Application.AddMessageFilter"/> filter rather than a
/// <c>ProcessCmdKey</c> override because a form's key chain misses two whole UI states: MenuStrip
/// menu mode (ToolStrip's ModalMenuFilter reroutes keyboard input to the dropdown window — a
/// filter registered later than this one, so this one sees the key first) and modal WinForms
/// dialogs (their modal loops run the thread's filter chain, but the owner form's
/// <c>ProcessCmdKey</c> never runs). Native modal loops (common file dialogs,
/// <c>MessageBox</c>) never consult WinForms filters — the hotkey is dark there by Win32 design.
/// </para>
/// <para>
/// Opening the dialog ends menu mode (the dropdown closes on activation change), so a shot of
/// transient light-dismiss UI is still the dialog-first + delayed-capture workflow.
/// </para>
/// </remarks>
public static class DiagnosticsHotkey
{
    private static Filter? sRegistered;

    /// <summary>
    /// Install the Ctrl+N filter for the process's UI thread: the hotkey opens (or focuses) the
    /// diagnostics dialog over <paramref name="owner"/>. <paramref name="contextProvider"/> is
    /// forwarded to <see cref="DiagnosticsDialog.ShowOrFocus"/> (called at OK time). Call once at
    /// main-window construction; a second call throws — double wiring is a programmer error,
    /// matching <see cref="ObservationSession.Begin"/>'s fail-fast policy.
    /// </summary>
    public static void Register(Form owner, Func<string>? contextProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (sRegistered != null)
            throw new InvalidOperationException("DiagnosticsHotkey.Register was already called for this process.");

        sRegistered = new Filter(owner, contextProvider);
        Application.AddMessageFilter(sRegistered);
    }

    private sealed class Filter : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100;

        private readonly Form mOwner;
        private readonly Func<string>? mContextProvider;

        public Filter(Form owner, Func<string>? contextProvider)
        {
            mOwner = owner;
            mContextProvider = contextProvider;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_KEYDOWN) return false;
            if ((Keys)m.WParam != Keys.N) return false;
            if (Control.ModifierKeys != Keys.Control) return false;

            DiagnosticsDialog.ShowOrFocus(mOwner, mContextProvider);
            return true;
        }
    }
}
