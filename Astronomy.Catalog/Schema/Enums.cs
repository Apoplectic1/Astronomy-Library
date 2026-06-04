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

/// <summary>Image frame type. Mirrors the <c>frame_type</c> lookup table.</summary>
public enum FrameType
{
    /// <summary>Light/science frame (id 0).</summary>
    Light = 0,
    /// <summary>Dark calibration frame (id 1).</summary>
    Dark = 1,
    /// <summary>Flat calibration frame (id 2).</summary>
    Flat = 2,
    /// <summary>Bias calibration frame (id 3).</summary>
    Bias = 3,
}

/// <summary>Where a scanned image sits in the processing pipeline. Mirrors the <c>processing_stage</c> lookup table.</summary>
public enum ProcessingStage
{
    /// <summary>Raw capture (id 0).</summary>
    Captures = 0,
    /// <summary>Calibrated (id 1).</summary>
    Calibrated = 1,
    /// <summary>Cosmetically corrected (id 2).</summary>
    Cosmetized = 2,
    /// <summary>Debayered (id 3).</summary>
    Debayered = 3,
    /// <summary>Master calibration frame (id 4).</summary>
    Master = 4,
    /// <summary>Final integration (id 5).</summary>
    Integrated = 5,
}
