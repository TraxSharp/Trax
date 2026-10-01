using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Hooks;

/// <summary>
/// Lifecycle hook that publishes train state transitions to Hot Chocolate's
/// in-memory subscription transport, enabling real-time GraphQL subscriptions.
/// By default only trains decorated with <c>[TraxBroadcast]</c> are published; when the operations
/// (admin) surface is exposed, every train is published (see <see cref="TrainLifecycleStreamOptions"/>).
/// </summary>
public class GraphQLSubscriptionHook : ITrainLifecycleHook
{
    private readonly LifecycleEventPublisher _publisher;
    private readonly bool _streamAllTrains;
    private readonly HashSet<string> _enabledTrains;

    /// <summary>
    /// Creates the hook and captures, once, the set of trains marked <c>[TraxBroadcast]</c>.
    /// Registered by <c>AddTraxGraphQL</c>; not intended to be constructed directly.
    /// </summary>
    /// <param name="eventSender">HotChocolate's subscription transport.</param>
    /// <param name="discoveryService">Supplies the registered trains.</param>
    /// <param name="options">Decides whether every train is published or only broadcast ones.</param>
    public GraphQLSubscriptionHook(
        ITopicEventSender eventSender,
        ITrainDiscoveryService discoveryService,
        TrainLifecycleStreamOptions options
    )
    {
        _publisher = LifecycleEventPublisher.For(eventSender);
        _streamAllTrains = options.StreamAllTrains;
        _enabledTrains = discoveryService
            .DiscoverTrains()
            .Where(r => r.IsBroadcastEnabled)
            .Select(r => r.ServiceType.FullName!)
            .ToHashSet();
    }

    // Admin observability streams every train; otherwise only [TraxBroadcast] trains.
    private bool ShouldPublish(string trainName) =>
        _streamAllTrains || _enabledTrains.Contains(trainName);

    /// <summary>Publishes to <c>onTrainStarted</c> when the train is published at all.</summary>
    /// <param name="metadata">The execution that started.</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task OnStarted(Metadata metadata, CancellationToken ct)
    {
        if (!ShouldPublish(metadata.Name))
            return;

        await _publisher.PublishAsync(
            nameof(LifecycleSubscriptions.OnTrainStarted),
            MapEvent(metadata),
            ct
        );
    }

    /// <summary>Publishes to <c>onTrainCompleted</c> when the train is published at all.</summary>
    /// <param name="metadata">The execution that completed.</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (!ShouldPublish(metadata.Name))
            return;

        await _publisher.PublishAsync(
            nameof(LifecycleSubscriptions.OnTrainCompleted),
            MapEvent(metadata),
            ct
        );
    }

    /// <summary>
    /// Publishes to <c>onTrainFailed</c> when the train is published at all. The event carries the
    /// recorded failure, not <paramref name="exception"/> itself.
    /// </summary>
    /// <param name="metadata">The execution that failed.</param>
    /// <param name="exception">The exception the train failed with (not sent).</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        if (!ShouldPublish(metadata.Name))
            return;

        await _publisher.PublishAsync(
            nameof(LifecycleSubscriptions.OnTrainFailed),
            MapEvent(metadata),
            ct
        );
    }

    /// <summary>Publishes to <c>onTrainCancelled</c> when the train is published at all.</summary>
    /// <param name="metadata">The execution that was cancelled.</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task OnCancelled(Metadata metadata, CancellationToken ct)
    {
        if (!ShouldPublish(metadata.Name))
            return;

        await _publisher.PublishAsync(
            nameof(LifecycleSubscriptions.OnTrainCancelled),
            MapEvent(metadata),
            ct
        );
    }

    /// <summary>Publishes to <c>onTrainStateChanged</c> when the train is published at all.</summary>
    /// <param name="metadata">The execution whose state changed.</param>
    /// <param name="ct">Cancels the send.</param>
    public async Task OnStateChanged(Metadata metadata, CancellationToken ct)
    {
        if (!ShouldPublish(metadata.Name))
            return;

        await _publisher.PublishAsync(
            nameof(LifecycleSubscriptions.OnTrainStateChanged),
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
            FailureJunction: metadata.FailureJunction,
            FailureReason: metadata.FailureReason,
            Output: metadata.Output
        )
        {
            FailureException = metadata.FailureException,
        };
}
