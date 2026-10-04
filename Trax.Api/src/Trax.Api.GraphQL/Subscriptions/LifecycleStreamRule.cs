using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Which trains' events this host publishes to its subscriptions at all: every train when
/// <see cref="TrainLifecycleStreamOptions.StreamAllTrains"/> is set (the operations surface is
/// exposed), otherwise only <c>[TraxBroadcast]</c> trains.
/// </summary>
/// <remarks>
/// The one rule behind the lifecycle hook, the remote train event handler and the junction event
/// handler, so a train's steps are published exactly when its own events are. Who among the
/// subscribers then receives an event is <see cref="LifecycleSubscriptionAccess"/>'s decision.
/// </remarks>
internal sealed class LifecycleStreamRule
{
    private readonly bool _streamAllTrains;
    private readonly HashSet<string> _broadcastTrains;

    /// <summary>Captures, once, the trains marked <c>[TraxBroadcast]</c>.</summary>
    public LifecycleStreamRule(
        ITrainDiscoveryService discovery,
        TrainLifecycleStreamOptions options
    )
    {
        _streamAllTrains = options.StreamAllTrains;
        _broadcastTrains = discovery
            .DiscoverTrains()
            .Where(r => r.IsBroadcastEnabled)
            .Select(r => r.ServiceType.FullName!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether events of the train named <paramref name="trainName"/> are published.</summary>
    /// <param name="trainName">The train's canonical name (its interface's full name).</param>
    public bool Publishes(string trainName) =>
        _streamAllTrains || _broadcastTrains.Contains(trainName);
}
