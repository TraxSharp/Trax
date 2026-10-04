using System.Runtime.CompilerServices;
using System.Security.Claims;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Validation;

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
    /// Fires for each step of the run <paramref name="metadataId"/>: a junction starting, completing,
    /// failing or being cancelled, a question a routing step asked, and the track it took. Only a
    /// host that called <c>AddJunctionEvents()</c> publishes them, and you receive them exactly when
    /// you would receive that run's train events. In the operations view, read
    /// <c>operations.junctionRuns</c> for the steps a run took before you subscribed or that a gap
    /// in <c>sequence</c> lost. A broadcast subscriber cannot read it, so it has no way to recover a
    /// lost step: it sees only the steps that reach it after it subscribed.
    /// </summary>
    /// <param name="metadataId">The run to follow (its execution id).</param>
    /// <param name="e">The event.</param>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToJunctionEvent))]
    public JunctionEvent OnJunctionEvent(long metadataId, [EventMessage] JunctionEvent e) => e;

    /// <summary>
    /// Fires when a scheduler/admin data domain changes (work queue, dead letters, manifests,
    /// manifest groups, scheduler config). One event per coalesced burst; the payload names the
    /// domain so a client can refetch just that view.
    /// </summary>
    [AuthorizedPerSubscriber]
    [Subscribe(With = nameof(SubscribeToDataChanged))]
    public DataChangedEvent OnDataChanged([EventMessage] DataChangedEvent e) => e;

    internal ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeToTrainStarted(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStarted), receiver, sender, access, user, ct);

    internal ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeToTrainCompleted(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCompleted), receiver, sender, access, user, ct);

    internal ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeToTrainFailed(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainFailed), receiver, sender, access, user, ct);

    internal ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeToTrainCancelled(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCancelled), receiver, sender, access, user, ct);

    internal ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeToTrainStateChanged(
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStateChanged), receiver, sender, access, user, ct);

    internal async ValueTask<ISourceStream<JunctionEvent>> SubscribeToJunctionEvent(
        long metadataId,
        [Service] ITopicEventReceiver receiver,
        [Service] ITopicEventSender sender,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    )
    {
        RunIdArgument.Require(metadataId);

        // Who may see a run's steps is who may see its train's events: the same visibility,
        // decided once here, and the same refusal for a subscriber who could see nothing.
        var visibility = await access.LifecycleFor(user).ConfigureAwait(false);
        if (visibility.IsEmpty)
            throw new GraphQLException(EndpointPolicyRequestMiddleware.NotAuthorized());

        const string topic = nameof(OnJunctionEvent);
        var stream = await receiver.SubscribeAsync<JunctionEvent>(topic, ct).ConfigureAwait(false);
        long baseline;
        try
        {
            baseline = await LifecycleEventPublisher
                .For(sender)
                .LastPublishedAsync(topic, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SubscriberSourceStream<JunctionEvent>(
            stream,
            (topicStream, readCt) =>
                ReadJunctionEvents(topicStream, metadataId, visibility, baseline, readCt)
        );
    }

    internal async ValueTask<ISourceStream<DataChangedEvent>> SubscribeToDataChanged(
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
        return new SubscriberSourceStream<DataChangedEvent>(stream, Read);
    }

    private static async ValueTask<ISourceStream<TrainLifecycleEvent>> SubscribeLifecycle(
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
        long baseline;
        try
        {
            baseline = await LifecycleEventPublisher
                .For(sender)
                .LastPublishedAsync(topic, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new SubscriberSourceStream<TrainLifecycleEvent>(
            stream,
            (topicStream, readCt) => ReadLifecycle(topicStream, visibility, baseline, readCt)
        );
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
    internal static IAsyncEnumerable<TrainLifecycleEvent> ReadLifecycle(
        ISourceStream<TrainLifecycleEvent> stream,
        LifecycleVisibility visibility,
        long baseline,
        CancellationToken ct = default
    ) =>
        ReadNumbered(
            stream,
            baseline,
            e => e.PublishSequence,
            visibility.Present,
            (e, sequence) => e with { Sequence = sequence },
            ct
        );

    /// <summary>
    /// Reads the junction event topic for one subscriber: only the steps of the run
    /// <paramref name="metadataId"/>, each passed through <paramref name="visibility"/>, numbered
    /// exactly as <see cref="ReadLifecycle"/> numbers train events.
    /// </summary>
    /// <remarks>
    /// The topic carries every run's steps, so a loss is reported whichever run the lost events
    /// belonged to, for the same reason a lifecycle subscription reports one for a train it cannot
    /// see: a needless refetch is cheap, a silent gap is not, and the jump of two says nothing about
    /// how much other runs did.
    /// </remarks>
    internal static IAsyncEnumerable<JunctionEvent> ReadJunctionEvents(
        ISourceStream<JunctionEvent> stream,
        long metadataId,
        LifecycleVisibility visibility,
        long baseline,
        CancellationToken ct = default
    ) =>
        ReadNumbered(
            stream,
            baseline,
            e => e.PublishSequence,
            e => e.MetadataId == metadataId ? visibility.Present(e) : null,
            (e, sequence) => e with { Sequence = sequence },
            ct
        );

    private static async IAsyncEnumerable<T> ReadNumbered<T>(
        ISourceStream<T> stream,
        long baseline,
        Func<T, long> publishSequence,
        Func<T, T?> present,
        Func<T, long, T> numbered,
        [EnumeratorCancellation] CancellationToken ct = default
    )
        where T : class
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
                var published = publishSequence(e);
                if (published > 0)
                {
                    if (published > lastPublished + 1)
                        lost = true;
                    lastPublished = Math.Max(lastPublished, published);
                }

                if (present(e) is not { } visible)
                    continue;

                sequence += lost ? 2 : 1;
                lost = false;
                yield return numbered(visible, sequence);
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
