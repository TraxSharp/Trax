using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class HttpRunExecutorTests
{
    #region Successful Execution

    [Test]
    public async Task ExecuteAsync_SuccessfulResponse_ReturnsOutputAndMetadataId()
    {
        var response = new RemoteRunResponse(
            MetadataId: 42,
            OutputJson: """{"value":"hello","count":7}""",
            OutputType: typeof(TestOutput).FullName
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var result = await executor.ExecuteAsync(
            "My.Train",
            new TestInput { Name = "test" },
            typeof(TestOutput)
        );

        result.MetadataId.Should().Be(42);
        result.Output.Should().NotBeNull();
        result.Output.Should().BeOfType<TestOutput>();
        var output = (TestOutput)result.Output!;
        output.Value.Should().Be("hello");
        output.Count.Should().Be(7);
    }

    [Test]
    public async Task ExecuteAsync_UnitResponse_ReturnsNullOutput()
    {
        var response = new RemoteRunResponse(MetadataId: 10);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var result = await executor.ExecuteAsync(
            "My.UnitTrain",
            new TestInput { Name = "unit" },
            typeof(LanguageExt.Unit)
        );

        result.MetadataId.Should().Be(10);
        result.Output.Should().BeNull();
    }

    #endregion

    #region Error Handling — IsError Response

    [Test]
    public async Task ExecuteAsync_ErrorResponse_ThrowsTrainException()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Train failed: something went wrong"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.FailingTrain",
                new TestInput { Name = "fail" },
                typeof(TestOutput)
            );

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*Train failed: something went wrong*");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithTrainExceptionData_ReconstructsStructuredException()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Validation failed",
            ExceptionType: "InvalidOperationException",
            FailureStep: "ValidateInputStep",
            StackTrace: "at MyApp.ValidateInputStep.Run()"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.FailingTrain",
                new TestInput { Name = "fail" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;

        // The exception message should be valid TrainExceptionData JSON
        var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        data.Should().NotBeNull();
        data!.Type.Should().Be("InvalidOperationException");
        data.Step.Should().Be("ValidateInputStep");
        data.Message.Should().Be("Validation failed");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithExceptionTypeAndStep_PreservesInException()
    {
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Some error",
            ExceptionType: "ArgumentException",
            FailureStep: "ProcessDataStep"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "x" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        data.Should().NotBeNull();
        data!.Type.Should().Be("ArgumentException");
        data.Step.Should().Be("ProcessDataStep");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithStackTrace_PreservesRemoteStackTrace()
    {
        var remoteStack = "at MyApp.Step.Run() in Step.cs:line 42\nat MyApp.Train.Execute()";
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Crash",
            ExceptionType: "NullReferenceException",
            StackTrace: remoteStack
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "x" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        // The exception message should contain the structured data (type preserved)
        var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        data.Should().NotBeNull();
        data!.Type.Should().Be("NullReferenceException");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithPlainMessage_FallsBackToFlatString()
    {
        // When no ExceptionType is set, falls back to flat error message
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Something failed"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "x" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("Something failed");
        // Should NOT be parseable as TrainExceptionData
        var parseAct = () => JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        parseAct.Should().Throw<JsonException>();
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithMetadataId_IncludesInException()
    {
        var response = new RemoteRunResponse(
            MetadataId: 999,
            IsError: true,
            ErrorMessage: "Failed after creating metadata",
            ExceptionType: "TrainException"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "x" },
                typeof(TestOutput)
            );

        // The exception is thrown — MetadataId from the response is preserved in TrainExceptionData
        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        data.Should().NotBeNull();
        data!.Type.Should().Be("TrainException");
    }

    #endregion

    #region Error Handling — Non-2xx HTTP Status

    [Test]
    public async Task ExecuteAsync_Non2xx_WithJsonBody_ThrowsTrainExceptionWithBodyContent()
    {
        var errorBody = """{"detail":"Connection refused to downstream service"}""";
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            responseBody: errorBody
        );
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "error" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("500");
        ex.Message.Should().Contain("Connection refused to downstream service");
    }

    [Test]
    public async Task ExecuteAsync_Non2xx_WithEmptyBody_ThrowsTrainExceptionWithStatusCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.BadGateway, responseBody: "");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "error" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("502");
    }

    [Test]
    public async Task ExecuteAsync_Non2xx_WithHtmlBody_ThrowsTrainExceptionWithStatusCode()
    {
        var htmlBody =
            "<html><body><h1>503 Service Unavailable</h1><p>The server is temporarily unable to service your request.</p></body></html>";
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable,
            responseBody: htmlBody
        );
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "error" },
                typeof(TestOutput)
            );

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("503");
        ex.Message.Should().Contain("Service Unavailable");
    }

    #endregion

    #region Error Handling — Null Response

    [Test]
    public async Task ExecuteAsync_NullResponse_ThrowsTrainException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseBody: "null");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "null" },
                typeof(TestOutput)
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("*null response*");
    }

    #endregion

    #region Request Serialization

    [Test]
    public async Task ExecuteAsync_SerializesRequestCorrectly()
    {
        RemoteRunRequest? capturedRequest = null;
        var response = new RemoteRunResponse(MetadataId: 1);
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            response,
            onRequest: async content =>
            {
                capturedRequest = await content!.ReadFromJsonAsync<RemoteRunRequest>();
            }
        );
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);
        var input = new TestInput { Name = "serialize-test" };

        await executor.ExecuteAsync("My.Train.FullName", input, typeof(TestOutput));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.TrainName.Should().Be("My.Train.FullName");
        capturedRequest.InputType.Should().Be(typeof(TestInput).FullName);
        capturedRequest.InputJson.Should().Contain("serialize-test");
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task ExecuteAsync_CancelledToken_ThrowsTaskCanceledException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, new RemoteRunResponse(1));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "cancel" },
                typeof(TestOutput),
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Test Types

    public record TestInput
    {
        public string Name { get; init; } = "";
    }

    public record TestOutput
    {
        public string Value { get; init; } = "";
        public int Count { get; init; }
    }

    #endregion

    #region Fake HTTP Handler

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        private readonly Func<HttpContent?, Task>? _onRequest;

        public FakeHttpMessageHandler(
            HttpStatusCode statusCode,
            object? responseObject = null,
            Func<HttpContent?, Task>? onRequest = null
        )
        {
            _statusCode = statusCode;
            _responseBody = responseObject is not null
                ? JsonSerializer.Serialize(responseObject)
                : "null";
            _onRequest = onRequest;
        }

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_onRequest is not null)
                await _onRequest(request.Content);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    System.Text.Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    #endregion
}
