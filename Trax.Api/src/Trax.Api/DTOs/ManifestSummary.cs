using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// A manifest as the list and point reads return it. Carries no <c>Properties</c>: the train
/// input a manifest holds is on <see cref="ManifestDetail"/> alone, the way an execution's input
/// is on <see cref="ExecutionDetail"/> and not on <see cref="ExecutionSummary"/>.
/// </summary>
/// <param name="Id">The manifest's database id.</param>
/// <param name="ExternalId">The manifest's stable external id.</param>
/// <param name="Name">The name of the train the manifest runs.</param>
/// <param name="IsEnabled">Whether the scheduler will run the manifest.</param>
/// <param name="ScheduleType">How the manifest is scheduled.</param>
/// <param name="CronExpression">The cron expression, for a cron-scheduled manifest.</param>
/// <param name="IntervalSeconds">The interval in seconds, for an interval-scheduled manifest.</param>
/// <param name="MaxRetries">How many times a failed run is retried before it is dead-lettered.</param>
/// <param name="TimeoutSeconds">The run timeout in seconds, or <c>null</c> for none.</param>
/// <param name="LastSuccessfulRun">When the manifest last ran successfully (UTC).</param>
/// <param name="ManifestGroupId">The id of the manifest group it belongs to.</param>
/// <param name="DependsOnManifestId">The manifest this one runs after, for a dependent manifest.</param>
/// <param name="Priority">The dispatch priority of the work it queues.</param>
/// <param name="ManifestGroupName">The name of the manifest group it belongs to.</param>
public record ManifestSummary(
    long Id,
    string ExternalId,
    string Name,
    bool IsEnabled,
    ScheduleType ScheduleType,
    string? CronExpression,
    int? IntervalSeconds,
    int MaxRetries,
    int? TimeoutSeconds,
    DateTime? LastSuccessfulRun,
    long ManifestGroupId,
    long? DependsOnManifestId,
    int Priority,
    string? ManifestGroupName = null
);
