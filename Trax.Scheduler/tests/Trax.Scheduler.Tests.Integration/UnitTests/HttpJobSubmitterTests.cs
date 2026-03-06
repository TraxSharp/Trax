using System.Net;
using System.Text.Json;
using FluentAssertions;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Examples.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class HttpJobSubmitterTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    #region Helpers

    private static (HttpJobSubmitter submitter, MockHttpMessageHandler handler) CreateSubmitter(
        HttpStatusCode responseStatus = HttpStatusCode.OK
    )
    {
        var handler = new MockHttpMessageHandler(responseStatus);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.example.com/trax/execute"),
        };
        return (new HttpJobSubmitter(httpClient), handler);
    }

    #endregion

    #region EnqueueAsync(metadataId) Tests

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_ReturnsHttpPrefixedJobId()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter();

        // Act
        var jobId = await submitter.EnqueueAsync(42);

        // Assert
        jobId.Should().StartWith("http-");
        jobId.Should().HaveLength("http-".Length + 32); // "http-" + 32 hex chars
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_PostsCorrectPayload()
    {
        // Arrange
        var (submitter, handler) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
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
        // Arrange
        var (submitter, handler) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
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
        // Arrange
        var (submitter, handler) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "hello-world" };

        // Act
        await submitter.EnqueueAsync(50, input);

        // Assert
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
        // Arrange
        var (submitter, handler) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "test-value" };

        // Act
        await submitter.EnqueueAsync(1, input);

        // Assert
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        // Verify the input JSON uses the same serialization as PostgresJobSubmitter
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
        // Arrange
        var (submitter, _) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "test" };

        // Act
        var jobId1 = await submitter.EnqueueAsync(1, input);
        var jobId2 = await submitter.EnqueueAsync(2, input);

        // Assert
        jobId1.Should().NotBe(jobId2);
    }

    #endregion

    #region EnqueueAsync with CancellationToken Tests

    [Test]
    public async Task EnqueueAsync_WithCancellationToken_PostsCorrectPayload()
    {
        // Arrange
        var (submitter, handler) = CreateSubmitter();
        using var cts = new CancellationTokenSource();

        // Act
        await submitter.EnqueueAsync(99, cts.Token);

        // Assert
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request!.MetadataId.Should().Be(99);
        request.Input.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithInputAndCancellationToken_PostsCorrectPayload()
    {
        // Arrange
        var (submitter, handler) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "with-ct" };
        using var cts = new CancellationTokenSource();

        // Act
        await submitter.EnqueueAsync(77, input, cts.Token);

        // Assert
        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(body, WebOptions);

        request!.MetadataId.Should().Be(77);
        request.Input.Should().Contain("with-ct");
        request.InputType.Should().Be(typeof(SchedulerTestInput).FullName);
    }

    [Test]
    public void EnqueueAsync_WhenCancelled_ThrowsOperationCancelledException()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(1, cts.Token);
        act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void EnqueueAsync_WhenRemoteReturns500_ThrowsHttpRequestException()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter(HttpStatusCode.InternalServerError);

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public void EnqueueAsync_WhenRemoteReturns401_ThrowsHttpRequestException()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter(HttpStatusCode.Unauthorized);

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public void EnqueueAsync_WhenRemoteReturns404_ThrowsHttpRequestException()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter(HttpStatusCode.NotFound);

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public void EnqueueAsync_WithInput_WhenRemoteReturns500_ThrowsHttpRequestException()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter(HttpStatusCode.InternalServerError);
        var input = new SchedulerTestInput { Value = "test" };

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42, input);
        act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Multiple Calls Tests

    [Test]
    public async Task EnqueueAsync_MultipleCalls_EachProducesUniqueJobId()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter();
        var jobIds = new HashSet<string>();

        // Act
        for (var i = 0; i < 10; i++)
        {
            var jobId = await submitter.EnqueueAsync(i);
            jobIds.Add(jobId);
        }

        // Assert
        jobIds.Should().HaveCount(10);
    }

    #endregion

    #region MockHttpMessageHandler

    private class MockHttpMessageHandler(HttpStatusCode responseStatus) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> AllRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read and buffer the content so it can be read later
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

            return new HttpResponseMessage(responseStatus);
        }
    }

    #endregion
}
