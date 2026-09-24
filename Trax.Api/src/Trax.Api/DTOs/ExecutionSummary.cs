using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

public record ExecutionSummary(
    long Id,
    string ExternalId,
    string Name,
    TrainState TrainState,
    DateTime StartTime,
    DateTime? EndTime,
    string? FailureJunction,
    string? FailureReason,
    long? ManifestId,
    bool CancellationRequested,
    string? HostName = null,
    string? HostEnvironment = null,
    string? HostInstanceId = null,
    Trax.Core.Exceptions.FailureClass FailureClass = Trax.Core.Exceptions.FailureClass.Unclassified
);
