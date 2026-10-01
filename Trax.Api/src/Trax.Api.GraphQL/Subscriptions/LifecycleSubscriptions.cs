using System.Runtime.CompilerServices;
using System.Security.Claims;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// GraphQL subscription root for real-time push. Carries per-train lifecycle events plus a
/// coalesced <c>onDataChanged</c> signal that admin UIs use to refetch a domain's view without
/// polling. Clients connect via WebSocket at the GraphQL endpoint.
/// </summary>
/// <remarks>
/// Each field carries the authorization of the data it streams, decided per subscriber by
/// <see cref="LifecycleSubscriptionAccess"/>: a subscriber sees the trains the operations
/// authorization or each train's own posture admits them to, and one who could see nothing is
/// refused when subscribing.
/// </remarks>
public class LifecycleSubscriptions
{
    private const string PrincipalState = "ClaimsPrincipal";

    /// <summary>
    /// Fires when a train you may see starts running.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToTrainStarted))]
    public TrainLifecycleEvent OnTrainStarted([EventMessage] TrainLifecycleEvent e) => e;

    /// <summary>
    /// Fires when a train you may see completes successfully.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToTrainCompleted))]
    public TrainLifecycleEvent OnTrainCompleted([EventMessage] TrainLifecycleEvent e) => e;

    /// <summary>
    /// Fires when a train you may see fails.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToTrainFailed))]
    public TrainLifecycleEvent OnTrainFailed([EventMessage] TrainLifecycleEvent e) => e;

    /// <summary>
    /// Fires when a train you may see is cancelled.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToTrainCancelled))]
    public TrainLifecycleEvent OnTrainCancelled([EventMessage] TrainLifecycleEvent e) => e;

    /// <summary>
    /// Fires on every state change of a train you may see, including the start, completion, failure
    /// and cancellation that the other lifecycle subscriptions report individually.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToTrainStateChanged))]
    public TrainLifecycleEvent OnTrainStateChanged([EventMessage] TrainLifecycleEvent e) => e;

    /// <summary>
    /// Fires when a scheduler/admin data domain changes (work queue, dead letters, manifests,
    /// manifest groups, scheduler config). One event per coalesced burst; the payload names the
    /// domain so a client can refetch just that view.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToDataChanged))]
    public DataChangedEvent OnDataChanged([EventMessage] DataChangedEvent e) => e;

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainStarted(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStarted), receiver, sender, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainCompleted(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCompleted), receiver, sender, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainFailed(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainFailed), receiver, sender, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainCancelled(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCancelled), receiver, sender, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainStateChanged(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStateChanged), receiver, sender, access, user, ct);

    internal async ValueTask<IAsyncEnumerable<DataChangedEvent>> SubscribeToDataChanged(
        [Service] ITopicEventReceiver receiver,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    )
    {
        if (!await access.DataChangesFor(user).ConfigureAwait(false))
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        var stream = await receiver
            .SubscribeAsync<DataChangedEvent>(nameof(OnDataChanged), ct)
            .ConfigureAwait(false);
        return Read(stream);
    }

    private static async ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeLifecycle(
        string topic,
        ITopicEventReceiver receiver,
        ITopicEventSender sender,
        LifecycleSubscriptionAccess access,
        ClaimsPrincipal? user,
        CancellationToken ct
    )
    {
        var visibility = await access.LifecycleFor(user).ConfigureAwait(false);
        if (visibility.IsEmpty)
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        var stream = await receiver
            .SubscribeAsync<TrainLifecycleEvent>(topic, ct)
            .ConfigureAwait(false);

        // Read once the subscription is registered: every event numbered above it is sent to this
        // subscription, so a later jump past it is a loss, not an event from before it existed.
        var baseline = await LifecycleEventPublisher
            .For(sender)
            .LastPublishedAsync(topic, ct)
            .ConfigureAwait(false);
        return ReadLifecycle(stream, visibility, baseline);
    }

    /// <summary>
    /// Reads a lifecycle topic for one subscriber: passes each event through
    /// <paramref name="visibility"/>, and numbers the events it delivers in
    /// <see cref="TrainLifecycleEvent.Sequence"/>, skipping a number after any lost publish.
    /// </summary>
    /// <remarks>
    /// The topic buffer drops events silently when a subscriber falls behind. A publish number
    /// more than one past the last one read means events were dropped in between. Some of them may
    /// be events this subscriber could not see anyway; the gap is reported regardless, because the
    /// cost of a needless refetch is small and the alternative is a feed that misses state changes
    /// without saying so. The gap is always one skipped number, so a broadcast subscriber does not
    /// learn how many events other trains produced. See
    /// <c>docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md</c>.
    /// </remarks>
    internal static async IAsyncEnumerable<TrainLifecycleEvent> ReadLifecycle(
        ISourceStream<TrainLifecycleEvent> stream,
        LifecycleVisibility visibility,
        long baseline,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var lastPublished = baseline;
        var lost = false;
        long sequence = 0;

        await using (stream.ConfigureAwait(false))
        {
            await foreach (
                var e in stream.ReadEventsAsync().WithCancellation(ct).ConfigureAwait(false)
            )
            {
                // An event sent without a number (by host code using the transport directly)
                // carries no information about losses and is delivered as it is.
                if (e.PublishSequence > 0)
                {
                    if (e.PublishSequence > lastPublished + 1)
                        lost = true;
                    lastPublished = Math.Max(lastPublished, e.PublishSequence);
                }

                if (visibility.Present(e) is not { } visible)
                    continue;

                sequence += lost ? 2 : 1;
                lost = false;
                yield return visible with
                {
                    Sequence = sequence,
                };
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="stream"/> and disposes the topic subscription when the subscriber
    /// goes away.
    /// </summary>
    private static async IAsyncEnumerable<T> Read<T>(
        ISourceStream<T> stream,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        await using (stream.ConfigureAwait(false))
        {
            await foreach (
                var e in stream.ReadEventsAsync().WithCancellation(ct).ConfigureAwait(false)
            )
                yield return e;
        }
    }
}
