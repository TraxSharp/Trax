using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HotChocolate.Subscriptions;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// Publishes lifecycle events to their subscription topics, numbering each topic's events so a
/// subscription can tell when one was lost on the way to it.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate gives every subscriber a bounded buffer per topic, and a subscriber that falls
/// behind loses its oldest buffered events without being told. Each event published here carries
/// <see cref="TrainLifecycleEvent.PublishSequence"/>, one more than the previous event on the same
/// topic, and the subscription compares it with the last one it read: a jump means events were
/// dropped between the two. See
/// <c>docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md</c>.
/// </para>
/// <para>
/// Numbering and sending happen under one per-topic lock, so the order of the numbers is the
/// order the transport receives the events in, and two publishers on different threads cannot
/// make a gap appear where nothing was lost.
/// </para>
/// <para>
/// There is one publisher per <see cref="ITopicEventSender"/>, found with <see cref="For"/>.
/// Every publisher of lifecycle events on a host sends through the same sender, so they share the
/// numbers however each of them was constructed.
/// </para>
/// </remarks>
internal sealed class LifecycleEventPublisher
{
    private static readonly ConditionalWeakTable<
        ITopicEventSender,
        LifecycleEventPublisher
    > Publishers = new();

    private readonly ITopicEventSender _sender;
    private readonly ConcurrentDictionary<string, TopicSequence> _topics = new(
        StringComparer.Ordinal
    );

    private LifecycleEventPublisher(ITopicEventSender sender) => _sender = sender;

    /// <summary>The publisher that numbers lifecycle events sent through <paramref name="sender"/>.</summary>
    public static LifecycleEventPublisher For(ITopicEventSender sender) =>
        Publishers.GetValue(sender, s => new LifecycleEventPublisher(s));

    /// <summary>
    /// Numbers <paramref name="lifecycleEvent"/> as the next event on <paramref name="topic"/> and
    /// sends it. A send that throws uses no number.
    /// </summary>
    public async ValueTask PublishAsync(
        string topic,
        TrainLifecycleEvent lifecycleEvent,
        CancellationToken ct
    )
    {
        var sequence = _topics.GetOrAdd(topic, _ => new TopicSequence());
        await sequence.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = sequence.Last + 1;
            await _sender
                .SendAsync(topic, lifecycleEvent with { PublishSequence = next }, ct)
                .ConfigureAwait(false);
            sequence.Last = next;
        }
        finally
        {
            sequence.Gate.Release();
        }
    }

    /// <summary>
    /// The number of the last event sent on <paramref name="topic"/>. Read after a subscription
    /// has registered with the transport, every event numbered above it is sent after the
    /// subscription exists, so it reaches the subscription unless its buffer drops it.
    /// </summary>
    public async ValueTask<long> LastPublishedAsync(string topic, CancellationToken ct)
    {
        var sequence = _topics.GetOrAdd(topic, _ => new TopicSequence());
        await sequence.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return sequence.Last;
        }
        finally
        {
            sequence.Gate.Release();
        }
    }

    private sealed class TopicSequence
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public long Last;
    }
}
