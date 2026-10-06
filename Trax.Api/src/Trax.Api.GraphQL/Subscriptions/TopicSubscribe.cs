using HotChocolate.Execution;
using HotChocolate.Subscriptions;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// How every Trax subscription registers on its topic: one subscribe at a time, across the
/// process.
/// </summary>
/// <remarks>
/// HotChocolate's topics add a subscriber to a plain list while holding only the read side of the
/// topic's lock, so two subscribes to one topic that land together can both write the same slot
/// and one of them is lost. The lost subscriber gets a stream that never yields: its socket stays
/// open and acknowledged, and it never hears an event. That is likeliest when many clients
/// subscribe at once, as every dashboard does when a host restarts and its sockets reconnect.
/// Taking subscribes one at a time leaves nothing for the list to race with but its own dispatch,
/// which never writes to it.
/// <para>
/// The gate sits here rather than around the registered <see cref="ITopicEventReceiver"/>, so it
/// holds whichever receiver the host registered and whenever it registered it.
/// </para>
/// </remarks>
internal static class TopicSubscribe
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async ValueTask<ISourceStream<TMessage>> OneAtATimeAsync<TMessage>(
        ITopicEventReceiver receiver,
        string topic,
        CancellationToken ct
    )
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await receiver.SubscribeAsync<TMessage>(topic, ct).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
