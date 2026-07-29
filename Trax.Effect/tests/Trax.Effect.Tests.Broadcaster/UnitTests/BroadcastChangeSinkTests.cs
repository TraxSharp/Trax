using FluentAssertions;
using NSubstitute;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

[TestFixture]
public class BroadcastChangeSinkTests
{
    private ITrainEventBroadcaster _broadcaster = null!;
    private BroadcastChangeSink _sink = null!;

    [SetUp]
    public void SetUp()
    {
        _broadcaster = Substitute.For<ITrainEventBroadcaster>();
        _broadcaster
            .PublishAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _sink = new BroadcastChangeSink(_broadcaster, TimeProvider.System);
    }

    [Test]
    public async Task FlushAsync_PublishesOneDataChangedMessagePerDomain()
    {
        await _sink.FlushAsync(
            new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter },
            CancellationToken.None
        );

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == TrainLifecycleEventMessage.DataChangedEventType
                    && m.ChangeDomain == nameof(ChangeDomain.WorkQueue)
                ),
                Arg.Any<CancellationToken>()
            );
        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == TrainLifecycleEventMessage.DataChangedEventType
                    && m.ChangeDomain == nameof(ChangeDomain.DeadLetter)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task FlushAsync_TagsMessagesWithLocalExecutor_SoLoopbackIsFiltered()
    {
        // The receiver drops events whose Executor matches the local process. The sink must stamp
        // the message with this process's executor so the originating process ignores its own
        // broadcast (it already delivered the signal to local subscribers).
        await _sink.FlushAsync(new[] { ChangeDomain.Manifest }, CancellationToken.None);

        var localExecutor = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m => m.Executor == localExecutor),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task FlushAsync_EmptyDomains_PublishesNothing()
    {
        await _sink.FlushAsync(Array.Empty<ChangeDomain>(), CancellationToken.None);

        await _broadcaster
            .DidNotReceive()
            .PublishAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());
    }
}
