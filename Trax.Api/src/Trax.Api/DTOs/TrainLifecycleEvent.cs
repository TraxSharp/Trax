using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// Event payload published by lifecycle hooks and consumed by GraphQL subscriptions.
/// </summary>
public record TrainLifecycleEvent(
    long MetadataId,
    string ExternalId,
    string TrainName,
    TrainState TrainState,
    DateTime Timestamp,
    string? FailureStep,
    string? FailureReason,
    string? Output
);
