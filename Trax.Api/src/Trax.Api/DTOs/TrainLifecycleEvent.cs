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

    /// <summary>
    /// This event's position in the subscription that delivered it: 1 for the first event, and one
    /// more for each event after it, unless events were lost in between, in which case it is two
    /// more. A client that sees a value other than the previous one plus one has missed events and
    /// should refetch what it shows. 0 outside a lifecycle subscription.
    /// </summary>
    /// <remarks>
    /// The live feed is lossy by design: a subscriber that falls behind loses its oldest buffered
    /// events. The jump is always two, whatever was lost, so a subscriber learns that it missed
    /// something but not how much activity there was in trains it may not see.
    /// </remarks>
    public long Sequence { get; init; }

    /// <summary>
    /// The number the publisher gave this event on its topic, or 0 when it was sent without one.
    /// Not part of the GraphQL type: the subscription compares it with the previous event's to
    /// detect a loss, and reports that through <see cref="Sequence"/>.
    /// </summary>
    internal long PublishSequence { get; init; }
}
