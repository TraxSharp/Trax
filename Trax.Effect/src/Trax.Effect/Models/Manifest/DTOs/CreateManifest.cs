using Trax.Effect.Enums;

namespace Trax.Effect.Models.Manifest.DTOs;

/// <summary>
/// Data transfer object for creating a new Manifest.
/// </summary>
public class CreateManifest
{
    /// <summary>
    /// The workflow type to execute. Must implement IEffectWorkflow.
    /// </summary>
    public required Type Name { get; set; }

    /// <summary>
    /// Optional properties/configuration for the workflow.
    /// </summary>
    public IManifestProperties? Properties { get; set; }

    #region Scheduling Properties

    /// <summary>
    /// Whether the manifest is enabled for scheduling. Defaults to true.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The scheduling strategy. Defaults to None (manual trigger only).
    /// </summary>
    public ScheduleType ScheduleType { get; set; } = ScheduleType.None;

    /// <summary>
    /// Cron expression for Cron-type schedules.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Interval in seconds for Interval-type schedules.
    /// </summary>
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// Maximum retry attempts before dead-lettering. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout in seconds for job execution. Null uses the global default.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// The ID of the parent manifest this manifest depends on.
    /// Used with <see cref="ScheduleType.Dependent"/>.
    /// </summary>
    public long? DependsOnManifestId { get; set; }

    /// <summary>
    /// Default dispatch priority for work queue entries. Range: 0-31. Defaults to 0.
    /// </summary>
    public int Priority { get; set; }

    #endregion
}
