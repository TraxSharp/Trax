using System.Runtime.CompilerServices;
using HotChocolate.Execution;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// The source stream a Trax subscription hands HotChocolate: one subscriber's view of a topic,
/// read through <c>read</c>, which owns the topic registration.
/// </summary>
/// <remarks>
/// A subscribe resolver that returns an <see cref="IAsyncEnumerable{T}"/> is wrapped by
/// HotChocolate in a source stream whose disposal does nothing, so disposing the subscription's
/// response stream would leave the topic registration, and an enumeration waiting on it, alive
/// until the next event on the topic. Returning this instead lets HotChocolate dispose the topic
/// registration with the subscription, which completes the topic's channel and ends a running
/// enumeration at once. The registration is released once, whichever of HotChocolate and the
/// enumeration's own end gets there first.
/// </remarks>
internal sealed class SubscriberSourceStream<T> : ISourceStream<T>
{
    private readonly OnceDisposed _topic;
    private readonly Func<ISourceStream<T>, CancellationToken, IAsyncEnumerable<T>> _read;

    public SubscriberSourceStream(
        ISourceStream<T> topic,
        Func<ISourceStream<T>, CancellationToken, IAsyncEnumerable<T>> read
    )
    {
        _topic = new OnceDisposed(topic);
        _read = read;
    }

    public IAsyncEnumerable<T> ReadEventsAsync() => Read<T>();

    IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

    public ValueTask DisposeAsync() => _topic.DisposeAsync();

    private async IAsyncEnumerable<TOut> Read<TOut>(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        await foreach (var e in _read(_topic, ct).WithCancellation(ct).ConfigureAwait(false))
            yield return (TOut)(object?)e!;
    }

    /// <summary>The topic registration, released at most once.</summary>
    private sealed class OnceDisposed(ISourceStream<T> inner) : ISourceStream<T>
    {
        private int _disposed;

        public IAsyncEnumerable<T> ReadEventsAsync() => inner.ReadEventsAsync();

        IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() =>
            ((ISourceStream)inner).ReadEventsAsync();

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0 ? inner.DisposeAsync() : default;
    }
}
