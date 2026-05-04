using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class HttpRunExecutorTests
{
    private static HttpRunExecutor BuildExecutor(StubHandler handler, RemoteRunOptions? opts = null)
    {
        opts ??= new RemoteRunOptions
        {
            BaseUrl = "https://example.invalid/",
            Timeout = TimeSpan.FromSeconds(5),
            Retry = new HttpRetryOptions { MaxRetries = 0 },
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        return new HttpRunExecutor(client, opts, NullLogger<HttpRunExecutor>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_HappyPath_ReturnsRunTrainResult()
    {
        var handler = new StubHandler(
            (req, ct) =>
            {
                var resp = new RemoteRunResponse(
                    MetadataId: 42,
                    ExternalId: "ext-42",
                    OutputJson: """{"value":"ok"}""",
                    OutputType: typeof(SimpleOutput).FullName,
                    IsError: false
                );
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, resp));
            }
        );
        var executor = BuildExecutor(handler);

        var result = await executor.ExecuteAsync(
            "Trax.X.MyTrain",
            new SimpleInput { Value = "hi" },
            typeof(SimpleOutput)
        );

        result.MetadataId.Should().Be(42);
        result.ExternalId.Should().Be("ext-42");
        result.Output.Should().BeOfType<SimpleOutput>();
        ((SimpleOutput)result.Output!).Value.Should().Be("ok");
    }

    [Test]
    public async Task ExecuteAsync_NullOutputJson_ReturnsResultWithNullOutput()
    {
        var handler = new StubHandler(
            (req, ct) =>
            {
                var resp = new RemoteRunResponse(
                    MetadataId: 1,
                    ExternalId: "ext-1",
                    OutputJson: null,
                    OutputType: null,
                    IsError: false
                );
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, resp));
            }
        );
        var executor = BuildExecutor(handler);

        var result = await executor.ExecuteAsync(
            "Trax.X.MyTrain",
            new SimpleInput(),
            typeof(SimpleOutput)
        );

        result.Output.Should().BeNull();
    }

    [Test]
    public async Task ExecuteAsync_NonSuccessHttp_ThrowsTrainExceptionWithBody()
    {
        var handler = new StubHandler(
            (req, ct) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("internal boom"),
                    }
                )
        );
        var executor = BuildExecutor(handler);

        Func<Task> act = () =>
            executor.ExecuteAsync("Trax.X.MyTrain", new SimpleInput(), typeof(SimpleOutput));

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*HTTP 500*")
            .WithMessage("*internal boom*");
    }

    [Test]
    public async Task ExecuteAsync_NonSuccessEmptyBody_FallsBackToReasonPhrase()
    {
        var handler = new StubHandler(
            (req, ct) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.BadGateway)
                    {
                        Content = new StringContent(""),
                        ReasonPhrase = "Bad Gateway",
                    }
                )
        );
        var executor = BuildExecutor(handler);

        Func<Task> act = () =>
            executor.ExecuteAsync("Trax.X.MyTrain", new SimpleInput(), typeof(SimpleOutput));

        await act.Should().ThrowAsync<TrainException>();
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_ThrowsExceptionWithStructuredJson()
    {
        var handler = new StubHandler(
            (req, ct) =>
            {
                var resp = new RemoteRunResponse(
                    MetadataId: 0,
                    ExternalId: null,
                    OutputJson: null,
                    OutputType: null,
                    IsError: true,
                    ExceptionType: "InvalidOperationException",
                    FailureJunction: "MyJunction",
                    ErrorMessage: "boom"
                );
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, resp));
            }
        );
        var executor = BuildExecutor(handler);

        Func<Task> act = () =>
            executor.ExecuteAsync("Trax.X.MyTrain", new SimpleInput(), typeof(SimpleOutput));

        var ex = await act.Should().ThrowAsync<TrainException>();
        // The structured exception data is JSON-serialized into the message
        ex.Which.Message.Should().Contain("InvalidOperationException");
        ex.Which.Message.Should().Contain("MyJunction");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponseWithoutStructuredFields_ThrowsPlainTrainException()
    {
        var handler = new StubHandler(
            (req, ct) =>
            {
                var resp = new RemoteRunResponse(
                    MetadataId: 0,
                    ExternalId: null,
                    OutputJson: null,
                    OutputType: null,
                    IsError: true,
                    ErrorMessage: "the failure"
                );
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, resp));
            }
        );
        var executor = BuildExecutor(handler);

        Func<Task> act = () =>
            executor.ExecuteAsync("Trax.X.MyTrain", new SimpleInput(), typeof(SimpleOutput));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*the failure*");
    }

    [Test]
    public async Task ExecuteAsync_NullJsonResponse_ThrowsTrainException()
    {
        var handler = new StubHandler(
            (req, ct) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("null", Encoding.UTF8, "application/json"),
                    }
                )
        );
        var executor = BuildExecutor(handler);

        Func<Task> act = () =>
            executor.ExecuteAsync("Trax.X.MyTrain", new SimpleInput(), typeof(SimpleOutput));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*null response*");
    }

    private static HttpResponseMessage JsonResponse<T>(HttpStatusCode code, T payload) =>
        new(code) { Content = JsonContent.Create(payload) };

    private record SimpleInput
    {
        public string Value { get; init; } = "";
    }

    private record SimpleOutput
    {
        public string Value { get; init; } = "";
    }

    private class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>
        > _handler;

        public StubHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler
        ) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => _handler(request, cancellationToken);
    }
}
