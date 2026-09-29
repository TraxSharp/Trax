using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Full detail for a single manifest: everything <see cref="ManifestSummary"/> carries plus the
/// train input it runs with (<see cref="Properties"/>) and the rest of its scheduling settings.
/// </summary>
/// <remarks>
/// <see cref="Properties"/> is the serialized train input and can hold anything the train takes,
/// credentials included. It follows the rule an execution's input follows: it appears on the
/// single-row detail read only, never on a list, and only under the <c>operations</c> namespace,
/// so the host's operations gate decides who reads it.
/// </remarks>
public record ManifestDetail(
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
    string ManifestGroupName,
    long? DependsOnManifestId,
    int Priority,
    string? PropertyTypeName,
    string? Properties,
    MisfirePolicy MisfirePolicy,
    int? MisfireThresholdSeconds,
    DateTime? ScheduledAt,
    DateTime? NextScheduledRun,
    int? VarianceSeconds
);
