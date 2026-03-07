using System.Text.Json;
using FluentAssertions;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests;

[TestFixture]
public class RemoteRunContractTests
{
    #region RemoteRunRequest Serialization

    [Test]
    public void RemoteRunRequest_RoundTrips()
    {
        var request = new RemoteRunRequest(
            TrainName: "My.Namespace.MyTrain",
            InputJson: """{"name":"test"}""",
            InputType: "My.Namespace.MyInput"
        );

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<RemoteRunRequest>(json);

        deserialized.Should().NotBeNull();
        deserialized!.TrainName.Should().Be("My.Namespace.MyTrain");
        deserialized.InputJson.Should().Be("""{"name":"test"}""");
        deserialized.InputType.Should().Be("My.Namespace.MyInput");
    }

    [Test]
    public void RemoteRunRequest_RecordEquality()
    {
        var a = new RemoteRunRequest("Train", "{}", "Input");
        var b = new RemoteRunRequest("Train", "{}", "Input");
        a.Should().Be(b);
    }

    [Test]
    public void RemoteRunRequest_RecordInequality_DifferentTrainName()
    {
        var a = new RemoteRunRequest("TrainA", "{}", "Input");
        var b = new RemoteRunRequest("TrainB", "{}", "Input");
        a.Should().NotBe(b);
    }

    #endregion

    #region RemoteRunResponse Serialization

    [Test]
    public void RemoteRunResponse_SuccessResponse_RoundTrips()
    {
        var response = new RemoteRunResponse(
            MetadataId: 42,
            OutputJson: """{"value":"hello"}""",
            OutputType: "My.Namespace.MyOutput"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(42);
        deserialized.OutputJson.Should().Be("""{"value":"hello"}""");
        deserialized.OutputType.Should().Be("My.Namespace.MyOutput");
        deserialized.IsError.Should().BeFalse();
        deserialized.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void RemoteRunResponse_ErrorResponse_RoundTrips()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Something went wrong"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(0);
        deserialized.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Something went wrong");
        deserialized.OutputJson.Should().BeNull();
        deserialized.OutputType.Should().BeNull();
    }

    [Test]
    public void RemoteRunResponse_UnitResponse_RoundTrips()
    {
        var response = new RemoteRunResponse(MetadataId: 10);

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(10);
        deserialized.OutputJson.Should().BeNull();
        deserialized.OutputType.Should().BeNull();
        deserialized.IsError.Should().BeFalse();
        deserialized.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void RemoteRunResponse_DefaultValues()
    {
        var response = new RemoteRunResponse(99);
        response.OutputJson.Should().BeNull();
        response.OutputType.Should().BeNull();
        response.IsError.Should().BeFalse();
        response.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void RemoteRunResponse_RecordEquality()
    {
        var a = new RemoteRunResponse(42, "json", "type");
        var b = new RemoteRunResponse(42, "json", "type");
        a.Should().Be(b);
    }

    [Test]
    public void RemoteRunResponse_RecordInequality_DifferentMetadataId()
    {
        var a = new RemoteRunResponse(42);
        var b = new RemoteRunResponse(43);
        a.Should().NotBe(b);
    }

    #endregion

    #region Cross-Compatibility (simulate full round-trip)

    [Test]
    public void RemoteRunContracts_FullRoundTrip_SuccessWithTypedOutput()
    {
        // Simulate what HttpRunExecutor sends
        var request = new RemoteRunRequest(
            TrainName: "Trax.Tests.MyTrain",
            InputJson: JsonSerializer.Serialize(new { name = "round-trip-test" }),
            InputType: "Trax.Tests.MyInput"
        );

        // Simulate serializing across the wire
        var requestJson = JsonSerializer.Serialize(request);

        // Simulate what UseTraxRunEndpoint receives and deserializes
        var receivedRequest = JsonSerializer.Deserialize<RemoteRunRequest>(requestJson);
        receivedRequest.Should().NotBeNull();
        receivedRequest!.TrainName.Should().Be("Trax.Tests.MyTrain");

        // Simulate what UseTraxRunEndpoint returns
        var response = new RemoteRunResponse(
            MetadataId: 100,
            OutputJson: """{"result":"success"}""",
            OutputType: "Trax.Tests.MyOutput"
        );

        var responseJson = JsonSerializer.Serialize(response);

        // Simulate what HttpRunExecutor receives
        var receivedResponse = JsonSerializer.Deserialize<RemoteRunResponse>(responseJson);
        receivedResponse.Should().NotBeNull();
        receivedResponse!.MetadataId.Should().Be(100);
        receivedResponse.IsError.Should().BeFalse();
        receivedResponse.OutputJson.Should().Be("""{"result":"success"}""");
    }

    [Test]
    public void RemoteRunContracts_FullRoundTrip_ErrorResponse()
    {
        // Simulate what UseTraxRunEndpoint returns on error
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "TrainException: validation failed"
        );

        var responseJson = JsonSerializer.Serialize(response);
        var receivedResponse = JsonSerializer.Deserialize<RemoteRunResponse>(responseJson);

        receivedResponse.Should().NotBeNull();
        receivedResponse!.IsError.Should().BeTrue();
        receivedResponse.ErrorMessage.Should().Contain("validation failed");
        receivedResponse.MetadataId.Should().Be(0);
    }

    #endregion
}
