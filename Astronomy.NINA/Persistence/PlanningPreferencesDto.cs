namespace Astronomy.NINA.Persistence;

/// <summary>
/// Cross-app per-site planning preferences DTO. Flat POCO + parameterless ctor +
/// public settable properties so any JSON serializer round-trips without a custom
/// converter. Consumer apps map this to their own in-memory planning-preferences
/// record (e.g. TargetPlanner's <c>State.PlanningPreferences</c>).
/// </summary>
public sealed class PlanningPreferencesDto
{
    /// <summary>Minimum target altitude in degrees -- the &quot;H&quot; of HMD planning.</summary>
    public double TargetFloorDeg { get; set; }

    /// <summary>Minimum contiguous-window duration in minutes -- the &quot;D&quot; of HMD planning.</summary>
    public double MinDurationMinutes { get; set; }
}
