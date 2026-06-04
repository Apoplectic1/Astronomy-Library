using Astronomy.Catalog.Data;
using Microsoft.Data.Sqlite;

namespace Astronomy.Catalog.Schema;

/// <summary>Maps <c>profile</c> rows.</summary>
public sealed class ProfileMapper : ITableMapper<Profile>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly ProfileMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "profile";

    /// <inheritdoc/>
    public Profile Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetString("name"),
        reader.GetStringOrNull("nina_profile_guid"),
        reader.GetInt64("created_at"));
}

/// <summary>Maps <c>project</c> rows.</summary>
public sealed class ProjectMapper : ITableMapper<Project>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly ProjectMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "project";

    /// <inheritdoc/>
    public Project Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetGuid("profile_id"),
        reader.GetString("name"),
        reader.GetStringOrNull("description"),
        (ProjectState)reader.GetInt32("state_id"),
        (ProjectPriority)reader.GetInt32("priority_id"),
        reader.GetDoubleOrNull("minimum_altitude_deg"),
        reader.GetDoubleOrNull("maximum_altitude_deg"),
        reader.GetInt32OrNull("minimum_time_minutes"),
        reader.GetBoolean("use_custom_horizon"),
        reader.GetDoubleOrNull("horizon_offset_deg"),
        reader.GetInt32OrNull("meridian_window_minutes"),
        reader.GetBoolean("is_mosaic"),
        reader.GetBoolean("enable_grader"),
        reader.GetInt64("created_at"),
        reader.GetInt64OrNull("active_at"),
        reader.GetInt64OrNull("inactive_at"),
        reader.GetStringOrNull("imported_from_ts_guid"));
}

/// <summary>Maps <c>target</c> rows.</summary>
public sealed class TargetMapper : ITableMapper<Target>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly TargetMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "target";

    /// <inheritdoc/>
    public Target Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetGuid("project_id"),
        reader.GetString("name"),
        reader.GetBoolean("enabled"),
        reader.GetDoubleOrNull("ra_hours"),
        reader.GetDoubleOrNull("dec_degrees_signed"),
        (Epoch)reader.GetInt32("epoch_id"),
        reader.GetDoubleOrNull("rotation_deg"),
        reader.GetDoubleOrNull("roi_percent"),
        reader.GetInt32OrNull("priority_id") is int p ? (ProjectPriority)p : null,
        reader.GetInt64("created_at"),
        reader.GetStringOrNull("imported_from_ts_guid"));
}

/// <summary>Maps <c>exposure_template</c> rows.</summary>
public sealed class ExposureTemplateMapper : ITableMapper<ExposureTemplate>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly ExposureTemplateMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "exposure_template";

    /// <inheritdoc/>
    public ExposureTemplate Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetGuid("profile_id"),
        reader.GetString("name"),
        reader.GetString("filter_name"),
        reader.GetInt32OrNull("gain"),
        reader.GetInt32OrNull("offset_adu"),
        reader.GetInt32OrNull("binning"),
        reader.GetInt32OrNull("readout_mode"),
        reader.GetDoubleOrNull("default_exposure_seconds"),
        reader.GetStringOrNull("imported_from_ts_guid"));
}

/// <summary>Maps <c>exposure_plan</c> rows.</summary>
public sealed class ExposurePlanMapper : ITableMapper<ExposurePlan>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly ExposurePlanMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "exposure_plan";

    /// <inheritdoc/>
    public ExposurePlan Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetGuid("target_id"),
        reader.GetGuid("exposure_template_id"),
        reader.GetDoubleOrNull("exposure_seconds"),
        reader.GetInt32("desired_count"),
        reader.GetInt32("acquired_count"),
        reader.GetInt32("accepted_count"),
        reader.GetBoolean("enabled"),
        reader.GetStringOrNull("imported_from_ts_guid"));
}

/// <summary>Maps <c>image_file</c> rows.</summary>
public sealed class ImageFileMapper : ITableMapper<ImageFile>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly ImageFileMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "image_file";

    /// <inheritdoc/>
    public ImageFile Map(SqliteDataReader reader) => new(
        reader.GetGuid("id"),
        reader.GetString("path"),
        reader.GetGuidOrNull("target_id"),
        reader.GetStringOrNull("target_name"),
        reader.GetStringOrNull("filter_name"),
        reader.GetInt32OrNull("frame_type_id") is int ft ? (FrameType)ft : null,
        reader.GetInt32OrNull("processing_stage_id") is int ps ? (ProcessingStage)ps : null,
        reader.GetDoubleOrNull("exposure_seconds"),
        reader.GetInt64OrNull("captured_at"),
        reader.GetStringOrNull("camera"),
        reader.GetInt32OrNull("gain"),
        reader.GetInt32OrNull("offset_adu"),
        reader.GetDoubleOrNull("ra_hours"),
        reader.GetDoubleOrNull("dec_degrees_signed"),
        reader.GetInt64("file_mtime"),
        reader.GetInt64("file_size"),
        reader.GetInt64("scanned_at"));
}

/// <summary>Maps rows of the <c>inventory_rollup</c> view.</summary>
public sealed class InventoryRollupMapper : ITableMapper<InventoryRollupRow>
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly InventoryRollupMapper Instance = new();

    /// <inheritdoc/>
    public string TableName => "inventory_rollup";

    /// <inheritdoc/>
    public InventoryRollupRow Map(SqliteDataReader reader) => new(
        reader.GetGuidOrNull("target_id"),
        reader.GetStringOrNull("target_name"),
        reader.GetStringOrNull("filter_name"),
        reader.GetInt32OrNull("processing_stage_id") is int ps ? (ProcessingStage)ps : null,
        reader.GetInt32("frame_count"),
        reader.GetDouble("total_integration_seconds"),
        reader.GetInt64OrNull("first_captured_at"),
        reader.GetInt64OrNull("last_captured_at"));
}
