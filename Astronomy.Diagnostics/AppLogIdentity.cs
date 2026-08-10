using System.Reflection;

namespace Astronomy.Diagnostics;

/// <summary>
/// Per-app configuration for <see cref="Log"/>: the app's <see cref="AppName"/> (which becomes the log area
/// <c>%APPDATA%\&lt;AppName&gt;\Logs\</c> unless <see cref="RootOverride"/> redirects it), its
/// <see cref="LogFileName"/>, the environment variable that toggles diagnostic channels
/// (<see cref="DiagEnvVar"/>), and the build-derived <see cref="DiagDefault"/>. The consumer passes the diag
/// default because a shared library compiles once and so cannot read the consumer's own <c>#if DEBUG</c>.
/// <see cref="Enabled"/> = <c>false</c> silences all logging.
///
/// <para><see cref="VersionAssembly"/> is the assembly whose informational version stamps session lines
/// (<c>build=…</c>). Leave <c>null</c> for a standalone app — the entry assembly is the app. A consumer
/// hosted inside another process (a plugin) MUST pass its own assembly (e.g.
/// <c>typeof(MyPlugin).Assembly</c>), because there the entry assembly is the host executable and the
/// stamp would report the host's version, not the consumer's.</para>
/// </summary>
public sealed record AppLogIdentity(
    string AppName,
    string LogFileName,
    string DiagEnvVar,
    DiagDefault DiagDefault,
    bool Enabled = true,
    string? RootOverride = null,
    Assembly? VersionAssembly = null);

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
