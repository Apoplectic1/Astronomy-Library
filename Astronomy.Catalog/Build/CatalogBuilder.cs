using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;

namespace Astronomy.Catalog.Build;

/// <summary>
/// Builds (full rebuild) a <c>Catalog.db</c> from its two sources: the image library on disk (ACTUAL) and the
/// N.I.N.A. Target Scheduler database (PLANNED). Because the catalog is fully derived, every build clears and
/// rewrites the whole graph in one transaction — there is no incremental/merge path to keep correct. Either source
/// may be omitted: library only → actuals-only catalog; TS only → planned-only catalog; both → reconciled.
/// </summary>
public static class CatalogBuilder
{
    /// <summary>
    /// Scans <paramref name="libraryRoot"/>, reads <paramref name="targetSchedulerDbPath"/>, resolves them onto one
    /// canonical target set, and writes <paramref name="catalogPath"/>. Returns the <see cref="CatalogBuildReport"/>
    /// (counts + TS reconciliation issues).
    /// </summary>
    /// <param name="catalogPath">Destination <c>Catalog.db</c> (created/overwritten).</param>
    /// <param name="libraryRoot">Image library root to scan, or <see langword="null"/>/empty to skip the disk plane.</param>
    /// <param name="targetSchedulerDbPath">TS <c>schedulerdb.sqlite</c> to import, or <see langword="null"/>/empty to skip the plan plane.</param>
    /// <param name="options">Resolver match tolerance; defaults to <see cref="ResolveOptions.Default"/>.</param>
    /// <param name="ct">Cancellation token, observed during the disk scan.</param>
    public static async Task<CatalogBuildReport> BuildAsync(
        string catalogPath,
        string? libraryRoot,
        string? targetSchedulerDbPath,
        ResolveOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        IReadOnlyList<TargetReport> diskTargets = [];
        if (!string.IsNullOrWhiteSpace(libraryRoot))
        {
            ImageLibraryReport scan = await ImageLibraryScanner.ScanAsync(libraryRoot, ct).ConfigureAwait(false);
            diskTargets = scan.Targets;
        }

        TsPlanData ts = TsPlanData.Empty;
        if (!string.IsNullOrWhiteSpace(targetSchedulerDbPath))
        {
            using TargetSchedulerReader reader = new(targetSchedulerDbPath);
            ts = reader.ReadPlanData();
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        (CatalogGraph graph, CatalogBuildReport report) = TargetResolver.Resolve(diskTargets, ts, now, options);

        using CatalogStore store = CatalogStore.Open(catalogPath);
        store.WriteCatalog(graph);
        return report;
    }
}
