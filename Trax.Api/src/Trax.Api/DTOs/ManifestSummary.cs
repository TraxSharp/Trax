using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

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
    int Priority
);
