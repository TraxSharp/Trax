using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

public record ExecutionSummary(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    string? FailureStep,
    string? FailureReason,
    long? ManifestId,
    bool CancellationRequested,
    string? HostName = null,
    string? HostEnvironment = null,
    string? HostInstanceId = null
);
