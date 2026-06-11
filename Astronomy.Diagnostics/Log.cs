using System.Reflection;

namespace Astronomy.Diagnostics;

/// <summary>
/// The portfolio's diagnostic logging contract — an append-only text log under <c>%APPDATA%\&lt;app&gt;\Logs\</c>
/// with a fixed line grammar, build-configurable verbosity, session rotation, and the Ctrl+N observation protocol.
/// The <em>method surface is the convention</em>: pick the method that fits the event and the format, level,
/// gating, and local-time stamp come out right by construction — the implementation enforces the invariants so an
/// off-convention or half-formed line can't be emitted. Configure once per app via <see cref="Init"/>; a shared
/// library compiles once, so the consumer supplies its identity and build-derived verbosity rather than the
/// library reading the consumer's <c>#if DEBUG</c>.
///
/// <para><b>Two verbosity axes.</b> <i>Severity</i> — <see cref="Info"/> (always-on audit trail),
/// <see cref="Warn(string)"/>, <see cref="Error(string)"/> — always writes, surviving Release: the forensic minimum. <i>Diagnostic channels</i> —
/// <see cref="Diag"/> — are gated: defaulting per <see cref="AppLogIdentity.DiagDefault"/> (all in Debug, none in
/// Release by convention) and per-channel toggleable at runtime via the identity's env var (a comma list, or
/// <c>*</c> for all) with no recompile. <see cref="AppLogIdentity.Enabled"/> = <c>false</c> silences everything.</para>
///
/// <para><b>Observation protocol.</b> <see cref="UserObservationStart"/> plus a terminator
/// (<see cref="UserObservationEnd"/> / <see cref="UserObservationCancel"/>) share a short id, chronologically
/// bracketing whatever happened while a Ctrl+N window was open; <see cref="UserObservationCapture"/> records each
/// on-demand screenshot. <c>grep id=&lt;short&gt;</c> surfaces the whole investigation window.</para>
///
/// Best-effort: any exception while writing is swallowed so a logging failure can never cascade into the caller.
/// </summary>
public static class Log
{
    private static readonly object sGate = new();
    private static bool sEnabled;
    private static string sPath = string.Empty;
    // null sentinel = "all channels enabled"; empty set = "none"; otherwise the explicitly enabled channels.
    private static HashSet<string>? sEnabledCategories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configure the log for one app — call once at startup, before any other member and before
    /// <see cref="StartNewSession"/>. Derives the log area from <paramref name="identity"/> and resolves channel
    /// verbosity (the identity's env var overrides its build default). Idempotent; safe to call again (e.g. a test
    /// pointing <see cref="AppLogIdentity.RootOverride"/> at a temp folder).</summary>
    public static void Init(AppLogIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (sGate)
        {
            sEnabled = identity.Enabled;
            string root = identity.RootOverride
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), identity.AppName);
            sPath = Path.Combine(root, "Logs", identity.LogFileName);
            sEnabledCategories = ResolveEnabledCategories(identity);
        }
    }

    /// <summary>Full path to the active log file (empty before <see cref="Init"/>).</summary>
    public static string FilePath => sPath;

    /// <summary>The log-area folder (<c>…\Logs</c>) holding the log, its <c>.prev</c>, and <c>screenshots\</c> —
    /// one delete clears every diagnostic artifact. Empty before <see cref="Init"/>.</summary>
    public static string LogFolderPath => sPath.Length == 0 ? string.Empty : Path.GetDirectoryName(sPath)!;

    /// <summary>The folder for Ctrl+N screen captures (<c>…\Logs\screenshots</c>).</summary>
    public static string ScreenshotsFolderPath => Path.Combine(LogFolderPath, "screenshots");

    /// <summary>A fresh observation-screenshot path under <see cref="ScreenshotsFolderPath"/>, named by the shared
    /// convention <c>obs-&lt;id&gt;-&lt;local timestamp to ms&gt;.png</c> — local time matches the log stamps, and
    /// the millisecond keeps rapid captures from colliding.</summary>
    public static string NewObservationScreenshotPath(string id) =>
        Path.Combine(ScreenshotsFolderPath, $"obs-{id}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

    /// <summary>Rotate the log → <c>.log.prev</c> and <c>screenshots\</c> → <c>screenshots.prev\</c> (each
    /// overwriting any previous rotation), then start a fresh log. Call once at startup so each run's trail is
    /// self-contained and the footprint stays bounded at one session back; <c>.prev</c> screenshot paths still
    /// resolve.</summary>
    public static void StartNewSession()
    {
        if (!sEnabled || sPath.Length == 0) return;
        try
        {
            lock (sGate)
            {
                string dir = LogFolderPath;
                Directory.CreateDirectory(dir);
                string prevPath = sPath + ".prev";
                if (File.Exists(sPath))
                {
                    if (File.Exists(prevPath)) File.Delete(prevPath);
                    File.Move(sPath, prevPath);
                }
                File.WriteAllText(sPath, $"{DateTimeOffset.Now:o} INFO new session build={TryGetBuildVersion()}{Environment.NewLine}");

                string shotsDir = Path.Combine(dir, "screenshots");
                string shotsPrev = Path.Combine(dir, "screenshots.prev");
                if (Directory.Exists(shotsDir))
                {
                    if (Directory.Exists(shotsPrev)) Directory.Delete(shotsPrev, recursive: true);
                    Directory.Move(shotsDir, shotsPrev);
                }
            }
        }
        catch
        {
            // Best-effort — a logging failure must never escalate.
        }
    }

    /// <summary>Always-on audit/info line (never gated) — the forensic trail that survives Release, e.g. every
    /// edit a consumer commits.</summary>
    public static void Info(string message) => Append("INFO", message, null);

    /// <summary>Always-on warning.</summary>
    public static void Warn(string message) => Append("WARN", message, null);

    /// <summary>Always-on warning with the exception appended.</summary>
    public static void Warn(string message, Exception ex) => Append("WARN", message, ex);

    /// <summary>Always-on error.</summary>
    public static void Error(string message) => Append("ERROR", message, null);

    /// <summary>Always-on error with the exception appended.</summary>
    public static void Error(string message, Exception ex) => Append("ERROR", message, ex);

    /// <summary>True when <paramref name="category"/> is enabled (and logging is on). Cheap — check it before
    /// building an expensive diag message.</summary>
    public static bool IsDiagEnabled(string category) =>
        sEnabled && (sEnabledCategories is null || sEnabledCategories.Contains(category));

    /// <summary>Append a gated diagnostic line tagged with <paramref name="category"/>; a no-op when that channel
    /// is off. Convention: keep <paramref name="message"/> short and structured as <c>key=value</c> pairs so grep
    /// filtering stays useful.</summary>
    public static void Diag(string category, string message)
    {
        if (!IsDiagEnabled(category)) return;
        Append("DIAG/" + category, message, null);
    }

    /// <summary>Mark the moment a Ctrl+N observation window opened; the matching END/CANCEL carries the same id.</summary>
    public static void UserObservationStart(string id) =>
        Append("USER_OBS_START", $"id={id} build={TryGetBuildVersion()}", null);

    /// <summary>Close an observation window: app-state snapshot (<paramref name="ctx"/>), screenshot path (empty
    /// when capture failed), and the user's notes. Newlines/quotes in notes are escaped so one observation stays
    /// one grep-friendly line; blank notes log as <c>(checkpoint)</c> — the all-okay gesture.</summary>
    public static void UserObservationEnd(string id, string ctx, string notes, string screenshotPath)
    {
        string bodyNotes = string.IsNullOrWhiteSpace(notes)
            ? "(checkpoint)"
            : notes
                .Replace("\\", "\\\\")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\"", "\\\"");
        Append("USER_OBS_END", $"id={id} ctx=({ctx}) screenshot={screenshotPath} notes=\"{bodyNotes}\"", null);
    }

    /// <summary>A manual mid-session screen capture from the open observation window. The line's local-time stamp
    /// plus the PNG filename's stamp let a sequence of capture → change UI → capture be ordered against the notes
    /// after the fact.</summary>
    public static void UserObservationCapture(string id, string path) =>
        Append("USER_OBS_CAP", $"id={id} screenshot={path}", null);

    /// <summary>Observation window abandoned (Cancel or close-X); every START gets a terminator.</summary>
    public static void UserObservationCancel(string id) => Append("USER_OBS_CANCEL", "id=" + id, null);

    private static void Append(string level, string message, Exception? ex)
    {
        if (!sEnabled || sPath.Length == 0) return;
        try
        {
            lock (sGate)
            {
                Directory.CreateDirectory(LogFolderPath);
                // Local time with offset ("o" keeps it sortable + unambiguous): the user reads these alongside
                // their own notes, and UTC rolls a day ahead during evening sessions.
                string body = ex is null
                    ? $"{DateTimeOffset.Now:o} {level} {message}{Environment.NewLine}"
                    : $"{DateTimeOffset.Now:o} {level} {message}: {ex}{Environment.NewLine}";
                File.AppendAllText(sPath, body);
            }
        }
        catch
        {
            // Best-effort — a logging failure must never escalate.
        }
    }

    // env var (comma list, or "*" = all) overrides the build default; unset → the identity's DiagDefault.
    private static HashSet<string>? ResolveEnabledCategories(AppLogIdentity identity)
    {
        string? env = Environment.GetEnvironmentVariable(identity.DiagEnvVar);
        if (env is not null)
        {
            string trimmed = env.Trim();
            if (trimmed == "*") return null;  // null sentinel = all
            return new HashSet<string>(
                trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }
        return identity.DiagDefault == DiagDefault.All
            ? null                                                      // all channels
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // none
    }

    private static string TryGetBuildVersion()
    {
        try
        {
            Assembly? asm = Assembly.GetEntryAssembly();
            string? infoVer = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer)) return infoVer;
            return asm?.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
