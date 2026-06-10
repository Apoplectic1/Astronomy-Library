using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// Pure planner (no database I/O) that turns a freshly built catalog into a <see cref="WriteBackPlan"/>: which TS
/// exposure-plan rows can be auto-set to the disk count, which need manual reconciliation, and which TS-level
/// issues to surface. Disk is the single source of truth, so every writable cell takes the disk count verbatim
/// (overwrite up or down). Scoped to <see cref="TargetSource.Both"/> targets; targets present on only one side
/// (disk xor TS) are counted in <see cref="WriteBackPlan.IgnoredMissing"/> and left untouched.
/// </summary>
public static class WriteBackPlanner
{
    /// <summary>Builds the write-back plan from catalog read-models plus the build report.</summary>
    public static WriteBackPlan Plan(
        IReadOnlyList<Target> targets,
        IReadOnlyList<ExposurePlan> plans,
        IReadOnlyList<ExposureTemplate> templates,
        IReadOnlyList<InventoryFilter> inventory,
        CatalogBuildReport report)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(report);

        Dictionary<Guid, Target> targetById = targets.ToDictionary(t => t.Id);
        Dictionary<Guid, ExposureTemplate> templateById = templates.ToDictionary(t => t.Id);
        HashSet<string> dupDirs = new(
            report.DuplicateTsTargets.Select(d => d.DiskDirectory), StringComparer.OrdinalIgnoreCase);

        // Alias folds (every TS name exactly matches a disk identity facet — same object): a cell carrying exactly
        // one plan per alias member is auto-written to every member, so they all reflect disk truth.
        Dictionary<string, int> aliasDirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (AliasTsTarget a in report.AliasTsTargets)
            aliasDirs[a.DiskDirectory] = a.TsTargetNames.Count;

        // Disk directories whose target identity is in question — a name mismatch (coords matched, names disagree)
        // or an ambiguous match (>1 disk target in tolerance). These are held for manual resolution, never
        // auto-written: a false-positive coordinate match would otherwise overwrite a real TS target's counts.
        HashSet<string> flaggedDirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (NameMismatch m in report.NameMismatches) flaggedDirs.Add(m.DiskDirectory);
        foreach (AmbiguousMatch a in report.AmbiguousMatches)
            foreach (string d in a.CandidateDirectories) flaggedDirs.Add(d);

        // Disk actuals summed per (target, filter, purpose). Filter compared case-insensitively (matches Reconciler).
        Dictionary<(Guid Target, string Filter, FilterPurpose Purpose), int> diskCount = new(KeyComparer.Instance);
        foreach (InventoryFilter f in inventory)
        {
            (Guid, string, FilterPurpose) key = (f.TargetId, f.FilterName, f.Purpose);
            diskCount[key] = diskCount.GetValueOrDefault(key) + f.ExposureCount;
        }

        // Group catalog plans by the write key, scoped to Both targets (others are missing-on-one-side -> ignored).
        Dictionary<(Guid Target, string Filter, FilterPurpose Purpose), List<ExposurePlan>> groups = new(KeyComparer.Instance);
        foreach (ExposurePlan p in plans)
        {
            if (!targetById.TryGetValue(p.TargetId, out Target? t) || t.Source != TargetSource.Both) continue;
            if (!templateById.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl)) continue;

            FilterPurpose purpose = FilterPurposeClassifier.Classify(tpl.Name);
            (Guid, string, FilterPurpose) key = (p.TargetId, tpl.FilterName, purpose);
            if (!groups.TryGetValue(key, out List<ExposurePlan>? list))
                groups[key] = list = [];
            list.Add(p);
        }

        List<PlannedWrite> writes = [];
        List<ManualGroup> manual = [];
        foreach (KeyValuePair<(Guid Target, string Filter, FilterPurpose Purpose), List<ExposurePlan>> g in groups)
        {
            (Guid targetId, string filter, FilterPurpose purpose) = g.Key;
            List<ExposurePlan> gplans = g.Value;
            Target t = targetById[targetId];
            int disk = diskCount.GetValueOrDefault(g.Key);
            bool flagged = t.DirectoryName is not null && flaggedDirs.Contains(t.DirectoryName);
            bool isMosaic = t.DirectoryName is not null && MosaicConvention.IsMosaicDirectory(t.DirectoryName);

            if (!flagged && !isMosaic && gplans.Count == 1 && TryParseTsId(gplans[0].ImportedFromTsGuid, out long id))
            {
                writes.Add(new PlannedWrite(id, targetId, t.Name, filter, purpose, disk));
            }
            else if (!flagged && !isMosaic
                && t.DirectoryName is not null && aliasDirs.TryGetValue(t.DirectoryName, out int members)
                && gplans.Count == members && TryParseTsIds(gplans, out List<long> ids))
            {
                // One plan per alias member on this cell — the fold explains the multiplicity exactly, so the
                // disk count goes to every member's plan. Any other count is a genuine same-purpose multi-plan.
                writes.AddRange(ids.Select(pid => new PlannedWrite(pid, targetId, t.Name, filter, purpose, disk)));
            }
            else
            {
                ManualReason reason =
                    flagged ? ManualReason.IdentityConflict
                    : isMosaic ? ManualReason.Mosaic
                    : t.DirectoryName is not null && dupDirs.Contains(t.DirectoryName) ? ManualReason.DuplicateFold
                    : ManualReason.MultiPlan;
                List<ManualPlan> mplans =
                [
                    .. gplans.Select(p => new ManualPlan(
                        TryParseTsId(p.ImportedFromTsGuid, out long pid) ? pid : -1,
                        p.AcquiredCount, p.AcceptedCount, p.DesiredCount)),
                ];
                manual.Add(new ManualGroup(targetId, t.Name, filter, purpose, disk, reason, mplans));
            }
        }

        List<ReconcileNote> needs = [];
        needs.AddRange(report.NameMismatches.Select(m => new ReconcileNote(
            "NameMismatch", m.TsName, $"disk '{m.DiskDirectory}' sep {m.SeparationDegrees:0.000} deg")));
        needs.AddRange(report.AmbiguousMatches.Select(a => new ReconcileNote(
            "Ambiguous", a.TsName, $"[{string.Join(" | ", a.CandidateDirectories)}] nearest {a.NearestSeparationDegrees:0.000} deg")));
        needs.AddRange(report.DuplicateTsTargets.Select(d => new ReconcileNote(
            "Duplicate", d.DiskDirectory, string.Join(" | ", d.TsTargetNames))));
        needs.AddRange(report.UnanchoredTsTargets.Select(u => new ReconcileNote(
            "Unanchored", u.TsName, "no usable coordinates")));
        needs.AddRange(report.InvalidTsTargets.Select(i => new ReconcileNote(
            "Invalid", i.TsName, i.Reason)));

        int ignoredMissing = report.PlannedOnlyCount + report.ActualOnlyCount;
        return new WriteBackPlan(writes, manual, needs, ignoredMissing);
    }

    // exposure_plan.imported_from_ts_guid always holds the TS exposureplan.Id as an invariant integer string
    // (TargetResolver), but parse defensively: a (never-expected) non-integer routes the cell to manual rather
    // than throwing mid-plan.
    private static bool TryParseTsId(string? importedFromTsGuid, out long id) =>
        long.TryParse(importedFromTsGuid, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private static bool TryParseTsIds(List<ExposurePlan> plans, out List<long> ids)
    {
        ids = new List<long>(plans.Count);
        foreach (ExposurePlan p in plans)
        {
            if (!TryParseTsId(p.ImportedFromTsGuid, out long id)) return false;
            ids.Add(id);
        }
        return true;
    }

    private sealed class KeyComparer : IEqualityComparer<(Guid Target, string Filter, FilterPurpose Purpose)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((Guid Target, string Filter, FilterPurpose Purpose) a, (Guid Target, string Filter, FilterPurpose Purpose) b) =>
            a.Target.Equals(b.Target) && a.Purpose == b.Purpose
            && string.Equals(a.Filter, b.Filter, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid Target, string Filter, FilterPurpose Purpose) k) =>
            HashCode.Combine(k.Target, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Filter), k.Purpose);
    }
}
