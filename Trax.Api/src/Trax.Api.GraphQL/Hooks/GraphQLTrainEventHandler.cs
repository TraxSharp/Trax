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
/// Only trains decorated with <c>[TraxBroadcast]</c> have their events forwarded.
/// </summary>
public class GraphQLTrainEventHandler : ITrainEventHandler
{
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<GraphQLTrainEventHandler>? _logger;
    private readonly bool _streamAllTrains;
    private readonly HashSet<string> _enabledTrains;

    public GraphQLTrainEventHandler(
        ITopicEventSender eventSender,
        ITrainDiscoveryService discoveryService,
        TrainLifecycleStreamOptions options,
        ILogger<GraphQLTrainEventHandler>? logger = null
    )
    {
        _eventSender = eventSender;
        _logger = logger;
        _streamAllTrains = options.StreamAllTrains;
        _enabledTrains = discoveryService
            .DiscoverTrains()
            .Where(r => r.IsBroadcastEnabled)
            .Select(r => r.ServiceType.FullName!)
            .ToHashSet();
    }

    // Admin observability forwards every train; otherwise only [TraxBroadcast] trains.
    private bool ShouldForward(string trainName) =>
        _streamAllTrains || _enabledTrains.Contains(trainName);

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
        );

        await _eventSender.SendAsync(topicName, lifecycleEvent, ct);

        _logger?.LogDebug(
            "Forwarded remote {EventType} event for train {TrainName} ({ExternalId}) to GraphQL subscriptions.",
            message.EventType,
            message.TrainName,
            message.ExternalId
        );
    }
}
