using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.Build;

/// <summary>
/// The fully-resolved in-memory catalog produced by <see cref="TargetResolver"/> and written atomically by
/// <see cref="CatalogStore.WriteCatalog"/>. The lists are already in foreign-key insert order: profiles → projects
/// → templates → targets → plans → inventory. Every <see cref="ExposurePlan.TargetId"/> and
/// <see cref="InventoryFilter.TargetId"/> points at a <see cref="Target"/> in <see cref="Targets"/>.
/// </summary>
public sealed record CatalogGraph(
    IReadOnlyList<Profile> Profiles,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<ExposureTemplate> Templates,
    IReadOnlyList<Target> Targets,
    IReadOnlyList<ExposurePlan> Plans,
    IReadOnlyList<InventoryFilter> InventoryFilters);
