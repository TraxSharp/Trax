using System.Diagnostics;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Enums;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// A run's steps are published on the run's path, so a publish never holds the run up for longer
/// than its bound. A step that cannot be published in time is dropped, and its number is skipped,
/// so every subscriber sees the loss as a jump in <c>sequence</c>.
///
/// <para>Enforces <c>docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md")]
[TestFixture]
public class JunctionEventPublishBoundTests
{
    private const string Adr =
        "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md";
    private const string Topic = nameof(LifecycleSubscriptions.OnJunctionEvent);
    private const string Train = "Some.Train";

    [Test]
    public async Task ABlockedSend_DoesNotHoldTheRunPastTheBound_AndTheNextStepShowsTheGap()
    {
        var sender = new BlockingSender(blockFirst: 1);
        var handler = new GraphQLJunctionEventHandler(sender, StreamAll())
        {
            PublishBound = TimeSpan.FromMilliseconds(100),
        };

        var clock = Stopwatch.StartNew();
        await handler.HandleAsync(Message(position: 0), default);
        clock
            .Elapsed.Should()
            .BeLessThan(TimeSpan.FromSeconds(2), "the run waits at most the bound");

        await handler.HandleAsync(Message(position: 1), default);

        var delivered = sender.Delivered.Single();
        delivered.PublishSequence.Should().Be(2, "the abandoned step kept number 1");
        (await ReadAsync([delivered]))
            .Should()
            .Equal([2L], "a subscriber sees the step it never received as a gap, per " + Adr);
    }

    [Test]
    public async Task AStepThatCannotTakeItsTurn_IsDroppedAndItsNumberSkipped()
    {
        var sender = new BlockingSender(blockFirst: 1);
        var publisher = LifecycleEventPublisher.For(sender);

        // The first holds the topic for up to a second; the second gives up after 50ms.
        var holding = publisher
            .PublishAsync(Topic, Event(0), TimeSpan.FromSeconds(1), default)
            .AsTask();
        await sender.FirstSendStarted;

        var clock = Stopwatch.StartNew();
        await publisher.PublishAsync(Topic, Event(1), TimeSpan.FromMilliseconds(50), default);
        clock
            .Elapsed.Should()
            .BeLessThan(TimeSpan.FromMilliseconds(900), "it did not wait its turn");

        await holding;
        (await publisher.LastPublishedAsync(Topic, default))
            .Should()
            .Be(2, "a subscription made now is not owed the dropped step");

        await publisher.PublishAsync(Topic, Event(2), TimeSpan.FromSeconds(1), default);

        var delivered = sender.Delivered.Single();
        delivered.Junction.Position.Should().Be(2, "the dropped step was never sent");
        delivered.PublishSequence.Should().Be(3);
        (await ReadAsync([delivered])).Should().Equal([2L], "the drop shows as a skip");
    }

    [Test]
    public async Task ASendLeftRunningWhenTheCallerCancels_KeepsItsNumber()
    {
        var sender = new BlockingSender(blockFirst: 1);
        var publisher = LifecycleEventPublisher.For(sender);

        using var cancel = new CancellationTokenSource();
        var cancelled = publisher
            .PublishAsync(Topic, Event(0), TimeSpan.FromSeconds(30), cancel.Token)
            .AsTask();
        await sender.FirstSendStarted;
        cancel.Cancel();

        await FluentActions
            .Awaiting(() => cancelled)
            .Should()
            .ThrowAsync<OperationCanceledException>();

        (await publisher.LastPublishedAsync(Topic, default))
            .Should()
            .Be(1, "the send may still arrive numbered 1");

        await publisher.PublishAsync(Topic, Event(1), TimeSpan.FromSeconds(1), default);

        sender
            .Delivered.Single()
            .PublishSequence.Should()
            .Be(
                2,
                "a number a send left running may still arrive under is never reused, per " + Adr
            );
    }

    private static async Task<List<long>> ReadAsync(IEnumerable<JunctionEvent> events)
    {
        var delivered = new List<long>();
        await foreach (
            var e in LifecycleSubscriptions.ReadJunctionEvents(
                new ListStream(events),
                metadataId: 7,
                LifecycleVisibility.Operations,
                baseline: 0
            )
        )
            delivered.Add(e.Sequence);
        return delivered;
    }

    private static LifecycleStreamRule StreamAll()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        return new LifecycleStreamRule(
            discovery,
            new TrainLifecycleStreamOptions { StreamAllTrains = true }
        );
    }

    private static JunctionEvent Event(int position) => JunctionEvent.From(Message(position))!;

    private static TrainLifecycleEventMessage Message(int position) =>
        new(
            MetadataId: 7,
            ExternalId: "ext",
            TrainName: Train,
            TrainState: nameof(TrainState.InProgress),
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: TrainLifecycleEventMessage.JunctionStartedEventType,
            Executor: null,
            Output: null
        )
        {
            Junction = new JunctionEventPayload(
                position,
                JunctionRunKind.Junction,
                "Step",
                JunctionRunState.InProgress,
                DateTime.UtcNow
            ),
        };

    /// <summary>Never completes its first sends; records the rest.</summary>
    private sealed class BlockingSender(int blockFirst) : ITopicEventSender
    {
        private int _sends;
        private readonly TaskCompletionSource _firstStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task FirstSendStarted => _firstStarted.Task;

        public List<JunctionEvent> Delivered { get; } = [];

        public ValueTask SendAsync<TMessage>(
            string topicName,
            TMessage message,
            CancellationToken cancellationToken = default
        )
        {
            _firstStarted.TrySetResult();
            if (Interlocked.Increment(ref _sends) <= blockFirst)
                return new ValueTask(new TaskCompletionSource().Task);

            lock (Delivered)
                Delivered.Add((JunctionEvent)(object)message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(string topicName) => ValueTask.CompletedTask;
    }

    private sealed class ListStream(IEnumerable<JunctionEvent> events)
        : ISourceStream<JunctionEvent>
    {
        public async IAsyncEnumerable<JunctionEvent> ReadEventsAsync()
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
}
