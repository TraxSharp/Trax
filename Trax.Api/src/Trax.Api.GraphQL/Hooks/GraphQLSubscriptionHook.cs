using HotChocolate.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Lifecycle hook that publishes train state transitions to Hot Chocolate's
/// in-memory subscription transport, enabling real-time GraphQL subscriptions.
/// Only trains decorated with <c>[TraxBroadcast]</c> have their events published.
/// </summary>
public class GraphQLSubscriptionHook : ITrainLifecycleHook
{
    private readonly ITopicEventSender _eventSender;
    private readonly HashSet<string> _enabledTrains;

    public GraphQLSubscriptionHook(
        ITopicEventSender eventSender,
        ITrainDiscoveryService discoveryService
    )
    {
        _eventSender = eventSender;
        _enabledTrains = discoveryService
            .DiscoverTrains()
            .Where(r => r.IsBroadcastEnabled)
            .Select(r => r.ImplementationType.FullName!)
            .ToHashSet();
    }

    public async Task OnStarted(Metadata metadata, CancellationToken ct)
    {
        if (!_enabledTrains.Contains(metadata.Name))
            return;

        await _eventSender.SendAsync(
            nameof(LifecycleSubscriptions.OnTrainStarted),
            MapEvent(metadata),
            ct
        );
    }

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (!_enabledTrains.Contains(metadata.Name))
            return;

        await _eventSender.SendAsync(
            nameof(LifecycleSubscriptions.OnTrainCompleted),
            MapEvent(metadata),
            ct
        );
    }

    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        if (!_enabledTrains.Contains(metadata.Name))
            return;

        await _eventSender.SendAsync(
            nameof(LifecycleSubscriptions.OnTrainFailed),
            MapEvent(metadata),
            ct
        );
    }

    public async Task OnCancelled(Metadata metadata, CancellationToken ct)
    {
        if (!_enabledTrains.Contains(metadata.Name))
            return;

        await _eventSender.SendAsync(
            nameof(LifecycleSubscriptions.OnTrainCancelled),
            MapEvent(metadata),
            ct
        );
    }

    private static TrainLifecycleEvent MapEvent(Metadata metadata) =>
        new(
            MetadataId: metadata.Id,
            ExternalId: metadata.ExternalId,
            TrainName: metadata.Name,
            TrainState: metadata.TrainState,
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            FailureStep: metadata.FailureStep,
            FailureReason: metadata.FailureReason
        );
}

/// <summary>
/// Factory that creates <see cref="GraphQLSubscriptionHook"/> instances via DI.
/// </summary>
public class GraphQLSubscriptionHookFactory(IServiceProvider serviceProvider)
    : ITrainLifecycleHookFactory
{
    public ITrainLifecycleHook Create() =>
        serviceProvider.GetRequiredService<GraphQLSubscriptionHook>();
}
