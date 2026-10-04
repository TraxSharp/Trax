using System.Collections.Concurrent;
using System.Diagnostics;
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
    public ValueTask PublishAsync(
        string topic,
        TrainLifecycleEvent lifecycleEvent,
        CancellationToken ct
    ) => PublishAsync(topic, next => lifecycleEvent with { PublishSequence = next }, ct);

    /// <summary>
    /// Numbers <paramref name="junctionEvent"/> as the next event on <paramref name="topic"/> and
    /// sends it, waiting at most <paramref name="bound"/> in all, because it is called on the run's
    /// path and the next junction waits for it.
    /// </summary>
    /// <remarks>
    /// An event that cannot take its turn within the bound is dropped, and the number it would have
    /// had is skipped, so every subscription reports the loss through its <c>sequence</c> as it
    /// reports one its own buffer caused. A send that is still running when the bound expires is
    /// left to finish on its own and keeps its number: if it arrives the subscription sees it, and
    /// if it never does the next event shows the gap. The same holds for a send still running when
    /// <paramref name="ct"/> is cancelled, which then throws. A send that throws uses no number.
    /// </remarks>
    public ValueTask PublishAsync(
        string topic,
        JunctionEvent junctionEvent,
        TimeSpan bound,
        CancellationToken ct
    ) => PublishAsync(topic, next => junctionEvent with { PublishSequence = next }, ct, bound);

    private async ValueTask PublishAsync<T>(
        string topic,
        Func<long, T> numbered,
        CancellationToken ct,
        TimeSpan? bound = null
    )
    {
        var sequence = _topics.GetOrAdd(topic, _ => new TopicSequence());
        var started = Stopwatch.GetTimestamp();

        if (bound is { } limit)
        {
            if (!await sequence.Gate.WaitAsync(limit, ct).ConfigureAwait(false))
            {
                Interlocked.Increment(ref sequence.Dropped);
                return;
            }
        }
        else
            await sequence.Gate.WaitAsync(ct).ConfigureAwait(false);

        // Numbers given up by dropped events are used here, so the jump past them is a gap.
        var skipped = Interlocked.Exchange(ref sequence.Dropped, 0);
        Task? send = null;
        long next = 0;
        try
        {
            next = sequence.Last + skipped + 1;
            send = _sender.SendAsync(topic, numbered(next), ct).AsTask();
            if (bound is { } sendLimit)
            {
                var remaining = sendLimit - Stopwatch.GetElapsedTime(started);
                await send.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, ct)
                    .ConfigureAwait(false);
            }
            else
                await send.ConfigureAwait(false);

            sequence.Last = next;
        }
        catch (Exception ex) when (send is { IsFaulted: false, IsCanceled: false })
        {
            // The bound expired or the caller cancelled while the send was still running (or as it
            // finished). It is left to finish, and its number is taken whether or not it arrives, so
            // no later event can be sent under the same number.
            sequence.Last = next;
            _ = send.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (ex is not TimeoutException)
                throw;
        }
        catch
        {
            // A failed send uses no number; the skipped ones are still owed to the next event.
            Interlocked.Add(ref sequence.Dropped, skipped);
            throw;
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
            // Numbers dropped before the subscription existed are not owed to it.
            return sequence.Last + Interlocked.Read(ref sequence.Dropped);
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

        /// <summary>Numbers given up by events dropped since the last send, owed as a gap.</summary>
        public long Dropped;
    }
}
