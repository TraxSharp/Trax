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
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 42,
            OutputJson: """{"value":"hello","count":7}""",
            OutputType: typeof(TestOutput).FullName
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        // Act
        var result = await executor.ExecuteAsync(
            "My.Train",
            new TestInput { Name = "test" },
            typeof(TestOutput)
        );

        // Assert
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
        // Arrange
        var response = new RemoteRunResponse(MetadataId: 10);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        // Act
        var result = await executor.ExecuteAsync(
            "My.UnitTrain",
            new TestInput { Name = "unit" },
            typeof(LanguageExt.Unit)
        );

        // Assert
        result.MetadataId.Should().Be(10);
        result.Output.Should().BeNull();
    }

    #endregion

    #region Error Handling

    [Test]
    public async Task ExecuteAsync_ErrorResponse_ThrowsTrainException()
    {
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Train failed: something went wrong"
        );
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.FailingTrain",
                new TestInput { Name = "fail" },
                typeof(TestOutput)
            );

        // Assert
        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*Train failed: something went wrong*");
    }

    [Test]
    public async Task ExecuteAsync_HttpErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "error" },
                typeof(TestOutput)
            );

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task ExecuteAsync_NullResponse_ThrowsTrainException()
    {
        // Arrange — response body is "null"
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseBody: "null");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "null" },
                typeof(TestOutput)
            );

        // Assert
        await act.Should().ThrowAsync<TrainException>().WithMessage("*null response*");
    }

    #endregion

    #region Request Serialization

    [Test]
    public async Task ExecuteAsync_SerializesRequestCorrectly()
    {
        // Arrange
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

        // Act
        await executor.ExecuteAsync("My.Train.FullName", input, typeof(TestOutput));

        // Assert
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
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, new RemoteRunResponse(1));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var executor = new HttpRunExecutor(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestInput { Name = "cancel" },
                typeof(TestOutput),
                cts.Token
            );

        // Assert
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
