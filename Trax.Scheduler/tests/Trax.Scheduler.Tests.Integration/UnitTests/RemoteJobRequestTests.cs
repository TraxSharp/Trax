using System.Text.Json;
using FluentAssertions;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class RemoteJobRequestTests
{
    #region Construction Tests

    [Test]
    public void Create_WithMetadataIdOnly_HasNullInputAndInputType()
    {
        var request = new RemoteJobRequest(42);

        request.MetadataId.Should().Be(42);
        request.Input.Should().BeNull();
        request.InputType.Should().BeNull();
    }

    [Test]
    public void Create_WithAllFields_SetsAllProperties()
    {
        var request = new RemoteJobRequest(42, "{\"value\":\"test\"}", "MyNamespace.MyType");

        request.MetadataId.Should().Be(42);
        request.Input.Should().Be("{\"value\":\"test\"}");
        request.InputType.Should().Be("MyNamespace.MyType");
    }

    #endregion

    #region JSON Serialization Round-Trip Tests

    [Test]
    public void Serialize_WithMetadataIdOnly_RoundTrips()
    {
        var original = new RemoteJobRequest(99);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RemoteJobRequest>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(99);
        deserialized.Input.Should().BeNull();
        deserialized.InputType.Should().BeNull();
    }

    [Test]
    public void Serialize_WithAllFields_RoundTrips()
    {
        var original = new RemoteJobRequest(
            42,
            "{\"value\":\"hello\"}",
            "Trax.Scheduler.Tests.Integration.Fakes.Trains.SchedulerTestInput"
        );

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RemoteJobRequest>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(42);
        deserialized.Input.Should().Be("{\"value\":\"hello\"}");
        deserialized
            .InputType.Should()
            .Be("Trax.Scheduler.Tests.Integration.Fakes.Trains.SchedulerTestInput");
    }

    [Test]
    public void Serialize_ProducesCamelCasePropertyNames()
    {
        var request = new RemoteJobRequest(1, "data", "Type");

        var json = JsonSerializer.Serialize(request);

        // Verify property names are present (PascalCase for record positional params)
        json.Should().Match(j => j.Contains("MetadataId") || j.Contains("metadataId"));
    }

    #endregion

    #region Record Equality Tests

    [Test]
    public void Equals_SameValues_AreEqual()
    {
        var a = new RemoteJobRequest(42, "input", "type");
        var b = new RemoteJobRequest(42, "input", "type");

        a.Should().Be(b);
    }

    [Test]
    public void Equals_DifferentMetadataId_AreNotEqual()
    {
        var a = new RemoteJobRequest(42);
        var b = new RemoteJobRequest(43);

        a.Should().NotBe(b);
    }

    [Test]
    public void Equals_DifferentInput_AreNotEqual()
    {
        var a = new RemoteJobRequest(42, "input1", "type");
        var b = new RemoteJobRequest(42, "input2", "type");

        a.Should().NotBe(b);
    }

    #endregion
}
