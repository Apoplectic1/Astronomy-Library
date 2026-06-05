namespace Astronomy.Catalog.Schema;

/// <summary>Project lifecycle state. Mirrors the <c>project_state</c> lookup table and TS's <c>ProjectState</c>.</summary>
public enum ProjectState
{
    /// <summary>Draft, not yet activated (id 0).</summary>
    Draft = 0,
    /// <summary>Active and schedulable (id 1).</summary>
    Active = 1,
    /// <summary>Temporarily inactive (id 2).</summary>
    Inactive = 2,
    /// <summary>Completed/closed (id 3).</summary>
    Closed = 3,
}

/// <summary>Scheduling priority. Mirrors the <c>project_priority</c> lookup table and TS's <c>ProjectPriority</c>.</summary>
public enum ProjectPriority
{
    /// <summary>Low priority (id 0).</summary>
    Low = 0,
    /// <summary>Normal priority (id 1).</summary>
    Normal = 1,
    /// <summary>High priority (id 2).</summary>
    High = 2,
}

/// <summary>Coordinate epoch. Mirrors the <c>epoch</c> lookup table and TS's <c>epochcode</c> (J2000 = 2).</summary>
public enum Epoch
{
    /// <summary>B1950 (id 0).</summary>
    B1950 = 0,
    /// <summary>Equinox of date / JNow (id 1).</summary>
    JNow = 1,
    /// <summary>J2000 (id 2) — the near-universal value.</summary>
    J2000 = 2,
}

/// <summary>How a target entered the catalog. Mirrors the <c>target_source</c> lookup table.</summary>
public enum TargetSource
{
    /// <summary>Shot on disk, with no matching TS plan (id 0) — actuals only.</summary>
    Actual = 0,
    /// <summary>Planned in TS but not yet shot on disk (id 1) — goals only, 0 actual.</summary>
    Planned = 1,
    /// <summary>Both planned (TS) and shot (disk) — the two resolved onto one canonical target (id 2).</summary>
    Both = 2,
}

// Light/Stars is the scanner's Astronomy.Catalog.Scan.FilterPurpose (id-aligned with the frame_purpose lookup:
// Light = 0, Stars = 1) — no separate catalog enum needed.
