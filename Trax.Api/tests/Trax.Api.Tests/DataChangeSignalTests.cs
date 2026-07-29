using FluentAssertions;
using HotChocolate.Subscriptions;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Sinks;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.Tests;

/// <summary>
/// Covers the two Api-side pieces of the change-signal pipeline: the in-process sink that publishes
/// coalesced signals to the <c>onDataChanged</c> subscription, and the cross-process handler that
/// forwards data-change signals arriving over the broadcaster.
/// </summary>
[TestFixture]
public class DataChangeSignalTests
{
    private sealed record RecordedEvent(string Topic, object Message);

    private sealed class RecordingTopicEventSender : ITopicEventSender
    {
        public List<RecordedEvent> Events { get; } = [];

        public ValueTask SendAsync<TMessage>(
            string topicName,
            TMessage message,
            CancellationToken cancellationToken = default
        )
        {
            Events.Add(new RecordedEvent(topicName, message!));
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(string topicName) => ValueTask.CompletedTask;
    }

    private static TrainLifecycleEventMessage DataChangedMessage(string? domain) =>
        new(
            MetadataId: 0,
            ExternalId: string.Empty,
            TrainName: string.Empty,
            TrainState: string.Empty,
            Timestamp: new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: TrainLifecycleEventMessage.DataChangedEventType,
            Executor: "RemoteScheduler",
            Output: null,
            ChangeDomain: domain
        );

    #region TopicEventSenderChangeSink

    [Test]
    public async Task Sink_FlushesOneEventPerDomainToOnDataChanged()
    {
        var sender = new RecordingTopicEventSender();
        var sink = new TopicEventSenderChangeSink(sender, TimeProvider.System);

        await sink.FlushAsync(
            new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter },
            CancellationToken.None
        );

        sender.Events.Should().HaveCount(2);
        sender
            .Events.Should()
            .OnlyContain(e => e.Topic == nameof(LifecycleSubscriptions.OnDataChanged));
        sender
            .Events.Select(e => ((DataChangedEvent)e.Message).Domain)
            .Should()
            .BeEquivalentTo(new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter });
    }

    [Test]
    public async Task Sink_EmptyDomains_PublishesNothing()
    {
        var sender = new RecordingTopicEventSender();
        var sink = new TopicEventSenderChangeSink(sender, TimeProvider.System);

        await sink.FlushAsync(Array.Empty<ChangeDomain>(), CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion

    #region GraphQLDataChangeHandler

    [Test]
    public async Task Handler_DataChangedMessage_ForwardsToOnDataChanged()
    {
        var sender = new RecordingTopicEventSender();
        var handler = new GraphQLDataChangeHandler(sender);

        await handler.HandleAsync(DataChangedMessage("DeadLetter"), CancellationToken.None);

        sender.Events.Should().ContainSingle();
        sender.Events[0].Topic.Should().Be(nameof(LifecycleSubscriptions.OnDataChanged));
        var evt = (DataChangedEvent)sender.Events[0].Message;
        evt.Domain.Should().Be(ChangeDomain.DeadLetter);
        evt.Timestamp.Should().Be(new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task Handler_LifecycleMessage_IsIgnored()
    {
        // A train lifecycle event (handled by GraphQLTrainEventHandler) must be left alone here.
        var sender = new RecordingTopicEventSender();
        var handler = new GraphQLDataChangeHandler(sender);

        var lifecycle = DataChangedMessage("WorkQueue") with { EventType = "Completed" };
        await handler.HandleAsync(lifecycle, CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    [Test]
    public async Task Handler_UnrecognizedDomain_IsIgnored()
    {
        var sender = new RecordingTopicEventSender();
        var handler = new GraphQLDataChangeHandler(sender);

        await handler.HandleAsync(DataChangedMessage("NotARealDomain"), CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    [Test]
    public async Task Handler_NullDomain_IsIgnored()
    {
        var sender = new RecordingTopicEventSender();
        var handler = new GraphQLDataChangeHandler(sender);

        await handler.HandleAsync(DataChangedMessage(null), CancellationToken.None);

        sender.Events.Should().BeEmpty();
    }

    #endregion
}
