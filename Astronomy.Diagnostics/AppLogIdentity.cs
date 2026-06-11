namespace Astronomy.Diagnostics;

/// <summary>
/// Per-app configuration for <see cref="Log"/>: the app's <see cref="AppName"/> (which becomes the log area
/// <c>%APPDATA%\&lt;AppName&gt;\Logs\</c> unless <see cref="RootOverride"/> redirects it), its
/// <see cref="LogFileName"/>, the environment variable that toggles diagnostic channels
/// (<see cref="DiagEnvVar"/>), and the build-derived <see cref="DiagDefault"/>. The consumer passes the diag
/// default because a shared library compiles once and so cannot read the consumer's own <c>#if DEBUG</c>.
/// <see cref="Enabled"/> = <c>false</c> silences all logging.
/// </summary>
public sealed record AppLogIdentity(
    string AppName,
    string LogFileName,
    string DiagEnvVar,
    DiagDefault DiagDefault,
    bool Enabled = true,
    string? RootOverride = null);

/// <summary>Default diagnostic-channel verbosity when the env var is unset — by convention <see cref="All"/> in
/// Debug and <see cref="None"/> in Release. The always-on severity lines (Info/Warn/Error) write regardless;
/// only the gated <c>Diag</c> channels are governed by this. See <see cref="Log"/>.</summary>
public enum DiagDefault
{
    /// <summary>Every channel on (the Debug convention).</summary>
    All,

    /// <summary>No channels — only the always-on Info/Warn/Error severity lines (the Release convention).</summary>
    None,
}
