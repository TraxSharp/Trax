using System.Runtime.CompilerServices;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Enums;

namespace Trax.Api.Tests;

/// <summary>
/// The live lifecycle feed is lossy, and says so: each subscription numbers the events it
/// delivers in <c>sequence</c>, one more each time, and skips a number when events were lost
/// on the way to it, so a client knows to refetch.
///
/// <para>Enforces <c>docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md")]
[TestFixture]
public class LifecycleSequenceTests
{
    private const string Adr =
        "docs/adr/0032-the-lifecycle-feed-is-lossy-and-numbers-its-events.md";
    private const string Visible = "My.Visible.Train";
    private const string Hidden = "My.Hidden.Train";

    private static readonly LifecycleVisibility BroadcastOfVisible = new(
        All: false,
        new HashSet<string>(StringComparer.Ordinal) { Visible }
    );

    [Test]
    public async Task ReadLifecycle_NothingLost_NumbersEventsOneApart()
    {
        var delivered = await ReadAsync(LifecycleVisibility.Operations, 0, Numbered(1, 2, 3));

        delivered.Should().Equal([1L, 2L, 3L], "an unbroken feed counts up by one, per " + Adr);
    }

    [Test]
    public async Task ReadLifecycle_PublishesLost_SkipsOneNumberWhateverWasLost()
    {
        // 3 to 9 were dropped by the subscriber's buffer.
        var delivered = await ReadAsync(LifecycleVisibility.Operations, 0, Numbered(1, 2, 10, 11));

        delivered
            .Should()
            .Equal(
                [1L, 2L, 4L, 5L],
                "a loss is one skipped number, not the count lost, which would tell a broadcast "
                    + "subscriber how busy trains it cannot see are, per "
                    + Adr
            );
    }

    [Test]
    public async Task ReadLifecycle_LostEventOfAHiddenTrain_StillSignalsAGap()
    {
        // 2 was lost; the subscriber cannot know whose it was, so it is told to refetch.
        var events = new[] { Event(1, Visible), Event(3, Hidden), Event(4, Visible) };

        var delivered = await ReadAsync(BroadcastOfVisible, 0, events);

        delivered.Should().Equal([1L, 3L], "a loss the subscriber cannot rule out is reported");
    }

    [Test]
    public async Task ReadLifecycle_HiddenEventsReceived_AreNotAGap()
    {
        var events = new[] { Event(1, Visible), Event(2, Hidden), Event(3, Visible) };

        var delivered = await ReadAsync(BroadcastOfVisible, 0, events);

        delivered.Should().Equal([1L, 2L], "an event filtered out was not lost");
    }

    [Test]
    public async Task ReadLifecycle_FromTheBaseline_OnlyALaterJumpIsAGap()
    {
        // Subscribed after publish 40: 41 is the first owed; 39 arrived from before it existed.
        var fromStart = await ReadAsync(LifecycleVisibility.Operations, 40, Numbered(39, 41, 42));
        var lostFirst = await ReadAsync(LifecycleVisibility.Operations, 40, Numbered(43, 44));

        fromStart.Should().Equal([1L, 2L, 3L]);
        lostFirst.Should().Equal([2L, 3L], "41 and 42 were owed to this subscription and lost");
    }

    [Test]
    public async Task ReadLifecycle_UnnumberedEvent_IsDeliveredWithoutAGap()
    {
        var events = new[] { Event(1, Visible), Event(0, Visible), Event(2, Visible) };

        var delivered = await ReadAsync(LifecycleVisibility.Operations, 0, events);

        delivered.Should().Equal([1L, 2L, 3L]);
    }

    [Test]
    public async Task Publisher_NumbersEachTopicFromOne_AndSharesNumbersPerSender()
    {
        var sender = new RecordingSender();

        await LifecycleEventPublisher.For(sender).PublishAsync("a", Event(0, Visible), default);
        await LifecycleEventPublisher.For(sender).PublishAsync("a", Event(0, Visible), default);
        await LifecycleEventPublisher.For(sender).PublishAsync("b", Event(0, Visible), default);

        sender
            .Sent.Select(s => (s.Topic, s.Event.PublishSequence))
            .Should()
            .Equal(("a", 1L), ("a", 2L), ("b", 1L));
        (await LifecycleEventPublisher.For(sender).LastPublishedAsync("a", default)).Should().Be(2);
    }

    [Test]
    public async Task Publisher_SendThatThrows_UsesNoNumber()
    {
        var sender = new RecordingSender { FailNext = true };
        var publisher = LifecycleEventPublisher.For(sender);

        var failing = () => publisher.PublishAsync("a", Event(0, Visible), default).AsTask();
        await failing.Should().ThrowAsync<InvalidOperationException>();
        await publisher.PublishAsync("a", Event(0, Visible), default);

        sender.Sent.Single().Event.PublishSequence.Should().Be(1, "a failed send is not a loss");
    }

    private static async Task<List<long>> ReadAsync(
        LifecycleVisibility visibility,
        long baseline,
        IEnumerable<TrainLifecycleEvent> events
    )
    {
        var delivered = new List<long>();
        await foreach (
            var e in LifecycleSubscriptions.ReadLifecycle(
                new ListStream(events),
                visibility,
                baseline
            )
        )
            delivered.Add(e.Sequence);
        return delivered;
    }

    private static IEnumerable<TrainLifecycleEvent> Numbered(params long[] numbers) =>
        numbers.Select(n => Event(n, Visible));

    private static TrainLifecycleEvent Event(long publishSequence, string trainName) =>
        new(
            MetadataId: publishSequence,
            ExternalId: "ext",
            TrainName: trainName,
            TrainState: TrainState.Completed,
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            Output: null
        )
        {
            PublishSequence = publishSequence,
        };

    private sealed class ListStream(IEnumerable<TrainLifecycleEvent> events)
        : ISourceStream<TrainLifecycleEvent>
    {
        public async IAsyncEnumerable<TrainLifecycleEvent> ReadEventsAsync()
        {
            foreach (var e in events)
            {
                await Task.Yield();
                yield return e;
            }
        }

        async IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync()
        {
            await foreach (var e in ReadEventsAsync())
                yield return e;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSender : ITopicEventSender
    {
        public bool FailNext { get; set; }

        public List<(string Topic, TrainLifecycleEvent Event)> Sent { get; } = [];

        public ValueTask SendAsync<TMessage>(
            string topicName,
            TMessage message,
            CancellationToken cancellationToken = default
        )
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("transport down");
            }
            Sent.Add((topicName, (TrainLifecycleEvent)(object)message!));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(string topicName) => ValueTask.CompletedTask;
    }
}
