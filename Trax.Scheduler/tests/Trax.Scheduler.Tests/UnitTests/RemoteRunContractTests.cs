using System.Text.Json;
using FluentAssertions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The HTTP wire contract for a remote run, pinned by round-trip and by record equality.
///
/// <para>A property rename fails to compile here, since the assertions name every property. A
/// reordering fails the positional constructions below: the executors build these records from
/// interchangeable strings, so a permutation compiles clean and, because JSON binds by name,
/// leaves the wire shape untouched while swapping which value each property carries. Either
/// breaks here rather than in a deployed worker.</para>
///
/// <para>Enforces <c>docs/adr/0001-remote-execution-is-a-json-wire-contract.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-remote-execution-is-a-json-wire-contract.md")]
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
        deserialized!
            .TrainName.Should()
            .Be(
                "My.Namespace.MyTrain",
                "the wire carries the canonical train name, not a compiled type, so the two ends "
                    + "need not ship the same assemblies. See "
                    + "docs/adr/0001-remote-execution-is-a-json-wire-contract.md."
            );
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

    [Test]
    public void RemoteRunRequest_PositionalOrder_IsPinned()
    {
        // Constructed positionally on purpose. HttpRunExecutor and LambdaRunExecutor both build
        // this record from three interchangeable strings, so a permutation of the parameters
        // compiles clean and swaps values on the wire. Named arguments elsewhere in this file
        // would not notice.
        var request = new RemoteRunRequest("the-train-name", "the-input-json", "the-input-type");

        request
            .TrainName.Should()
            .Be(
                "the-train-name",
                "the executors construct RemoteRunRequest positionally from three strings, so "
                    + "reordering its parameters would swap values on the wire without a compile "
                    + "error. See docs/adr/0001-remote-execution-is-a-json-wire-contract.md."
            );
        request.InputJson.Should().Be("the-input-json");
        request.InputType.Should().Be("the-input-type");
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
        deserialized.FailureJunction.Should().BeNull();
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
            FailureJunction: "ValidateInputJunction",
            StackTrace: "at MyApp.ValidateInputJunction.Run() in Junction.cs:line 42"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(123);
        deserialized.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Validation failed");
        deserialized.ExceptionType.Should().Be("InvalidOperationException");
        deserialized.FailureJunction.Should().Be("ValidateInputJunction");
        deserialized.StackTrace.Should().Contain("ValidateInputJunction");
    }

    [Test]
    public void RemoteRunResponse_ErrorWithNullOptionalFields_RoundTripsCleanly()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Generic error",
            ExceptionType: null,
            FailureJunction: null,
            StackTrace: null
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteRunResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Generic error");
        deserialized.ExceptionType.Should().BeNull();
        deserialized.FailureJunction.Should().BeNull();
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
        response.FailureJunction.Should().BeNull();
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

    [Test]
    public void RemoteRunResponse_PositionalOrder_IsPinned()
    {
        // TraxRequestHandler builds the success response positionally, and ExternalId,
        // OutputJson and OutputType are three consecutive nullable strings.
        var response = new RemoteRunResponse(
            42,
            "the-external-id",
            "the-output-json",
            "the-output-type"
        );

        response
            .ExternalId.Should()
            .Be(
                "the-external-id",
                "TraxRequestHandler constructs RemoteRunResponse positionally, so reordering "
                    + "its three consecutive string parameters would swap values on the wire "
                    + "without a compile error. See "
                    + "docs/adr/0001-remote-execution-is-a-json-wire-contract.md."
            );
        response.OutputJson.Should().Be("the-output-json");
        response.OutputType.Should().Be("the-output-type");
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
            StackTrace: "at Junction.Run() in Junction.cs:line 10"
        );

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<RemoteJobResponse>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MetadataId.Should().Be(55);
        deserialized.IsError.Should().BeTrue();
        deserialized.ErrorMessage.Should().Be("Something went wrong");
        deserialized.ExceptionType.Should().Be("InvalidOperationException");
        deserialized.StackTrace.Should().Contain("Junction.Run");
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
            FailureJunction: "ValidateJunction",
            StackTrace: "at App.ValidateJunction.Run()"
        );

        var responseJson = JsonSerializer.Serialize(response);
        var receivedResponse = JsonSerializer.Deserialize<RemoteRunResponse>(responseJson);

        receivedResponse.Should().NotBeNull();
        receivedResponse!.IsError.Should().BeTrue();
        receivedResponse.ErrorMessage.Should().Contain("validation failed");
        receivedResponse.ExceptionType.Should().Be("TrainException");
        receivedResponse.FailureJunction.Should().Be("ValidateJunction");
        receivedResponse.StackTrace.Should().Contain("ValidateJunction");
    }

    #endregion
}
