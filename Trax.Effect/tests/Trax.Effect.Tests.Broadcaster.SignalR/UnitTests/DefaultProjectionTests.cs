using FluentAssertions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

[TestFixture]
public class DefaultProjectionTests
{
    private static TrainLifecycleEventMessage SampleMessage(
        string eventType = "Completed",
        string? failureReason = null
    ) =>
        new(
            MetadataId: 42,
            ExternalId: "ext-abc",
            TrainName: "MyApp.Trains.IDoThingTrain",
            TrainState: "Completed",
            Timestamp: new DateTime(2026, 5, 15, 14, 30, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: failureReason,
            EventType: eventType,
            Executor: "MyApp.Worker",
            Output: "{\"result\":\"ok\"}",
            HostName: "host-1",
            HostEnvironment: "Production"
        );

    [Test]
    public void Project_CopiesAllSixFieldsFromMessage()
    {
        var message = SampleMessage(failureReason: "boom");

        var result = DefaultTraxClientEventProjection.Project(message);

        result.Should().BeOfType<TraxClientEvent>();
        var evt = (TraxClientEvent)result;
        evt.MetadataId.Should().Be(message.MetadataId);
        evt.ExternalId.Should().Be(message.ExternalId);
        evt.TrainName.Should().Be(message.TrainName);
        evt.EventType.Should().Be(message.EventType);
        evt.Timestamp.Should().Be(message.Timestamp);
        evt.FailureReason.Should().Be(message.FailureReason);
    }

    [Test]
    public void Project_DropsTrainStateExecutorOutputHostFields()
    {
        var propertyNames = typeof(TraxClientEvent).GetProperties().Select(p => p.Name).ToHashSet();

        propertyNames
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    nameof(TraxClientEvent.MetadataId),
                    nameof(TraxClientEvent.ExternalId),
                    nameof(TraxClientEvent.TrainName),
                    nameof(TraxClientEvent.EventType),
                    nameof(TraxClientEvent.Timestamp),
                    nameof(TraxClientEvent.FailureReason),
                }
            );
    }

    [Test]
    public void Project_NullFailureReason_PreservesNull()
    {
        var message = SampleMessage(failureReason: null);

        var evt = (TraxClientEvent)DefaultTraxClientEventProjection.Project(message);

        evt.FailureReason.Should().BeNull();
    }
}
