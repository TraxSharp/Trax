using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using FluentAssertions;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Lambda.Services;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class LambdaJobSubmitterTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    #region Helpers

    private static (LambdaJobSubmitter submitter, MockLambdaClient client) CreateSubmitter(
        string functionName = "my-lambda-function"
    )
    {
        var options = new LambdaWorkerOptions { FunctionName = functionName };
        var client = new MockLambdaClient();
        var logger = Microsoft
            .Extensions
            .Logging
            .Abstractions
            .NullLogger<LambdaJobSubmitter>
            .Instance;
        return (new LambdaJobSubmitter(client, options, logger), client);
    }

    #endregion

    #region EnqueueAsync(metadataId) Tests

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_ReturnsLambdaPrefixedJobId()
    {
        // Arrange
        var (submitter, _) = CreateSubmitter();

        // Act
        var jobId = await submitter.EnqueueAsync(42);

        // Assert
        jobId.Should().StartWith("lambda-");
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_SendsCorrectEnvelope()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest.Should().NotBeNull();
        var envelope = JsonSerializer.Deserialize<LambdaEnvelope>(
            client.LastRequest!.Payload,
            WebOptions
        );

        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(LambdaRequestType.Execute);

        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            envelope.PayloadJson,
            WebOptions
        );
        request.Should().NotBeNull();
        request!.MetadataId.Should().Be(42);
        request.Input.Should().BeNull();
        request.InputType.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_UsesEventInvocationType()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client.LastRequest!.InvocationType.Should().Be(InvocationType.Event);
    }

    [Test]
    public async Task EnqueueAsync_WithMetadataIdOnly_UsesConfiguredFunctionName()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter(
            "arn:aws:lambda:us-east-1:123456789:function:my-fn"
        );

        // Act
        await submitter.EnqueueAsync(42);

        // Assert
        client
            .LastRequest!.FunctionName.Should()
            .Be("arn:aws:lambda:us-east-1:123456789:function:my-fn");
    }

    #endregion

    #region EnqueueAsync(metadataId, input) Tests

    [Test]
    public async Task EnqueueAsync_WithInput_SerializesInputInEnvelope()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        var input = new SchedulerTestInput { Value = "hello-world" };

        // Act
        await submitter.EnqueueAsync(50, input);

        // Assert
        var envelope = JsonSerializer.Deserialize<LambdaEnvelope>(
            client.LastRequest!.Payload,
            WebOptions
        );

        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            envelope!.PayloadJson,
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
        var envelope = JsonSerializer.Deserialize<LambdaEnvelope>(
            client.LastRequest!.Payload,
            WebOptions
        );

        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            envelope!.PayloadJson,
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
        var envelope = JsonSerializer.Deserialize<LambdaEnvelope>(
            client.LastRequest!.Payload,
            WebOptions
        );

        var request = JsonSerializer.Deserialize<RemoteJobRequest>(
            envelope!.PayloadJson,
            WebOptions
        );

        request!.MetadataId.Should().Be(99);
        request.Input.Should().BeNull();
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
    public async Task EnqueueAsync_WhenFunctionErrorReturned_ThrowsTrainException()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        client.FunctionError = "Unhandled";

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*my-lambda-function*Unhandled*");
    }

    [Test]
    public void EnqueueAsync_WhenLambdaThrows_PropagatesException()
    {
        // Arrange
        var (submitter, client) = CreateSubmitter();
        client.ThrowOnInvoke = true;

        // Act & Assert
        var act = async () => await submitter.EnqueueAsync(42);
        act.Should().ThrowAsync<AmazonLambdaException>();
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

    #region MockLambdaClient

    internal class MockLambdaClient : IAmazonLambda
    {
        public InvokeRequest? LastRequest { get; private set; }
        public List<InvokeRequest> AllRequests { get; } = [];
        public bool ThrowOnInvoke { get; set; }
        public string? FunctionError { get; set; }

        public Amazon.Runtime.IClientConfig Config => throw new NotImplementedException();

        public ILambdaPaginatorFactory Paginators => throw new NotImplementedException();

        public Task<InvokeResponse> InvokeAsync(
            InvokeRequest request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnInvoke)
                throw new AmazonLambdaException("Mock Lambda error");

            LastRequest = request;
            AllRequests.Add(request);

            return Task.FromResult(
                new InvokeResponse { StatusCode = 202, FunctionError = FunctionError }
            );
        }

        // All other interface members throw NotImplementedException — only InvokeAsync is used
        public Task<AddLayerVersionPermissionResponse> AddLayerVersionPermissionAsync(
            AddLayerVersionPermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            AddPermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateAliasResponse> CreateAliasAsync(
            CreateAliasRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateCodeSigningConfigResponse> CreateCodeSigningConfigAsync(
            CreateCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateEventSourceMappingResponse> CreateEventSourceMappingAsync(
            CreateEventSourceMappingRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateFunctionResponse> CreateFunctionAsync(
            CreateFunctionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CreateFunctionUrlConfigResponse> CreateFunctionUrlConfigAsync(
            CreateFunctionUrlConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteAliasResponse> DeleteAliasAsync(
            DeleteAliasRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteCodeSigningConfigResponse> DeleteCodeSigningConfigAsync(
            DeleteCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteEventSourceMappingResponse> DeleteEventSourceMappingAsync(
            DeleteEventSourceMappingRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionResponse> DeleteFunctionAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionResponse> DeleteFunctionAsync(
            DeleteFunctionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionCodeSigningConfigResponse> DeleteFunctionCodeSigningConfigAsync(
            DeleteFunctionCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionConcurrencyResponse> DeleteFunctionConcurrencyAsync(
            DeleteFunctionConcurrencyRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionEventInvokeConfigResponse> DeleteFunctionEventInvokeConfigAsync(
            DeleteFunctionEventInvokeConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionUrlConfigResponse> DeleteFunctionUrlConfigAsync(
            DeleteFunctionUrlConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteLayerVersionResponse> DeleteLayerVersionAsync(
            DeleteLayerVersionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<DeleteProvisionedConcurrencyConfigResponse> DeleteProvisionedConcurrencyConfigAsync(
            DeleteProvisionedConcurrencyConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(
            Amazon.Runtime.AmazonWebServiceRequest request
        ) => throw new NotImplementedException();

        public Task<GetAccountSettingsResponse> GetAccountSettingsAsync(
            GetAccountSettingsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetAliasResponse> GetAliasAsync(
            GetAliasRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetCodeSigningConfigResponse> GetCodeSigningConfigAsync(
            GetCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetEventSourceMappingResponse> GetEventSourceMappingAsync(
            GetEventSourceMappingRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionResponse> GetFunctionAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionResponse> GetFunctionAsync(
            GetFunctionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionCodeSigningConfigResponse> GetFunctionCodeSigningConfigAsync(
            GetFunctionCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConcurrencyResponse> GetFunctionConcurrencyAsync(
            GetFunctionConcurrencyRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConfigurationResponse> GetFunctionConfigurationAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConfigurationResponse> GetFunctionConfigurationAsync(
            GetFunctionConfigurationRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionEventInvokeConfigResponse> GetFunctionEventInvokeConfigAsync(
            GetFunctionEventInvokeConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionRecursionConfigResponse> GetFunctionRecursionConfigAsync(
            GetFunctionRecursionConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionUrlConfigResponse> GetFunctionUrlConfigAsync(
            GetFunctionUrlConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionResponse> GetLayerVersionAsync(
            GetLayerVersionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionByArnResponse> GetLayerVersionByArnAsync(
            GetLayerVersionByArnRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionPolicyResponse> GetLayerVersionPolicyAsync(
            GetLayerVersionPolicyRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetPolicyResponse> GetPolicyAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetPolicyResponse> GetPolicyAsync(
            GetPolicyRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetProvisionedConcurrencyConfigResponse> GetProvisionedConcurrencyConfigAsync(
            GetProvisionedConcurrencyConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<GetRuntimeManagementConfigResponse> GetRuntimeManagementConfigAsync(
            GetRuntimeManagementConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<InvokeResponse> InvokeAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<InvokeWithResponseStreamResponse> InvokeWithResponseStreamAsync(
            InvokeWithResponseStreamRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListAliasesResponse> ListAliasesAsync(
            ListAliasesRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListCodeSigningConfigsResponse> ListCodeSigningConfigsAsync(
            ListCodeSigningConfigsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListEventSourceMappingsResponse> ListEventSourceMappingsAsync(
            ListEventSourceMappingsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionEventInvokeConfigsResponse> ListFunctionEventInvokeConfigsAsync(
            ListFunctionEventInvokeConfigsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionsResponse> ListFunctionsAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionsResponse> ListFunctionsAsync(
            ListFunctionsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionsByCodeSigningConfigResponse> ListFunctionsByCodeSigningConfigAsync(
            ListFunctionsByCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionUrlConfigsResponse> ListFunctionUrlConfigsAsync(
            ListFunctionUrlConfigsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListLayersResponse> ListLayersAsync(
            ListLayersRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListLayerVersionsResponse> ListLayerVersionsAsync(
            ListLayerVersionsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListProvisionedConcurrencyConfigsResponse> ListProvisionedConcurrencyConfigsAsync(
            ListProvisionedConcurrencyConfigsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListTagsResponse> ListTagsAsync(
            ListTagsRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<ListVersionsByFunctionResponse> ListVersionsByFunctionAsync(
            ListVersionsByFunctionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PublishLayerVersionResponse> PublishLayerVersionAsync(
            PublishLayerVersionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PublishVersionResponse> PublishVersionAsync(
            PublishVersionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionCodeSigningConfigResponse> PutFunctionCodeSigningConfigAsync(
            PutFunctionCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionConcurrencyResponse> PutFunctionConcurrencyAsync(
            PutFunctionConcurrencyRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionEventInvokeConfigResponse> PutFunctionEventInvokeConfigAsync(
            PutFunctionEventInvokeConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionRecursionConfigResponse> PutFunctionRecursionConfigAsync(
            PutFunctionRecursionConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutProvisionedConcurrencyConfigResponse> PutProvisionedConcurrencyConfigAsync(
            PutProvisionedConcurrencyConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<PutRuntimeManagementConfigResponse> PutRuntimeManagementConfigAsync(
            PutRuntimeManagementConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<RemoveLayerVersionPermissionResponse> RemoveLayerVersionPermissionAsync(
            RemoveLayerVersionPermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            string functionName,
            string statementId,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            RemovePermissionRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<TagResourceResponse> TagResourceAsync(
            TagResourceRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UntagResourceResponse> UntagResourceAsync(
            UntagResourceRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateAliasResponse> UpdateAliasAsync(
            UpdateAliasRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateCodeSigningConfigResponse> UpdateCodeSigningConfigAsync(
            UpdateCodeSigningConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateEventSourceMappingResponse> UpdateEventSourceMappingAsync(
            UpdateEventSourceMappingRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionCodeResponse> UpdateFunctionCodeAsync(
            UpdateFunctionCodeRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionConfigurationResponse> UpdateFunctionConfigurationAsync(
            UpdateFunctionConfigurationRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionEventInvokeConfigResponse> UpdateFunctionEventInvokeConfigAsync(
            UpdateFunctionEventInvokeConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionUrlConfigResponse> UpdateFunctionUrlConfigAsync(
            UpdateFunctionUrlConfigRequest request,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<CheckpointDurableExecutionResponse> CheckpointDurableExecutionAsync(
            CheckpointDurableExecutionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateCapacityProviderResponse> CreateCapacityProviderAsync(
            CreateCapacityProviderRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteCapacityProviderResponse> DeleteCapacityProviderAsync(
            DeleteCapacityProviderRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetCapacityProviderResponse> GetCapacityProviderAsync(
            GetCapacityProviderRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetDurableExecutionResponse> GetDurableExecutionAsync(
            GetDurableExecutionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetDurableExecutionHistoryResponse> GetDurableExecutionHistoryAsync(
            GetDurableExecutionHistoryRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetDurableExecutionStateResponse> GetDurableExecutionStateAsync(
            GetDurableExecutionStateRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionScalingConfigResponse> GetFunctionScalingConfigAsync(
            GetFunctionScalingConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListCapacityProvidersResponse> ListCapacityProvidersAsync(
            ListCapacityProvidersRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListDurableExecutionsByFunctionResponse> ListDurableExecutionsByFunctionAsync(
            ListDurableExecutionsByFunctionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionVersionsByCapacityProviderResponse> ListFunctionVersionsByCapacityProviderAsync(
            ListFunctionVersionsByCapacityProviderRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionScalingConfigResponse> PutFunctionScalingConfigAsync(
            PutFunctionScalingConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SendDurableExecutionCallbackFailureResponse> SendDurableExecutionCallbackFailureAsync(
            SendDurableExecutionCallbackFailureRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SendDurableExecutionCallbackHeartbeatResponse> SendDurableExecutionCallbackHeartbeatAsync(
            SendDurableExecutionCallbackHeartbeatRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SendDurableExecutionCallbackSuccessResponse> SendDurableExecutionCallbackSuccessAsync(
            SendDurableExecutionCallbackSuccessRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<StopDurableExecutionResponse> StopDurableExecutionAsync(
            StopDurableExecutionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateCapacityProviderResponse> UpdateCapacityProviderAsync(
            UpdateCapacityProviderRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public void Dispose() { }
    }

    #endregion
}
