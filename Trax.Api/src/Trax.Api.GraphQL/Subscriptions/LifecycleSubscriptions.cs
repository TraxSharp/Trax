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
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStarted), receiver, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainCompleted(
        [Service] ITopicEventReceiver receiver,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCompleted), receiver, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainFailed(
        [Service] ITopicEventReceiver receiver,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainFailed), receiver, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainCancelled(
        [Service] ITopicEventReceiver receiver,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainCancelled), receiver, access, user, ct);

    internal ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeToTrainStateChanged(
        [Service] ITopicEventReceiver receiver,
        [Service] LifecycleSubscriptionAccess access,
        [GlobalState(PrincipalState)] ClaimsPrincipal? user,
        CancellationToken ct
    ) => SubscribeLifecycle(nameof(OnTrainStateChanged), receiver, access, user, ct);

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
        return Read(stream, e => e);
    }

    private static async ValueTask<IAsyncEnumerable<TrainLifecycleEvent>> SubscribeLifecycle(
        string topic,
        ITopicEventReceiver receiver,
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
        return Read(stream, visibility.Present);
    }

    /// <summary>
    /// Reads <paramref name="stream"/>, passing each event through <paramref name="present"/> and
    /// dropping the ones it returns <c>null</c> for, and disposes the topic subscription when the
    /// subscriber goes away.
    /// </summary>
    private static async IAsyncEnumerable<T> Read<T>(
        ISourceStream<T> stream,
        Func<T, T?> present,
        [EnumeratorCancellation] CancellationToken ct = default
    )
        where T : class
    {
        await using (stream.ConfigureAwait(false))
        {
            await foreach (
                var e in stream.ReadEventsAsync().WithCancellation(ct).ConfigureAwait(false)
            )
            {
                if (present(e) is { } visible)
                    yield return visible;
            }
        }
    }
}
