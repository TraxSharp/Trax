using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

[TestFixture]
public class TrainLifecycleEventMessageTests
{
    [Test]
    public void SerializesAndDeserializesCorrectly()
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: 42,
            ExternalId: "ext-123",
            TrainName: "MyTrain",
            TrainState: "Completed",
            Timestamp: new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: "TestWorker",
            Output: "{\"result\":\"success\"}"
        );

        var json = JsonSerializer.Serialize(message);
        var deserialized = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(42);
        deserialized.ExternalId.Should().Be("ext-123");
        deserialized.TrainName.Should().Be("MyTrain");
        deserialized.TrainState.Should().Be("Completed");
        deserialized.EventType.Should().Be("Completed");
        deserialized.Executor.Should().Be("TestWorker");
        deserialized.FailureJunction.Should().BeNull();
        deserialized.FailureReason.Should().BeNull();
        deserialized.Output.Should().Be("{\"result\":\"success\"}");
    }

    [Test]
    public void SerializesWithFailureDetails()
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: 1,
            ExternalId: "fail-ext",
            TrainName: "FailingTrain",
            TrainState: "Failed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: "JunctionA",
            FailureReason: "Something went wrong",
            EventType: "Failed",
            Executor: "Worker1",
            Output: null
        );

        var json = JsonSerializer.Serialize(message);
        var deserialized = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json);

        deserialized!.FailureJunction.Should().Be("JunctionA");
        deserialized.FailureReason.Should().Be("Something went wrong");
    }

    [Test]
    public void SerializesNullExecutor()
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: 1,
            ExternalId: "no-exec",
            TrainName: "OrphanTrain",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: null,
            Output: null
        );

        var json = JsonSerializer.Serialize(message);
        var deserialized = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json);

        deserialized!.Executor.Should().BeNull();
    }

    [Test]
    public void UsesJsonPropertyNames()
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: 1,
            ExternalId: "test",
            TrainName: "Train",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: "Worker",
            Output: "{\"data\":1}"
        );

        var json = JsonSerializer.Serialize(message);
        json.Should().Contain("\"metadataId\":");
        json.Should().Contain("\"externalId\":");
        json.Should().Contain("\"trainName\":");
        json.Should().Contain("\"trainState\":");
        json.Should().Contain("\"eventType\":");
        json.Should().Contain("\"executor\":");
        json.Should().Contain("\"output\":");
    }

    [Test]
    public void DataChangedMessage_RoundTripsChangeDomain()
    {
        // Data-change signals ride the same transport as lifecycle events, tagged with the
        // DataChanged event type and the domain. The domain must survive the JSON boundary.
        var message = new TrainLifecycleEventMessage(
            MetadataId: 0,
            ExternalId: string.Empty,
            TrainName: string.Empty,
            TrainState: string.Empty,
            Timestamp: new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: TrainLifecycleEventMessage.DataChangedEventType,
            Executor: "Hub",
            Output: null,
            ChangeDomain: "WorkQueue"
        );

        var json = JsonSerializer.Serialize(message);
        json.Should().Contain("\"changeDomain\":\"WorkQueue\"");

        var deserialized = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(json);
        deserialized!.EventType.Should().Be(TrainLifecycleEventMessage.DataChangedEventType);
        deserialized.ChangeDomain.Should().Be("WorkQueue");
    }

    [Test]
    public void LifecycleMessage_HasNullChangeDomainByDefault()
    {
        // Existing lifecycle events don't set ChangeDomain; the new optional field must default to
        // null so older/other publishers stay wire-compatible.
        var message = new TrainLifecycleEventMessage(
            MetadataId: 1,
            ExternalId: "ext",
            TrainName: "Train",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: "Worker",
            Output: null
        );

        message.ChangeDomain.Should().BeNull();

        var roundTripped = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(
            JsonSerializer.Serialize(message)
        );
        roundTripped!.ChangeDomain.Should().BeNull();
    }

    [Test]
    public void RecordEquality_WorksCorrectly()
    {
        var timestamp = DateTime.UtcNow;
        var msg1 = new TrainLifecycleEventMessage(
            1,
            "ext",
            "Train",
            "Completed",
            timestamp,
            null,
            null,
            "Completed",
            "Worker",
            null
        );
        var msg2 = new TrainLifecycleEventMessage(
            1,
            "ext",
            "Train",
            "Completed",
            timestamp,
            null,
            null,
            "Completed",
            "Worker",
            null
        );
        var msg3 = new TrainLifecycleEventMessage(
            2,
            "ext",
            "Train",
            "Completed",
            timestamp,
            null,
            null,
            "Completed",
            "Worker",
            null
        );

        msg1.Should().Be(msg2);
        msg1.Should().NotBe(msg3);
    }
}
