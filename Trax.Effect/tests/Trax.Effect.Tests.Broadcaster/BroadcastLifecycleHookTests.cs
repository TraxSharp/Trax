using System.Reflection;
using FluentAssertions;
using NSubstitute;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster;

[TestFixture]
public class BroadcastLifecycleHookTests
{
    private ITrainEventBroadcaster _broadcaster = null!;
    private BroadcastLifecycleHook _hook = null!;

    [SetUp]
    public void SetUp()
    {
        _broadcaster = Substitute.For<ITrainEventBroadcaster>();
        _hook = new BroadcastLifecycleHook(_broadcaster);
    }

    private static Metadata CreateMetadata(
        TrainState state = TrainState.Pending,
        string name = "TestTrain",
        string? failureStep = null,
        string? failureReason = null
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        metadata.TrainState = state;

        return metadata;
    }

    [Test]
    public async Task OnStarted_PublishesMessageWithCorrectEventType()
    {
        var metadata = CreateMetadata(TrainState.InProgress);
        TrainLifecycleEventMessage? captured = null;
        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => captured = m),
            Arg.Any<CancellationToken>()
        );

        await _hook.OnStarted(metadata, CancellationToken.None);

        await _broadcaster
            .Received(1)
            .PublishAsync(Arg.Any<TrainLifecycleEventMessage>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.EventType.Should().Be("Started");
        captured.TrainName.Should().Be("TestTrain");
        captured.ExternalId.Should().Be(metadata.ExternalId);
        captured.MetadataId.Should().Be(metadata.Id);
    }

    [Test]
    public async Task OnCompleted_PublishesMessageWithCorrectEventType()
    {
        var metadata = CreateMetadata(TrainState.Completed);

        await _hook.OnCompleted(metadata, CancellationToken.None);

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == "Completed"
                    && m.TrainState == "Completed"
                    && m.TrainName == "TestTrain"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task OnFailed_PublishesMessageWithCorrectEventType()
    {
        var metadata = CreateMetadata(TrainState.Failed);

        await _hook.OnFailed(
            metadata,
            new InvalidOperationException("boom"),
            CancellationToken.None
        );

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == "Failed" && m.TrainState == "Failed"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task OnCancelled_PublishesMessageWithCorrectEventType()
    {
        var metadata = CreateMetadata(TrainState.Cancelled);

        await _hook.OnCancelled(metadata, CancellationToken.None);

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == "Cancelled" && m.TrainState == "Cancelled"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task OnStarted_UsesLocalExecutorNotMetadataExecutor()
    {
        var metadata = CreateMetadata();
        // Simulate a cross-process scenario: metadata was created by a different process
        typeof(Metadata).GetProperty("Executor")!.SetValue(metadata, "SomeOtherProcess");

        TrainLifecycleEventMessage? captured = null;
        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => captured = m),
            Arg.Any<CancellationToken>()
        );

        await _hook.OnStarted(metadata, CancellationToken.None);

        captured.Should().NotBeNull();

        // Executor should reflect the broadcasting process, not metadata.Executor
        var expectedExecutor = System.Reflection.Assembly.GetEntryAssembly()?.GetAssemblyProject();
        captured!.Executor.Should().Be(expectedExecutor);
        captured.Executor.Should().NotBe("SomeOtherProcess");
    }

    [Test]
    public async Task OnCompleted_IncludesTimestampFromMetadata()
    {
        var metadata = CreateMetadata(TrainState.Completed);
        var endTime = new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc);
        metadata.EndTime = endTime;

        TrainLifecycleEventMessage? captured = null;
        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => captured = m),
            Arg.Any<CancellationToken>()
        );

        await _hook.OnCompleted(metadata, CancellationToken.None);

        captured!.Timestamp.Should().Be(endTime);
    }

    [Test]
    public async Task OnStarted_UsesUtcNowWhenEndTimeIsNull()
    {
        var metadata = CreateMetadata();
        metadata.EndTime = null;

        TrainLifecycleEventMessage? captured = null;
        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => captured = m),
            Arg.Any<CancellationToken>()
        );

        var before = DateTime.UtcNow;
        await _hook.OnStarted(metadata, CancellationToken.None);
        var after = DateTime.UtcNow;

        captured!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Test]
    public async Task OnFailed_IncludesFailureDetails()
    {
        var metadata = CreateMetadata(TrainState.Failed);
        metadata.AddException(new InvalidOperationException("something broke"));

        TrainLifecycleEventMessage? captured = null;
        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => captured = m),
            Arg.Any<CancellationToken>()
        );

        await _hook.OnFailed(
            metadata,
            new InvalidOperationException("something broke"),
            CancellationToken.None
        );

        captured!.FailureStep.Should().NotBeNull();
        captured.FailureReason.Should().Be("something broke");
    }

    [Test]
    public async Task PassesCancellationTokenToBroadcaster()
    {
        var metadata = CreateMetadata();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await _hook.OnStarted(metadata, token);

        await _broadcaster.Received(1).PublishAsync(Arg.Any<TrainLifecycleEventMessage>(), token);
    }

    [Test]
    public async Task AllEvents_ProduceDistinctEventTypes()
    {
        var metadata = CreateMetadata();
        var eventTypes = new List<string>();

        await _broadcaster.PublishAsync(
            Arg.Do<TrainLifecycleEventMessage>(m => eventTypes.Add(m.EventType)),
            Arg.Any<CancellationToken>()
        );

        await _hook.OnStarted(metadata, CancellationToken.None);
        await _hook.OnCompleted(metadata, CancellationToken.None);
        await _hook.OnFailed(metadata, new Exception(), CancellationToken.None);
        await _hook.OnCancelled(metadata, CancellationToken.None);

        eventTypes.Should().BeEquivalentTo(["Started", "Completed", "Failed", "Cancelled"]);
    }

    [Test]
    public async Task OnStateChanged_PublishesMessageWithCorrectEventType()
    {
        var metadata = CreateMetadata(TrainState.InProgress);

        await _hook.OnStateChanged(metadata, CancellationToken.None);

        await _broadcaster
            .Received(1)
            .PublishAsync(
                Arg.Is<TrainLifecycleEventMessage>(m =>
                    m.EventType == "StateChanged" && m.TrainName == "TestTrain"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
