using HotChocolate.Subscriptions;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Bridges cross-process data-change signals to the local <c>onDataChanged</c> subscription. A split
/// scheduler process broadcasts a <see cref="TrainLifecycleEventMessage"/> tagged with
/// <see cref="TrainLifecycleEventMessage.DataChangedEventType"/>; this handler (running in the API
/// process) forwards it to HotChocolate's subscription transport. Train lifecycle messages are left
/// to <see cref="GraphQLTrainEventHandler"/>.
/// </summary>
public class GraphQLDataChangeHandler : ITrainEventHandler
{
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<GraphQLDataChangeHandler>? _logger;

    public GraphQLDataChangeHandler(
        ITopicEventSender eventSender,
        ILogger<GraphQLDataChangeHandler>? logger = null
    )
    {
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
    {
        if (message.EventType != TrainLifecycleEventMessage.DataChangedEventType)
            return;

        if (!Enum.TryParse<ChangeDomain>(message.ChangeDomain, out var domain))
        {
            _logger?.LogWarning(
                "Received a data-change signal with an unrecognized domain '{Domain}'.",
                message.ChangeDomain
            );
            return;
        }

        await _eventSender.SendAsync(
            nameof(LifecycleSubscriptions.OnDataChanged),
            new DataChangedEvent(domain, message.Timestamp),
            ct
        );

        _logger?.LogDebug(
            "Forwarded remote data-change signal for domain {Domain} to GraphQL subscriptions.",
            domain
        );
    }
}
