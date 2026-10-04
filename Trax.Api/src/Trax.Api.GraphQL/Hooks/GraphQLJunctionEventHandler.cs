using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Forwards junction events, the steps of a run, to the <c>onJunctionEvent</c> subscription. Called
/// on the run's path for a run on this host, and by the broadcaster's receiver for a run on another.
/// </summary>
/// <remarks>
/// A step is forwarded exactly when the train's own events are (<see cref="LifecycleStreamRule"/>):
/// for a <c>[TraxBroadcast]</c> train, or for every train on a host exposing the operations surface.
/// Which subscribers receive it is decided per subscriber when it is read, by the same visibility as
/// the train's events. It publishes to the in-memory topic and returns, and never waits longer than
/// <see cref="PublishBound"/>: a step that cannot be published in time is dropped and reported to
/// subscribers as a gap, rather than holding up the run.
/// </remarks>
internal sealed class GraphQLJunctionEventHandler(
    ITopicEventSender eventSender,
    LifecycleStreamRule rule,
    ILogger<GraphQLJunctionEventHandler>? logger = null
) : IJunctionEventHandler
{
    /// <summary>How long a step may wait to be published before it is dropped as a loss.</summary>
    internal static readonly TimeSpan DefaultPublishBound = TimeSpan.FromMilliseconds(250);

    private readonly LifecycleEventPublisher _publisher = LifecycleEventPublisher.For(eventSender);

    /// <summary>
    /// The most a step's publish holds up the run. Past it the step is dropped and subscribers see
    /// the loss as a skip in <c>sequence</c>.
    /// </summary>
    internal TimeSpan PublishBound { get; init; } = DefaultPublishBound;

    /// <inheritdoc />
    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (!TrainLifecycleEventMessage.IsJunctionEvent(message.EventType))
            return;

        if (!rule.Publishes(message.TrainName))
            return;

        if (JunctionEvent.From(message) is not { } junctionEvent)
        {
            logger?.LogWarning(
                "Dropping {EventType} for train {TrainName}: not a junction event this API knows.",
                message.EventType,
                message.TrainName
            );
            return;
        }

        await _publisher
            .PublishAsync(
                nameof(LifecycleSubscriptions.OnJunctionEvent),
                junctionEvent,
                PublishBound,
                ct
            )
            .ConfigureAwait(false);
    }
}
