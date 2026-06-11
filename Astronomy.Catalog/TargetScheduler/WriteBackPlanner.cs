using System.Globalization;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;

namespace Astronomy.Catalog.TargetScheduler;

/// <summary>
/// Pure planner (no database I/O) that turns a freshly built catalog into a <see cref="WriteBackPlan"/>: which TS
/// exposure-plan rows can be auto-set to the disk count, which need manual reconciliation, and which TS-level
/// issues to surface. The write key is <b>(target, filter, purpose, whole-second exposure)</b> — a plan's
/// effective sub length is its spec, so it receives the count of disk frames at exactly that duration (0 when
/// none exist: an unmet spec, written as a flagged decrease). Disk is the single source of truth, so every
/// writable cell takes its bucket's count verbatim (overwrite up or down). Disk buckets no plan targets are
/// surfaced as <see cref="ReconcileNote.UnplannedFramesKind"/> notes — write-back updates existing plan rows
/// only, never creates or deletes plans. Scoped to <see cref="TargetSource.Both"/> targets; targets present on
/// only one side (disk xor TS) are counted in <see cref="WriteBackPlan.IgnoredMissing"/> and left untouched.
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

        // Disk actuals summed per (target, filter, purpose, seconds). Filter compared case-insensitively
        // (matches Reconciler); the scanner's whole-second exposure bucket is part of the key.
        Dictionary<(Guid Target, string Filter, FilterPurpose Purpose, int Seconds), int> diskCount = new(KeyComparer.Instance);
        foreach (InventoryFilter f in inventory)
        {
            (Guid, string, FilterPurpose, int) key = (f.TargetId, f.FilterName, f.Purpose, (int)Math.Round(f.ExposureSeconds));
            diskCount[key] = diskCount.GetValueOrDefault(key) + f.ExposureCount;
        }

        // Group catalog plans by the write key, scoped to Both targets (others are missing-on-one-side -> ignored).
        // Same-purpose plans at DIFFERENT sub lengths land in different groups and auto-resolve against their own
        // disk buckets — only same-key multiplicity remains a manual case.
        Dictionary<(Guid Target, string Filter, FilterPurpose Purpose, int Seconds), List<ExposurePlan>> groups = new(KeyComparer.Instance);
        foreach (ExposurePlan p in plans)
        {
            if (!targetById.TryGetValue(p.TargetId, out Target? t) || t.Source != TargetSource.Both) continue;
            if (!templateById.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl)) continue;

            FilterPurpose purpose = FilterPurposeClassifier.Classify(tpl.Name);
            (Guid, string, FilterPurpose, int) key = (p.TargetId, tpl.FilterName, purpose, EffectiveExposure.Seconds(p, tpl));
            if (!groups.TryGetValue(key, out List<ExposurePlan>? list))
                groups[key] = list = [];
            list.Add(p);
        }

        List<PlannedWrite> writes = [];
        List<ManualGroup> manual = [];
        foreach (KeyValuePair<(Guid Target, string Filter, FilterPurpose Purpose, int Seconds), List<ExposurePlan>> g in groups)
        {
            (Guid targetId, string filter, FilterPurpose purpose, int seconds) = g.Key;
            List<ExposurePlan> gplans = g.Value;
            Target t = targetById[targetId];
            int disk = diskCount.GetValueOrDefault(g.Key);   // 0 when no frames at this duration: spec unmet

            // Identity-flagged directories (name mismatch / ambiguous match — panels' composite names land in
            // those reports like any other unit's) are held for manual resolution, never auto-written: a
            // false-positive match would otherwise overwrite a real TS target's counts.
            bool flagged = report.IsIdentityFlagged(t.DirectoryName);

            // Mosaic panels are ordinary Both targets here (own TS provenance, own inventory); the mosaic
            // PARENT carries no plans and no inventory, so it never forms a group — inert by construction.
            if (!flagged && gplans.Count == 1 && TryParseTsId(gplans[0].ImportedFromTsGuid, out long id))
            {
                writes.Add(new PlannedWrite(id, targetId, t.Name, filter, purpose, seconds, disk));
            }
            else if (!flagged
                && report.AliasMemberCount(t.DirectoryName) is int members && members > 0
                && gplans.Count == members && TryParseTsIds(gplans, out List<long> ids))
            {
                // One plan per alias member on this cell — the fold explains the multiplicity exactly, so the
                // disk count goes to every member's plan. Any other count is a genuine same-purpose multi-plan.
                // (Alias members whose plans differ in sub length grouped separately and auto-write above.)
                writes.AddRange(ids.Select(pid => new PlannedWrite(pid, targetId, t.Name, filter, purpose, seconds, disk)));
            }
            else
            {
                ManualReason reason =
                    flagged ? ManualReason.IdentityConflict
                    : report.IssuesFor(t.DirectoryName).HasFlag(TargetMatchIssues.Duplicate) ? ManualReason.DuplicateFold
                    : ManualReason.MultiPlan;
                List<ManualPlan> mplans =
                [
                    .. gplans.Select(p => new ManualPlan(
                        TryParseTsId(p.ImportedFromTsGuid, out long pid) ? pid : -1,
                        templateById.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? ptpl)
                            ? EffectiveExposure.Seconds(p, ptpl) : seconds,
                        p.AcquiredCount, p.AcceptedCount, p.DesiredCount)),
                ];
                manual.Add(new ManualGroup(targetId, t.Name, filter, purpose, seconds, disk, reason, mplans));
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

        // Disk buckets no plan targets (frames at a duration with no plan at that exact key): informational
        // only — plan creation/deletion is out of write-back's contract, so nothing is written and nothing
        // goes to manual. Scoped to Both targets; one-sided targets stay in IgnoredMissing.
        List<ReconcileNote> unplanned = [];
        foreach (KeyValuePair<(Guid Target, string Filter, FilterPurpose Purpose, int Seconds), int> kv in diskCount)
        {
            if (groups.ContainsKey(kv.Key)) continue;
            if (!targetById.TryGetValue(kv.Key.Target, out Target? t) || t.Source != TargetSource.Both) continue;
            unplanned.Add(new ReconcileNote(ReconcileNote.UnplannedFramesKind, t.Name,
                $"{kv.Key.Filter} {kv.Key.Purpose} {kv.Value} frames @{kv.Key.Seconds}s - no TS plan at {kv.Key.Seconds}s"));
        }
        needs.AddRange(unplanned
            .OrderBy(n => n.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Detail, StringComparer.Ordinal));

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

    private sealed class KeyComparer : IEqualityComparer<(Guid Target, string Filter, FilterPurpose Purpose, int Seconds)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals(
            (Guid Target, string Filter, FilterPurpose Purpose, int Seconds) a,
            (Guid Target, string Filter, FilterPurpose Purpose, int Seconds) b) =>
            a.Target.Equals(b.Target) && a.Purpose == b.Purpose && a.Seconds == b.Seconds
            && string.Equals(a.Filter, b.Filter, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid Target, string Filter, FilterPurpose Purpose, int Seconds) k) =>
            HashCode.Combine(k.Target, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Filter), k.Purpose, k.Seconds);
    }
}
