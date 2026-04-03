using System.Net;
using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// End-to-end tests verifying that exception context is preserved across
/// the full remote worker wire boundary: runner catch → structured response →
/// API-side reconstruction → Metadata.AddException() parsing.
/// </summary>
[TestFixture]
public class RemoteErrorRoundTripTests
{
    #region Run Path Round-Trip (TrainExceptionData)

    [Test]
    public void ErrorRoundTrip_TrainExceptionData_PreservedFromRunnerToApiMetadata()
    {
        // Step 1: Simulate a train failure on the runner that produces TrainExceptionData
        var originalData = new TrainExceptionData
        {
            TrainName = "My.Namespace.IMyTrain",
            TrainExternalId = "ext-abc",
            Type = "ArgumentException",
            Junction = "ValidateInputJunction",
            Message = "Input 'email' was null",
        };
        var trainException = new TrainException(JsonSerializer.Serialize(originalData));

        // Step 2: TraxRequestHandler.BuildErrorResponse processes the exception
        var runnerResponse = TraxRequestHandler.BuildErrorResponse(trainException);

        runnerResponse.IsError.Should().BeTrue();
        runnerResponse.ExceptionType.Should().Be("ArgumentException");
        runnerResponse.FailureJunction.Should().Be("ValidateInputJunction");
        runnerResponse.ErrorMessage.Should().Be("Input 'email' was null");

        // Step 3: Serialize across the wire (runner → API)
        var wireJson = JsonSerializer.Serialize(runnerResponse);
        var apiResponse = JsonSerializer.Deserialize<RemoteRunResponse>(wireJson);
        apiResponse.Should().NotBeNull();

        // Step 4: HttpRunExecutor builds a TrainException from the error response
        // (simulating BuildExceptionFromErrorResponse logic)
        var reconstructedData = new TrainExceptionData
        {
            TrainName = "",
            TrainExternalId = "",
            Type = apiResponse!.ExceptionType!,
            Junction = apiResponse.FailureJunction ?? "Unknown",
            Message = apiResponse.ErrorMessage ?? "Remote train execution failed",
        };
        var reconstructedJson = JsonSerializer.Serialize(reconstructedData);
        var reconstructedException = new TrainException(reconstructedJson);

        // Step 5: Metadata.AddException() on the API side parses it correctly
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "My.Namespace.IMyTrain",
                ExternalId = "test",
                Input = null,
            }
        );
        metadata.AddException(reconstructedException);

        metadata.FailureException.Should().Be("ArgumentException");
        metadata.FailureJunction.Should().Be("ValidateInputJunction");
        metadata.FailureReason.Should().Be("Input 'email' was null");
    }

    [Test]
    public void ErrorRoundTrip_PlainException_ProducesUsableMetadataFields()
    {
        // Step 1: Plain exception on the runner (not TrainExceptionData)
        var plainException = new InvalidOperationException("Connection timed out");

        // Step 2: BuildErrorResponse processes the plain exception
        var runnerResponse = TraxRequestHandler.BuildErrorResponse(plainException);

        runnerResponse.IsError.Should().BeTrue();
        runnerResponse.ExceptionType.Should().Be("InvalidOperationException");
        runnerResponse.ErrorMessage.Should().Be("Connection timed out");
        runnerResponse.FailureJunction.Should().BeNull();

        // Step 3: Wire round-trip
        var wireJson = JsonSerializer.Serialize(runnerResponse);
        var apiResponse = JsonSerializer.Deserialize<RemoteRunResponse>(wireJson)!;

        // Step 4: Reconstruct exception (as HttpRunExecutor does)
        var reconstructedData = new TrainExceptionData
        {
            TrainName = "",
            TrainExternalId = "",
            Type = apiResponse.ExceptionType!,
            Junction = apiResponse.FailureJunction ?? "Unknown",
            Message = apiResponse.ErrorMessage ?? "Remote train execution failed",
        };
        var reconstructedJson = JsonSerializer.Serialize(reconstructedData);
        var reconstructedException = new TrainException(reconstructedJson);

        // Step 5: Metadata.AddException() parses it
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "My.Namespace.IMyTrain",
                ExternalId = "test",
                Input = null,
            }
        );
        metadata.AddException(reconstructedException);

        metadata.FailureException.Should().Be("InvalidOperationException");
        metadata.FailureReason.Should().Be("Connection timed out");
        metadata.FailureJunction.Should().Be("Unknown");
    }

    #endregion

    #region Queue Path Round-Trip

    [Test]
    public void ErrorRoundTrip_JobPath_ErrorDetailsPreservedInResponse()
    {
        // Step 1: Job execution fails on the runner
        var exception = new InvalidOperationException("Database connection lost");

        // Step 2: UseTraxJobRunner endpoint catches it and builds RemoteJobResponse
        var jobResponse = new RemoteJobResponse(
            MetadataId: 42,
            IsError: true,
            ErrorMessage: exception.Message,
            ExceptionType: exception.GetType().Name,
            StackTrace: exception.StackTrace
        );

        // Step 3: Wire round-trip
        var wireJson = JsonSerializer.Serialize(jobResponse);
        var apiResponse = JsonSerializer.Deserialize<RemoteJobResponse>(wireJson)!;

        // Step 4: HttpJobSubmitter reads the response
        apiResponse.IsError.Should().BeTrue();
        apiResponse.MetadataId.Should().Be(42);
        apiResponse.ErrorMessage.Should().Be("Database connection lost");
        apiResponse.ExceptionType.Should().Be("InvalidOperationException");
    }

    [Test]
    public void ErrorRoundTrip_JobPath_SuccessResponsePreservesMetadataId()
    {
        var jobResponse = new RemoteJobResponse(MetadataId: 999);

        var wireJson = JsonSerializer.Serialize(jobResponse);
        var apiResponse = JsonSerializer.Deserialize<RemoteJobResponse>(wireJson)!;

        apiResponse.IsError.Should().BeFalse();
        apiResponse.MetadataId.Should().Be(999);
        apiResponse.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region HttpRunExecutor Error Reconstruction

    [Test]
    public void ErrorRoundTrip_HttpRunExecutor_ReconstructedExceptionIsParsableByMetadata()
    {
        // This tests the exact reconstruction logic from HttpRunExecutor.BuildExceptionFromErrorResponse
        // by using the same approach: structured fields → TrainExceptionData JSON → TrainException

        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Null reference in step",
            ExceptionType: "NullReferenceException",
            FailureJunction: "LoadDataJunction",
            StackTrace: "at App.LoadDataJunction.Run()"
        );

        // Reconstruct as HttpRunExecutor does
        var data = new TrainExceptionData
        {
            TrainName = "",
            TrainExternalId = "",
            Type = response.ExceptionType!,
            Junction = response.FailureJunction ?? "Unknown",
            Message = response.ErrorMessage ?? "Remote train execution failed",
        };
        var exception = new TrainException(JsonSerializer.Serialize(data));

        // Verify Metadata.AddException() handles it
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );
        metadata.AddException(exception);

        metadata.FailureException.Should().Be("NullReferenceException");
        metadata.FailureJunction.Should().Be("LoadDataJunction");
        metadata.FailureReason.Should().Be("Null reference in step");
    }

    [Test]
    public void ErrorRoundTrip_HttpRunExecutor_FlatErrorFallsBackGracefully()
    {
        // When response has no ExceptionType (older runner), falls back to flat message
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Something failed"
        );

        // Flat error path (no ExceptionType)
        var exception = new TrainException(
            $"Remote train execution failed: {response.ErrorMessage}"
        );

        // Verify Metadata.AddException() still works
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );
        metadata.AddException(exception);

        metadata.FailureException.Should().Be("TrainException");
        metadata.FailureReason.Should().Contain("Something failed");
    }

    #endregion

    #region AddException Exception.Data Priority

    [Test]
    public void AddException_WithExceptionDataDictionary_PrefersStructuredData()
    {
        // Arrange — attach TrainExceptionData via Exception.Data (local execution path)
        var exception = new InvalidOperationException("raw message");
        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-1",
            Type = "InvalidOperationException",
            Junction = "MyJunction",
            Message = "structured message",
            StackTrace = "at MyApp.MyJunction.Run()",
        };
        exception.Data["TrainExceptionData"] = data;

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );

        // Act
        metadata.AddException(exception);

        // Assert — should use the structured data, not the raw exception
        metadata.FailureException.Should().Be("InvalidOperationException");
        metadata.FailureJunction.Should().Be("MyJunction");
        metadata.FailureReason.Should().Be("structured message");
        metadata.StackTrace.Should().Be("at MyApp.MyJunction.Run()");
    }

    [Test]
    public void AddException_WithJsonMessage_FallsBackToDeserialization()
    {
        // Arrange — JSON in message (remote execution path), no Data dictionary
        var jsonData = new TrainExceptionData
        {
            TrainName = "RemoteTrain",
            TrainExternalId = "ext-2",
            Type = "ArgumentException",
            Junction = "RemoteJunction",
            Message = "bad input",
            StackTrace = "at Remote.Junction.Run()",
        };
        var exception = new TrainException(JsonSerializer.Serialize(jsonData));

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );

        // Act
        metadata.AddException(exception);

        // Assert
        metadata.FailureException.Should().Be("ArgumentException");
        metadata.FailureJunction.Should().Be("RemoteJunction");
        metadata.FailureReason.Should().Be("bad input");
        metadata.StackTrace.Should().Be("at Remote.Junction.Run()");
    }

    [Test]
    public void AddException_WithExceptionDataAndStackTrace_PrefersOriginalStackTrace()
    {
        // Arrange — exception has a real stack trace from being thrown,
        // but TrainExceptionData has the original (correct) stack trace
        Exception thrownException;
        try
        {
            throw new InvalidOperationException("test");
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-1",
            Type = "InvalidOperationException",
            Junction = "OriginalJunction",
            Message = "test",
            StackTrace = "at Original.Junction.Run() in /app/Junction.cs:line 10",
        };
        thrownException.Data["TrainExceptionData"] = data;

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );

        // Act
        metadata.AddException(thrownException);

        // Assert — should prefer the TrainExceptionData.StackTrace over the exception's own
        metadata.StackTrace.Should().Be("at Original.Junction.Run() in /app/Junction.cs:line 10");
        metadata.StackTrace.Should().NotBe(thrownException.StackTrace);
    }

    [Test]
    public void AddException_WithExceptionDataNoStackTrace_FallsBackToExceptionStackTrace()
    {
        // Arrange — TrainExceptionData has null StackTrace (backwards compat)
        Exception thrownException;
        try
        {
            throw new InvalidOperationException("test");
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        var data = new TrainExceptionData
        {
            TrainName = "MyTrain",
            TrainExternalId = "ext-1",
            Type = "InvalidOperationException",
            Junction = "MyJunction",
            Message = "test",
            StackTrace = null,
        };
        thrownException.Data["TrainExceptionData"] = data;

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );

        // Act
        metadata.AddException(thrownException);

        // Assert — should fall back to trainException.StackTrace
        metadata.StackTrace.Should().Be(thrownException.StackTrace);
        metadata.StackTrace.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void AddException_PlainException_UsesExceptionDirectly()
    {
        // Arrange — plain exception with no Trax context at all
        Exception thrownException;
        try
        {
            throw new InvalidOperationException("plain error");
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Test.Train",
                ExternalId = "test",
                Input = null,
            }
        );

        // Act
        metadata.AddException(thrownException);

        // Assert
        metadata.FailureException.Should().Be("InvalidOperationException");
        metadata.FailureReason.Should().Be("plain error");
        metadata.FailureJunction.Should().Be("TrainException");
        metadata.StackTrace.Should().Be(thrownException.StackTrace);
    }

    #endregion
}
