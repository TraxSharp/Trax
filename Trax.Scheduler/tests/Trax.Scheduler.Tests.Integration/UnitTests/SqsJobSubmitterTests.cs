using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Trax.Effect.Utils;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Configuration;
using Trax.Scheduler.Sqs.Services;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class SqsJobSubmitterTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    #region Helpers

    private static (SqsJobSubmitter submitter, MockSqsClient client) CreateSubmitter(
        string queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
        string? messageGroupId = null
    )
    {
        var options = new SqsWorkerOptions { QueueUrl = queueUrl, MessageGroupId = messageGroupId };
        var client = new MockSqsClient();
        return (new SqsJobSubmitter(client, options), client);
    }

    #endregion

    #region EnqueueAsync(metadataId) Tests

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_ReturnsSqsPrefixedJobId()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter();

        // Act
        var jobId = await submitter.EnqueueAsync(42);

        // Assert
        jobId.Should().StartWith("sqs-");
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_SendsCorrectPayload()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest.Should().NotBeNull();
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            client.LastRequest!.MessageBody,
            WebOptions
        );

        request.Should().NotBeNull();
        request!.MetadataId.Should().Be(42);
        request.Input.Should().BeNull();
        request.InputType.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_SendsToConfiguredQueueUrl()
    {
        // Arrange
        var queueUrl = "https://sqs.eu-west-1.amazonaws.com/999999999/my-queue";
        var (submitter, client) = CreateSubmitter(queueUrl);

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest!.QueueUrl.Should().Be(queueUrl);
    }

    #endregion

    #region EnqueueAsync(metadataId, input) Tests

    [Test]
    public async Task EnqueueAsync_WithInput_SerializesInputAsJson()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "hello-world" };

        // Act
        await submitter.EnqueueAsync(50, input);

        // Assert
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            client.LastRequest!.MessageBody,
            WebOptions
        );

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
        var (submitter, client) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "test-value" };

        // Act
        await submitter.EnqueueAsync(1, input);

        // Assert
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            client.LastRequest!.MessageBody,
            WebOptions
        );

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

    #region CancellationToken Tests

    [Test]
    public async Task EnqueueAsync_WithCancellationToken_SendsCorrectPayload()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        using var cts = new CancellationTokenSource();

        // Act
        await submitter.EnqueueAsync(99, cts.Token);

        // Assert
        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            client.LastRequest!.MessageBody,
            WebOptions
        );

        request!.MetadataId.Should().Be(99);
        request.Input.Should().BeNull();
    }

    [Test]
    public void EnqueueAsync_WhenCancelled_ThrowsOperationCancelledException()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        client.ThrowOnSend = true;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(1, cts.Token);
        act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void EnqueueAsync_WhenSqsThrows_PropagatesException()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        client.ThrowOnSend = true;

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        act.Should().ThrowAsync<AmazonSQSException>();
    }

    #endregion

    #region FIFO Queue Tests

    [Test]
    public async Task EnqueueAsync_FifoQueue_SetsMessageGroupIdAndDeduplicationId()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter(
            "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs.fifo"
        );

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest!.MessageGroupId.Should().NotBeNullOrEmpty();
        client.LastRequest.MessageDeduplicationId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task EnqueueAsync_FifoQueue_UsesCustomMessageGroupId()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter(
            "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs.fifo",
            messageGroupId: "my-group"
        );

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest!.MessageGroupId.Should().Be("my-group");
    }

    [Test]
    public async Task EnqueueAsync_StandardQueue_DoesNotSetGroupId()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest!.MessageGroupId.Should().BeNull();
        client.LastRequest.MessageDeduplicationId.Should().BeNull();
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

    #region MockSqsClient

    private class MockSqsClient : IAmazonSQS
    {
        private int _messageCounter;

        public SendMessageRequest? LastRequest { get; private set; }
        public List<SendMessageRequest> AllRequests { get; } = [];
        public bool ThrowOnSend { get; set; }

        public Amazon.Runtime.IClientConfig Config => throw new NotImplementedException();

        public ISQSPaginatorFactory Paginators => throw new NotImplementedException();

        public Task<SendMessageResponse> SendMessageAsync(
            SendMessageRequest request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnSend)
                throw new AmazonSQSException("Mock SQS error");

            LastRequest = request;
            AllRequests.Add(request);

            var messageId = $"mock-msg-{Interlocked.Increment(ref _messageCounter)}";
            return Task.FromResult(new SendMessageResponse { MessageId = messageId });
        }

        // All other interface members throw NotImplementedException — only SendMessageAsync is used
        public Task<SendMessageResponse> SendMessageAsync(
            string queueUrl,
            string messageBody,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<SendMessageBatchResponse> SendMessageBatchAsync(
            SendMessageBatchRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<SendMessageBatchResponse> SendMessageBatchAsync(
            string queueUrl,
            List<SendMessageBatchRequestEntry> entries,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ReceiveMessageResponse> ReceiveMessageAsync(
            ReceiveMessageRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ReceiveMessageResponse> ReceiveMessageAsync(
            string queueUrl,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteMessageResponse> DeleteMessageAsync(
            DeleteMessageRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteMessageResponse> DeleteMessageAsync(
            string queueUrl,
            string receiptHandle,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(
            DeleteMessageBatchRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(
            string queueUrl,
            List<DeleteMessageBatchRequestEntry> entries,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateQueueResponse> CreateQueueAsync(
            CreateQueueRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateQueueResponse> CreateQueueAsync(
            string queueName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteQueueResponse> DeleteQueueAsync(
            DeleteQueueRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteQueueResponse> DeleteQueueAsync(
            string queueUrl,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetQueueUrlResponse> GetQueueUrlAsync(
            GetQueueUrlRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetQueueUrlResponse> GetQueueUrlAsync(
            string queueName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(
            GetQueueAttributesRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetQueueAttributesResponse> GetQueueAttributesAsync(
            string queueUrl,
            List<string> attributeNames,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(
            SetQueueAttributesRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<SetQueueAttributesResponse> SetQueueAttributesAsync(
            string queueUrl,
            Dictionary<string, string> attributes,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListQueuesResponse> ListQueuesAsync(
            ListQueuesRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListQueuesResponse> ListQueuesAsync(
            string queueNamePrefix,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
            ChangeMessageVisibilityRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
            string queueUrl,
            string receiptHandle,
            int visibilityTimeout,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
            string queueUrl,
            string receiptHandle,
            int? visibilityTimeout,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(
            ChangeMessageVisibilityBatchRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ChangeMessageVisibilityBatchResponse> ChangeMessageVisibilityBatchAsync(
            string queueUrl,
            List<ChangeMessageVisibilityBatchRequestEntry> entries,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PurgeQueueResponse> PurgeQueueAsync(
            PurgeQueueRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PurgeQueueResponse> PurgeQueueAsync(
            string queueUrl,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            AddPermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            string queueUrl,
            string label,
            List<string> awsAccountIds,
            List<string> actions,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            RemovePermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            string queueUrl,
            string label,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<TagQueueResponse> TagQueueAsync(
            TagQueueRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UntagQueueResponse> UntagQueueAsync(
            UntagQueueRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListQueueTagsResponse> ListQueueTagsAsync(
            ListQueueTagsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListDeadLetterSourceQueuesResponse> ListDeadLetterSourceQueuesAsync(
            ListDeadLetterSourceQueuesRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<StartMessageMoveTaskResponse> StartMessageMoveTaskAsync(
            StartMessageMoveTaskRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListMessageMoveTasksResponse> ListMessageMoveTasksAsync(
            ListMessageMoveTasksRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CancelMessageMoveTaskResponse> CancelMessageMoveTaskAsync(
            CancelMessageMoveTaskRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<string> AuthorizeS3ToSendMessageAsync(string queueUrl, string bucket) =>
            throw new NotImplementedException();

        public Task<Dictionary<string, string>> GetAttributesAsync(string queueUrl) =>
            throw new NotImplementedException();

        public Task SetAttributesAsync(string queueUrl, Dictionary<string, string> attributes) =>
            throw new NotImplementedException();

        public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(
            Amazon.Runtime.AmazonWebServiceRequest request
        ) => throw new NotImplementedException();

        public void Dispose() { }
    }

    #endregion
}
