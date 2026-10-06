using AwesomeAssertions;
using HotChocolate.Execution;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.Tests;

/// <summary>
/// A subscription's source stream reads its topic through the subscriber's own view, and releases
/// the topic registration exactly once, whether HotChocolate disposes it or the view's own read
/// ends first.
/// </summary>
[TestFixture]
public class SubscriberSourceStreamTests
{
    [Test]
    public async Task TypedAndUntypedReads_DeliverWhatTheViewYields()
    {
        var topic = new Topic(1, 2, 3);
        var stream = new SubscriberSourceStream<int>(topic, (t, ct) => Doubled(t, ct));

        var typed = new List<int>();
        await foreach (var e in stream.ReadEventsAsync())
            typed.Add(e);
        var untyped = new List<object?>();
        await foreach (var e in ((ISourceStream)stream).ReadEventsAsync())
            untyped.Add(e);

        typed.Should().Equal(2, 4, 6);
        untyped.Should().Equal(2, 4, 6);
    }

    [Test]
    public async Task TopicRegistration_IsReleasedOnce_WhoeverReleasesIt()
    {
        var topic = new Topic(1);
        var stream = new SubscriberSourceStream<int>(
            topic,
            (t, ct) => ReadUntypedThenRelease(t, ct)
        );

        await foreach (var _ in stream.ReadEventsAsync()) { }
        await stream.DisposeAsync();
        await stream.DisposeAsync();

        topic.Disposals.Should().Be(1);
    }

    private static async IAsyncEnumerable<int> Doubled(
        ISourceStream<int> topic,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        await foreach (var e in topic.ReadEventsAsync().WithCancellation(ct))
            yield return e * 2;
    }

    private static async IAsyncEnumerable<int> ReadUntypedThenRelease(
        ISourceStream<int> topic,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        await using (topic)
        {
            await foreach (var e in ((ISourceStream)topic).ReadEventsAsync().WithCancellation(ct))
                yield return (int)e!;
        }
    }

    private sealed class Topic(params int[] events) : ISourceStream<int>
    {
        public int Disposals { get; private set; }

        public IAsyncEnumerable<int> ReadEventsAsync() => events.ToAsyncEnumerable();

        IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() =>
            events.Select(e => (object?)e).ToAsyncEnumerable();

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
