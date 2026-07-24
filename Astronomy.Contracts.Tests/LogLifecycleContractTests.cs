using Astronomy.Diagnostics;
using Xunit;

namespace Astronomy.Contracts.Tests;

/// <summary>
/// Contract test for CONSUMERS.md "Semantic assumptions" #6 — <c>Log.Init</c> →
/// <c>Log.StartNewSession</c> must precede any <c>Log.*</c>, else the call is a SILENT NO-OP
/// (no throw, no file, no path): a consumer that mis-orders startup loses its forensic trail
/// without any error to notice. <c>Log</c> is process-global static state, so the pre-init and
/// post-init phases live in ONE test method (deterministic ordering), and every bench class that
/// touches <c>Log</c> AT ALL — even calls that are no-ops pre-Init — must join the
/// <c>"LogProcessGlobal"</c> collection so it serializes with this one: once phase 2's
/// <c>Init</c> lands, a parallel class's "no-op" log call becomes a live append into this test's
/// temp file and its writer handle races <c>File.ReadAllText</c> below (observed 2026-07-24 as an
/// intermittent <c>IOException</c> from <c>ObservationSessionContractTests</c> running alongside).
/// An <c>Init</c> anywhere else in this process would invalidate the pre-init phase outright.
/// </summary>
[Collection("LogProcessGlobal")]
public sealed class LogLifecycleContractTests
{
    [Fact]
    public void PreInit_LogCalls_AreSilentNoOps_ThenInitEnablesWriting()
    {
        // ---- phase 1: before Init — every severity call must be a silent no-op ----------------
        Log.Info("pre-init info");        // must not throw
        Log.Warn("pre-init warn");
        Log.Error("pre-init error");
        Log.Diag("Channel", "pre-init diag");

        Assert.Equal(string.Empty, Log.FilePath);        // no path resolved…
        Assert.Equal(string.Empty, Log.LogFolderPath);   // …no folder derived — nothing was written anywhere.

        // ---- phase 2: positive control — Init + StartNewSession makes the same calls write ----
        // (Proves phase 1 exercised the lifecycle gate, not a globally disabled log.)
        string root = Path.Combine(Path.GetTempPath(), $"log_contract_{Guid.NewGuid():N}");
        try
        {
            Log.Init(new AppLogIdentity(
                AppName: "ContractsBench", LogFileName: "bench.log", DiagEnvVar: "CONTRACTS_BENCH_DIAG",
                DiagDefault: DiagDefault.None, Enabled: true, RootOverride: root));
            Log.StartNewSession();
            Log.Info("post-init info");

            Assert.True(File.Exists(Log.FilePath));
            Assert.Contains("post-init info", File.ReadAllText(Log.FilePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup of throwaway test artifacts */ }
        }
    }
}
