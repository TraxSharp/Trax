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
    string? FailureJunction,
    string? FailureReason,
    string? Output,
    string? HostName = null,
    string? HostEnvironment = null
)
{
    /// <summary>
    /// The type name of the exception the train failed with, as recorded on its metadata. Not
    /// part of the GraphQL type: it decides whether a subscriber outside the operations view may
    /// see <see cref="FailureReason"/>. <c>null</c> when unknown, which withholds the reason.
    /// </summary>
    public string? FailureException { get; init; }
}
