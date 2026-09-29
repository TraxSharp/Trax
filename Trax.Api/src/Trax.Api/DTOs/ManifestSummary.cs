using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// A manifest as the list and point reads return it. Carries no <c>Properties</c>: the train
/// input a manifest holds is on <see cref="ManifestDetail"/> alone, the way an execution's input
/// is on <see cref="ExecutionDetail"/> and not on <see cref="ExecutionSummary"/>.
/// </summary>
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
