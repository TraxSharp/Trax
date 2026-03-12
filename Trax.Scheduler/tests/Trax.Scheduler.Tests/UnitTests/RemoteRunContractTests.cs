using System.Text.Json;
using FluentAssertions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

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
        deserialized.ExceptionType.Should().BeNull();
        deserialized.FailureStep.Should().BeNull();
        deserialized.StackTrace.Should().BeNull();
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
    public void RemoteRunResponse_WithStructuredError_RoundTripsAllFields()
    {
        var response = new RemoteRunResponse(
            MetadataId: 123,
            IsError: true,
            ErrorMessage: "Validation failed",
            ExceptionType: "InvalidOperationException",
            FailureStep: "ValidateInputStep",
            StackTrace: "at MyApp.ValidateInputStep.Run() in Step.cs:line 42"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(123);
        deserialized.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Validation failed");
        deserialized.ExceptionType.Should().Be("InvalidOperationException");
        deserialized.FailureStep.Should().Be("ValidateInputStep");
        deserialized.StackTrace.Should().Contain("ValidateInputStep");
    }

    [Test]
    public void RemoteRunResponse_ErrorWithNullOptionalFields_RoundTripsCleanly()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Generic error",
            ExceptionType: null,
            FailureStep: null,
            StackTrace: null
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Generic error");
        deserialized.ExceptionType.Should().BeNull();
        deserialized.FailureStep.Should().BeNull();
        deserialized.StackTrace.Should().BeNull();
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
        response.ExceptionType.Should().BeNull();
        response.FailureStep.Should().BeNull();
        response.StackTrace.Should().BeNull();
    }

    [Test]
    public void RemoteRunResponse_RecordEquality()
    {
        var a = new RemoteRunResponse(
            42,
            ExternalId: "ext-42",
            OutputJson: "json",
            OutputType: "type"
        );
        var b = new RemoteRunResponse(
            42,
            ExternalId: "ext-42",
            OutputJson: "json",
            OutputType: "type"
        );
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

    #region RemoteJobResponse Serialization

    [Test]
    public void RemoteJobResponse_DefaultValues_IsNotError()
    {
        var response = new RemoteJobResponse(MetadataId: 42);
        response.IsError.Should().BeFalse();
        response.ErrorMessage.Should().BeNull();
        response.ExceptionType.Should().BeNull();
        response.StackTrace.Should().BeNull();
    }

    [Test]
    public void RemoteJobResponse_WithError_HasExpectedFields()
    {
        var response = new RemoteJobResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Train exploded",
            ExceptionType: "TrainException",
            StackTrace: "at MyApp.Train.Run()"
        );

        response.IsError.Should().BeTrue();
        response.ErrorMessage.Should().Be("Train exploded");
        response.ExceptionType.Should().Be("TrainException");
        response.StackTrace.Should().Contain("MyApp.Train.Run");
    }

    [Test]
    public void RemoteJobResponse_RoundTripSerialization_PreservesAllFields()
    {
        var response = new RemoteJobResponse(
            MetadataId: 55,
            IsError: true,
            ErrorMessage: "Something went wrong",
            ExceptionType: "InvalidOperationException",
            StackTrace: "at Step.Run() in Step.cs:line 10"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteJobResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(55);
        deserialized.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Something went wrong");
        deserialized.ExceptionType.Should().Be("InvalidOperationException");
        deserialized.StackTrace.Should().Contain("Step.Run");
    }

    [Test]
    public void RemoteJobResponse_SuccessRoundTrip_PreservesMetadataId()
    {
        var response = new RemoteJobResponse(MetadataId: 100);

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteJobResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(100);
        deserialized.IsError.Should().BeFalse();
    }

    #endregion

    #region Cross-Compatibility (simulate full round-trip)

    [Test]
    public void RemoteRunContracts_FullRoundTrip_SuccessWithTypedOutput()
    {
        var request = new RemoteRunRequest(
            TrainName: "Trax.Tests.MyTrain",
            InputJson: JsonSerializer.Serialize(new { name = "round-trip-test" }),
            InputType: "Trax.Tests.MyInput"
        );

        var requestJson = JsonSerializer.Serialize(request);
        var receivedRequest = JsonSerializer.Deserialize<RemoteRunRequest>(requestJson);
        receivedRequest.Should().NotBeNull();
        receivedRequest!.TrainName.Should().Be("Trax.Tests.MyTrain");

        var response = new RemoteRunResponse(
            MetadataId: 100,
            OutputJson: """{"result":"success"}""",
            OutputType: "Trax.Tests.MyOutput"
        );

        var responseJson = JsonSerializer.Serialize(response);
        var receivedResponse = JsonSerializer.Deserialize<RemoteRunResponse>(responseJson);
        receivedResponse.Should().NotBeNull();
        receivedResponse!.MetadataId.Should().Be(100);
        receivedResponse.IsError.Should().BeFalse();
        receivedResponse.OutputJson.Should().Be("""{"result":"success"}""");
    }

    [Test]
    public void RemoteRunContracts_FullRoundTrip_StructuredErrorResponse()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "validation failed",
            ExceptionType: "TrainException",
            FailureStep: "ValidateStep",
            StackTrace: "at App.ValidateStep.Run()"
        );

        var responseJson = JsonSerializer.Serialize(response);
        var receivedResponse = JsonSerializer.Deserialize<RemoteRunResponse>(responseJson);

        receivedResponse.Should().NotBeNull();
        receivedResponse!.IsError.Should().BeTrue();
        receivedResponse.ErrorMessage.Should().Contain("validation failed");
        receivedResponse.ExceptionType.Should().Be("TrainException");
        receivedResponse.FailureStep.Should().Be("ValidateStep");
        receivedResponse.StackTrace.Should().Contain("ValidateStep");
    }

    #endregion
}
