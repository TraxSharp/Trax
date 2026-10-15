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
    private readonly HashSet<string> _progressTrains;

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
        _progressTrains = discovery
            .DiscoverTrains()
            .Where(r => r.IsRunProgressEnabled)
            .Select(r => r.ServiceType.FullName!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The trains every machine operation runs through. Their runs are a machine's own bookkeeping, one per event,
    /// read, start and discard, so they are never published, whatever the stream options say: a machine's instances
    /// have their own feed, and an operator watching every train would otherwise see each user's every click. See
    /// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
    /// </summary>
    internal static readonly IReadOnlySet<string> MachineTrains = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.ISaveSnapshot).FullName!,
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.IAdvanceSnapshot).FullName!,
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.ILoadSnapshot).FullName!,
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.ISendSnapshot).FullName!,
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.IStartSnapshot).FullName!,
        typeof(Trax.Effect.StateMachine.Persistence.Mutations.IDiscardSnapshot).FullName!,
    };

    /// <summary>Whether events of the train named <paramref name="trainName"/> are published.</summary>
    /// <param name="trainName">The train's canonical name (its interface's full name).</param>
    /// <remarks>
    /// A <c>[TraxRunProgress]</c> train is published too, so its starter can follow a run through
    /// <c>runProgress</c>. A broadcast subscriber still never sees it: its view holds only broadcast trains.
    /// </remarks>
    public bool Publishes(string trainName) =>
        !MachineTrains.Contains(trainName)
        && (
            _streamAllTrains
            || _broadcastTrains.Contains(trainName)
            || _progressTrains.Contains(trainName)
        );
}
