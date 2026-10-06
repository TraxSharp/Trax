using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Handles cross-process train lifecycle events received via the broadcaster
/// and forwards them to HotChocolate's in-memory subscription transport.
/// This bridges the gap between worker processes (where trains execute)
/// and hub processes (where GraphQL subscriptions live).
/// Only trains decorated with <c>[TraxBroadcast]</c> have their events forwarded, unless the
/// operations surface is exposed, in which case every train is (see
/// <see cref="TrainLifecycleStreamOptions"/>).
/// </summary>
public class GraphQLTrainEventHandler : ITrainEventHandler
{
    private readonly LifecycleEventPublisher _publisher;
    private readonly ILogger<GraphQLTrainEventHandler>? _logger;
    private readonly LifecycleStreamRule _rule;

    /// <summary>
    /// Creates the handler and captures, once, the set of trains marked <c>[TraxBroadcast]</c>.
    /// Registered by <c>AddTraxGraphQL</c>; not intended to be constructed directly.
    /// </summary>
    /// <param name="eventSender">HotChocolate's subscription transport.</param>
    /// <param name="discoveryService">Supplies the registered trains.</param>
    /// <param name="options">Decides whether every train is forwarded or only broadcast ones.</param>
    /// <param name="logger">Logs unknown event types; optional.</param>
    public GraphQLTrainEventHandler(
        ITopicEventSender eventSender,
        ITrainDiscoveryService discoveryService,
        TrainLifecycleStreamOptions options,
        ILogger<GraphQLTrainEventHandler>? logger = null
    )
    {
        _publisher = LifecycleEventPublisher.For(eventSender);
        _logger = logger;
        _rule = new LifecycleStreamRule(discoveryService, options);
    }

    // Admin observability forwards every train; otherwise only [TraxBroadcast] trains.
    private bool ShouldForward(string trainName) => _rule.Publishes(trainName);

    /// <summary>
    /// Forwards a lifecycle message from another process to the matching lifecycle subscription on
    /// this process. Skips data-change messages, trains that are not forwarded, and unknown event
    /// types (logged). An unparseable train state is sent as <c>Pending</c>.
    /// </summary>
    /// <param name="message">The broadcast message.</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        // Data-change signals ride the same transport but are handled by GraphQLDataChangeHandler.
        if (message.EventType == TrainLifecycleEventMessage.DataChangedEventType)
            return;

        if (!ShouldForward(message.TrainName))
            return;

        var topicName = message.EventType switch
        {
            "Started" => nameof(LifecycleSubscriptions.OnTrainStarted),
            "Completed" => nameof(LifecycleSubscriptions.OnTrainCompleted),
            "Failed" => nameof(LifecycleSubscriptions.OnTrainFailed),
            "Cancelled" => nameof(LifecycleSubscriptions.OnTrainCancelled),
            "StateChanged" => nameof(LifecycleSubscriptions.OnTrainStateChanged),
            _ => null,
        };

        if (topicName is null)
        {
            _logger?.LogWarning(
                "Unknown event type {EventType} for train {TrainName}.",
                message.EventType,
                message.TrainName
            );
            return;
        }

        var lifecycleEvent = new TrainLifecycleEvent(
            MetadataId: message.MetadataId,
            ExternalId: message.ExternalId,
            TrainName: message.TrainName,
            TrainState: Enum.TryParse<TrainState>(message.TrainState, out var state)
                ? state
                : TrainState.Pending,
            Timestamp: message.Timestamp,
            FailureJunction: message.FailureJunction,
            FailureReason: message.FailureReason,
            Output: message.Output,
            HostName: message.HostName,
            HostEnvironment: message.HostEnvironment
        )
        {
            // The exception type decides whether a broadcast subscriber may see the reason, so a
            // remote failure is presented exactly as the same failure on this node would be.
            FailureException = message.FailureException,
        };

        await _publisher.PublishAsync(topicName, lifecycleEvent, ct);

        _logger?.LogDebug(
            "Forwarded remote {EventType} event for train {TrainName} ({ExternalId}) to GraphQL subscriptions.",
            message.EventType,
            message.TrainName,
            message.ExternalId
        );
    }
}
