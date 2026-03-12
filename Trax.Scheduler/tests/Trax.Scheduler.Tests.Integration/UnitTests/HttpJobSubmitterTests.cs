using System.Net;
using System.Text.Json;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class HttpJobSubmitterTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    #region Helpers

    private static (HttpJobSubmitter submitter, MockHttpMessageHandler handler) CreateSubmitter(
        HttpStatusCode responseStatus = HttpStatusCode.OK,
        string? responseBody = null
    )
    {
        var handler = new MockHttpMessageHandler(responseStatus, responseBody);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.example.com/trax/execute"),
        };
        return (new HttpJobSubmitter(httpClient), handler);
    }

    private static string SerializeSuccessResponse(long metadataId) =>
        JsonSerializer.Serialize(new RemoteJobResponse(metadataId));

    private static string SerializeErrorResponse(
        string errorMessage,
        string? exceptionType = null,
        string? stackTrace = null
    ) =>
        JsonSerializer.Serialize(
            new RemoteJobResponse(
                0,
                IsError: true,
                ErrorMessage: errorMessage,
                ExceptionType: exceptionType,
                StackTrace: stackTrace
            )
        );

    #endregion

    #region EnqueueAsync(metadataId) Tests

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_ReturnsHttpPrefixedJobId()
    {
        var (submitter, _) = CreateSubmitter(responseBody: SerializeSuccessResponse(42));

        var jobId = await submitter.EnqueueAsync(42);

        jobId.Should().StartWith("http-");
        jobId.Should().HaveLength("http-".Length + 32);
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_PostsCorrectPayload()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(42));

        await submitter.EnqueueAsync(42);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request.Should().NotBeNull();
        request!.MetadataId.Should().Be(42);
        request.Input.Should().BeNull();
        request.InputType.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_PostsToBaseAddress()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(42));

        await submitter.EnqueueAsync(42);

        handler.LastRequest.Should().NotBeNull();
        handler
            .LastRequest!.RequestUri!.ToString()
            .Should()
            .Be("https://test.example.com/trax/execute");
    }

    #endregion

    #region EnqueueAsync(metadataId, input) Tests

    [Test]
    public async Task EnqueueAsync_WithInput_SerializesInputAsJson()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(50));
        var input = new SchedulerTestInput { Value = "hello-world" };

        await submitter.EnqueueAsync(50, input);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request.Should().NotBeNull();
        request!.MetadataId.Should().Be(50);
        request.Input.Should().NotBeNull();
        request.Input.Should().Contain("hello-world");
        request.InputType.Should().Be(typeof(SchedulerTestInput).FullName);
    }

    [Test]
    public async Task EnqueueAsync_WithInput_UsesManifestPropertiesSerialization()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(1));
        var input = new SchedulerTestInput { Value = "test-value" };

        await submitter.EnqueueAsync(1, input);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        var expectedJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );
        request!.Input.Should().Be(expectedJson);
    }

    [Test]
    public async Task EnqueueAsync_WithInput_ReturnsUniqueJobIds()
    {
        var (submitter, _) = CreateSubmitter(responseBody: SerializeSuccessResponse(1));
        var input = new SchedulerTestInput { Value = "test" };

        var jobId1 = await submitter.EnqueueAsync(1, input);
        var jobId2 = await submitter.EnqueueAsync(2, input);

        jobId1.Should().NotBe(jobId2);
    }

    #endregion

    #region EnqueueAsync with CancellationToken Tests

    [Test]
    public async Task EnqueueAsync_WithCancellationToken_PostsCorrectPayload()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(99));
        using var cts = new CancellationTokenSource();

        await submitter.EnqueueAsync(99, cts.Token);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request!.MetadataId.Should().Be(99);
        request.Input.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithInputAndCancellationToken_PostsCorrectPayload()
    {
        var (submitter, handler) = CreateSubmitter(responseBody: SerializeSuccessResponse(77));
        var input = new SchedulerTestInput { Value = "with-ct" };
        using var cts = new CancellationTokenSource();

        await submitter.EnqueueAsync(77, input, cts.Token);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request!.MetadataId.Should().Be(77);
        request.Input.Should().Contain("with-ct");
        request.InputType.Should().Be(typeof(SchedulerTestInput).FullName);
    }

    [Test]
    public void EnqueueAsync_WhenCancelled_ThrowsOperationCancelledException()
    {
        var (submitter, _) = CreateSubmitter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await submitter.EnqueueAsync(1, cts.Token);
        act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Error Handling — Non-2xx HTTP Status

    [Test]
    public async Task EnqueueAsync_WhenRemoteReturns500_ThrowsTrainExceptionWithBody()
    {
        var errorBody = """{"detail":"Internal server error: database timeout"}""";
        var (submitter, _) = CreateSubmitter(HttpStatusCode.InternalServerError, errorBody);

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("500");
        ex.Message.Should().Contain("database timeout");
    }

    [Test]
    public async Task EnqueueAsync_WhenRemoteReturns401_ThrowsTrainExceptionWithBody()
    {
        var (submitter, _) = CreateSubmitter(HttpStatusCode.Unauthorized, "Unauthorized");

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("401");
    }

    [Test]
    public async Task EnqueueAsync_WhenRemoteReturns404_ThrowsTrainExceptionWithBody()
    {
        var (submitter, _) = CreateSubmitter(HttpStatusCode.NotFound, "Not Found");

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("404");
    }

    [Test]
    public async Task EnqueueAsync_WithInput_WhenRemoteReturns500_ThrowsTrainException()
    {
        var (submitter, _) = CreateSubmitter(HttpStatusCode.InternalServerError, "Server Error");
        var input = new SchedulerTestInput { Value = "test" };

        var act = async () => await submitter.EnqueueAsync(42, input);
        await act.Should().ThrowAsync<TrainException>();
    }

    [Test]
    public async Task EnqueueAsync_Non2xx_WithEmptyBody_ThrowsTrainExceptionWithStatusCode()
    {
        var (submitter, _) = CreateSubmitter(HttpStatusCode.BadGateway, "");

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("502");
    }

    [Test]
    public async Task EnqueueAsync_Non2xx_WithHtmlBody_ThrowsTrainExceptionWithTruncatedBody()
    {
        var hugeHtml = new string('x', 5000);
        var (submitter, _) = CreateSubmitter(HttpStatusCode.InternalServerError, hugeHtml);

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("500");
        ex.Message.Should().Contain("truncated");
        ex.Message.Length.Should().BeLessThan(3000);
    }

    #endregion

    #region Error Handling — IsError Response

    [Test]
    public async Task EnqueueAsync_SuccessResponse_WithIsErrorTrue_ThrowsTrainExceptionWithDetails()
    {
        var (submitter, _) = CreateSubmitter(
            responseBody: SerializeErrorResponse(
                "Train execution failed: validation error",
                "InvalidOperationException"
            )
        );

        var act = async () => await submitter.EnqueueAsync(42);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("validation error");
        ex.Message.Should().Contain("InvalidOperationException");
    }

    [Test]
    public async Task EnqueueAsync_SuccessResponse_WithIsErrorFalse_ReturnsJobId()
    {
        var (submitter, _) = CreateSubmitter(responseBody: SerializeSuccessResponse(42));

        var jobId = await submitter.EnqueueAsync(42);

        jobId.Should().StartWith("http-");
    }

    [Test]
    public async Task EnqueueAsync_WithInput_SuccessResponse_WithIsErrorTrue_ThrowsTrainExceptionWithDetails()
    {
        var (submitter, _) = CreateSubmitter(
            responseBody: SerializeErrorResponse("Step failed", "TrainException")
        );
        var input = new SchedulerTestInput { Value = "test" };

        var act = async () => await submitter.EnqueueAsync(42, input);

        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("Step failed");
    }

    #endregion

    #region Multiple Calls Tests

    [Test]
    public async Task EnqueueAsync_MultipleCalls_EachProducesUniqueJobId()
    {
        var (submitter, _) = CreateSubmitter(responseBody: SerializeSuccessResponse(0));
        var jobIds = new HashSet<string>();

        for (var i = 0; i < 10; i++)
        {
            var jobId = await submitter.EnqueueAsync(i);
            jobIds.Add(jobId);
        }

        jobIds.Should().HaveCount(10);
    }

    #endregion

    #region MockHttpMessageHandler

    private class MockHttpMessageHandler(HttpStatusCode responseStatus, string? responseBody = null)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> AllRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? content = null;
            if (request.Content is not null)
                content = await request.Content.ReadAsStringAsync(cancellationToken);

            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (content is not null)
                clone.Content = new StringContent(
                    content,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

            LastRequest = clone;
            AllRequests.Add(clone);

            var response = new HttpResponseMessage(responseStatus);

            if (responseBody is not null)
                response.Content = new StringContent(
                    responseBody,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

            return response;
        }
    }

    #endregion
}
