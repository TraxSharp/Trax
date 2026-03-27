using System.Text;
using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Lambda.Services;
using Trax.Scheduler.Services.Lambda;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class LambdaRunExecutorTests
{
    #region Successful Execution

    [Test]
    public async Task ExecuteAsync_SuccessfulResponse_ReturnsOutputAndMetadataId()
    {
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 42,
            OutputJson: """{"value":"hello","count":7}""",
            OutputType: typeof(TestRunOutput).FullName
        );
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        var result = await executor.ExecuteAsync(
            "My.Train",
            new TestRunInput { Name = "test" },
            typeof(TestRunOutput)
        );

        // Assert
        result.MetadataId.Should().Be(42);
        result.Output.Should().NotBeNull();
        result.Output.Should().BeOfType<TestRunOutput>();
        var output = (TestRunOutput)result.Output!;
        output.Value.Should().Be("hello");
        output.Count.Should().Be(7);
    }

    [Test]
    public async Task ExecuteAsync_UnitResponse_ReturnsNullOutput()
    {
        // Arrange
        var response = new RemoteRunResponse(MetadataId: 10);
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        var result = await executor.ExecuteAsync(
            "My.UnitTrain",
            new TestRunInput { Name = "unit" },
            typeof(LanguageExt.Unit)
        );

        // Assert
        result.MetadataId.Should().Be(10);
        result.Output.Should().BeNull();
    }

    [Test]
    public async Task ExecuteAsync_UsesRequestResponseInvocationType()
    {
        // Arrange
        var response = new RemoteRunResponse(MetadataId: 1);
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        await executor.ExecuteAsync(
            "My.Train",
            new TestRunInput { Name = "test" },
            typeof(TestRunOutput)
        );

        // Assert
        client.LastRequest.Should().NotBeNull();
        client.LastRequest!.InvocationType.Should().Be(InvocationType.RequestResponse);
    }

    [Test]
    public async Task ExecuteAsync_SendsCorrectEnvelope()
    {
        // Arrange
        var response = new RemoteRunResponse(MetadataId: 1);
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        await executor.ExecuteAsync(
            "My.Train.FullName",
            new TestRunInput { Name = "serialize-test" },
            typeof(TestRunOutput)
        );

        // Assert
        var envelope = JsonSerializer.Deserialize<LambdaEnvelope>(
            client.LastRequest!.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(LambdaRequestType.Run);

        var request = JsonSerializer.Deserialize<RemoteRunRequest>(
            envelope.PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        request.Should().NotBeNull();
        request!.TrainName.Should().Be("My.Train.FullName");
        request.InputType.Should().Be(typeof(TestRunInput).FullName);
        request.InputJson.Should().Contain("serialize-test");
    }

    #endregion

    #region Error Handling — IsError Response

    [Test]
    public async Task ExecuteAsync_ErrorResponse_ThrowsTrainException()
    {
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Train failed: something went wrong"
        );
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.FailingTrain",
                new TestRunInput { Name = "fail" },
                typeof(TestRunOutput)
            );

        // Assert
        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*Train failed: something went wrong*");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithTrainExceptionData_ReconstructsStructuredException()
    {
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Validation failed",
            ExceptionType: "InvalidOperationException",
            FailureJunction: "ValidateInputJunction",
            StackTrace: "at MyApp.ValidateInputJunction.Run()"
        );
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.FailingTrain",
                new TestRunInput { Name = "fail" },
                typeof(TestRunOutput)
            );

        // Assert
        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        data.Should().NotBeNull();
        data!.Type.Should().Be("InvalidOperationException");
        data.Junction.Should().Be("ValidateInputJunction");
        data.Message.Should().Be("Validation failed");
    }

    [Test]
    public async Task ExecuteAsync_ErrorResponse_WithPlainMessage_FallsBackToFlatString()
    {
        // Arrange
        var response = new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: "Something failed"
        );
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestRunInput { Name = "x" },
                typeof(TestRunOutput)
            );

        // Assert
        var ex = (await act.Should().ThrowAsync<TrainException>()).Which;
        ex.Message.Should().Contain("Something failed");
        // Should NOT be parseable as TrainExceptionData
        var parseAct = () => JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);
        parseAct.Should().Throw<JsonException>();
    }

    #endregion

    #region Error Handling — Lambda Function Error

    [Test]
    public async Task ExecuteAsync_FunctionError_ThrowsTrainException()
    {
        // Arrange
        var client = new MockLambdaClient { FunctionError = "Unhandled" };
        var executor = CreateExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestRunInput { Name = "crash" },
                typeof(TestRunOutput)
            );

        // Assert
        await act.Should().ThrowAsync<TrainException>().WithMessage("*Unhandled*");
    }

    #endregion

    #region Error Handling — Null Response

    [Test]
    public async Task ExecuteAsync_NullResponsePayload_ThrowsTrainException()
    {
        // Arrange
        var client = new MockLambdaClient
        {
            ResponsePayload = new MemoryStream(Encoding.UTF8.GetBytes("null")),
        };
        var executor = CreateExecutor(client);

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestRunInput { Name = "null" },
                typeof(TestRunOutput)
            );

        // Assert
        await act.Should().ThrowAsync<TrainException>().WithMessage("*null response*");
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        // Arrange
        var response = new RemoteRunResponse(MetadataId: 1);
        var client = CreateMockClient(response);
        var executor = CreateExecutor(client);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () =>
            await executor.ExecuteAsync(
                "My.Train",
                new TestRunInput { Name = "cancel" },
                typeof(TestRunOutput),
                cts.Token
            );

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Helpers

    private static LambdaRunExecutor CreateExecutor(MockLambdaClient client) =>
        new(
            client,
            new LambdaRunOptions { FunctionName = "my-runner" },
            NullLogger<LambdaRunExecutor>.Instance
        );

    private static MockLambdaClient CreateMockClient(RemoteRunResponse response)
    {
        var json = JsonSerializer.Serialize(response);
        return new MockLambdaClient
        {
            ResponsePayload = new MemoryStream(Encoding.UTF8.GetBytes(json)),
        };
    }

    #endregion

    #region Test Types

    public record TestRunInput
    {
        public string Name { get; init; } = "";
    }

    public record TestRunOutput
    {
        public string Value { get; init; } = "";
        public int Count { get; init; }
    }

    #endregion

    #region MockLambdaClient

    private class MockLambdaClient : IAmazonLambda
    {
        public InvokeRequest? LastRequest { get; private set; }
        public string? FunctionError { get; set; }
        public MemoryStream? ResponsePayload { get; set; }

        public Amazon.Runtime.IClientConfig Config => throw new NotImplementedException();

        public ILambdaPaginatorFactory Paginators => throw new NotImplementedException();

        public Task<InvokeResponse> InvokeAsync(
            InvokeRequest request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastRequest = request;

            var payload = ResponsePayload ?? new MemoryStream(Encoding.UTF8.GetBytes("{}"));

            return Task.FromResult(
                new InvokeResponse
                {
                    StatusCode = 200,
                    FunctionError = FunctionError,
                    Payload = payload,
                }
            );
        }

        // Minimal interface stubs — only InvokeAsync(InvokeRequest) is used
        public Task<InvokeResponse> InvokeAsync(
            string functionName,
            CancellationToken cancellationToken = default
        ) => throw new NotImplementedException();

        public Task<AddLayerVersionPermissionResponse> AddLayerVersionPermissionAsync(
            AddLayerVersionPermissionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<AddPermissionResponse> AddPermissionAsync(
            AddPermissionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateAliasResponse> CreateAliasAsync(
            CreateAliasRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateCodeSigningConfigResponse> CreateCodeSigningConfigAsync(
            CreateCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateEventSourceMappingResponse> CreateEventSourceMappingAsync(
            CreateEventSourceMappingRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateFunctionResponse> CreateFunctionAsync(
            CreateFunctionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<CreateFunctionUrlConfigResponse> CreateFunctionUrlConfigAsync(
            CreateFunctionUrlConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteAliasResponse> DeleteAliasAsync(
            DeleteAliasRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteCodeSigningConfigResponse> DeleteCodeSigningConfigAsync(
            DeleteCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteEventSourceMappingResponse> DeleteEventSourceMappingAsync(
            DeleteEventSourceMappingRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionResponse> DeleteFunctionAsync(
            string fn,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionResponse> DeleteFunctionAsync(
            DeleteFunctionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionCodeSigningConfigResponse> DeleteFunctionCodeSigningConfigAsync(
            DeleteFunctionCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionConcurrencyResponse> DeleteFunctionConcurrencyAsync(
            DeleteFunctionConcurrencyRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionEventInvokeConfigResponse> DeleteFunctionEventInvokeConfigAsync(
            DeleteFunctionEventInvokeConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteFunctionUrlConfigResponse> DeleteFunctionUrlConfigAsync(
            DeleteFunctionUrlConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteLayerVersionResponse> DeleteLayerVersionAsync(
            DeleteLayerVersionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<DeleteProvisionedConcurrencyConfigResponse> DeleteProvisionedConcurrencyConfigAsync(
            DeleteProvisionedConcurrencyConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(
            Amazon.Runtime.AmazonWebServiceRequest r
        ) => throw new NotImplementedException();

        public Task<GetAccountSettingsResponse> GetAccountSettingsAsync(
            GetAccountSettingsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetAliasResponse> GetAliasAsync(
            GetAliasRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetCodeSigningConfigResponse> GetCodeSigningConfigAsync(
            GetCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetEventSourceMappingResponse> GetEventSourceMappingAsync(
            GetEventSourceMappingRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionResponse> GetFunctionAsync(
            string fn,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionResponse> GetFunctionAsync(
            GetFunctionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionCodeSigningConfigResponse> GetFunctionCodeSigningConfigAsync(
            GetFunctionCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConcurrencyResponse> GetFunctionConcurrencyAsync(
            GetFunctionConcurrencyRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConfigurationResponse> GetFunctionConfigurationAsync(
            string fn,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionConfigurationResponse> GetFunctionConfigurationAsync(
            GetFunctionConfigurationRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionEventInvokeConfigResponse> GetFunctionEventInvokeConfigAsync(
            GetFunctionEventInvokeConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionRecursionConfigResponse> GetFunctionRecursionConfigAsync(
            GetFunctionRecursionConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetFunctionUrlConfigResponse> GetFunctionUrlConfigAsync(
            GetFunctionUrlConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionResponse> GetLayerVersionAsync(
            GetLayerVersionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionByArnResponse> GetLayerVersionByArnAsync(
            GetLayerVersionByArnRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetLayerVersionPolicyResponse> GetLayerVersionPolicyAsync(
            GetLayerVersionPolicyRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetPolicyResponse> GetPolicyAsync(string fn, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<GetPolicyResponse> GetPolicyAsync(
            GetPolicyRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetProvisionedConcurrencyConfigResponse> GetProvisionedConcurrencyConfigAsync(
            GetProvisionedConcurrencyConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<GetRuntimeManagementConfigResponse> GetRuntimeManagementConfigAsync(
            GetRuntimeManagementConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<InvokeWithResponseStreamResponse> InvokeWithResponseStreamAsync(
            InvokeWithResponseStreamRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListAliasesResponse> ListAliasesAsync(
            ListAliasesRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListCodeSigningConfigsResponse> ListCodeSigningConfigsAsync(
            ListCodeSigningConfigsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListEventSourceMappingsResponse> ListEventSourceMappingsAsync(
            ListEventSourceMappingsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionEventInvokeConfigsResponse> ListFunctionEventInvokeConfigsAsync(
            ListFunctionEventInvokeConfigsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionsResponse> ListFunctionsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ListFunctionsResponse> ListFunctionsAsync(
            ListFunctionsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionsByCodeSigningConfigResponse> ListFunctionsByCodeSigningConfigAsync(
            ListFunctionsByCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListFunctionUrlConfigsResponse> ListFunctionUrlConfigsAsync(
            ListFunctionUrlConfigsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListLayersResponse> ListLayersAsync(
            ListLayersRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListLayerVersionsResponse> ListLayerVersionsAsync(
            ListLayerVersionsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListProvisionedConcurrencyConfigsResponse> ListProvisionedConcurrencyConfigsAsync(
            ListProvisionedConcurrencyConfigsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListTagsResponse> ListTagsAsync(
            ListTagsRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<ListVersionsByFunctionResponse> ListVersionsByFunctionAsync(
            ListVersionsByFunctionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PublishLayerVersionResponse> PublishLayerVersionAsync(
            PublishLayerVersionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PublishVersionResponse> PublishVersionAsync(
            PublishVersionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionCodeSigningConfigResponse> PutFunctionCodeSigningConfigAsync(
            PutFunctionCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionConcurrencyResponse> PutFunctionConcurrencyAsync(
            PutFunctionConcurrencyRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionEventInvokeConfigResponse> PutFunctionEventInvokeConfigAsync(
            PutFunctionEventInvokeConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutFunctionRecursionConfigResponse> PutFunctionRecursionConfigAsync(
            PutFunctionRecursionConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutProvisionedConcurrencyConfigResponse> PutProvisionedConcurrencyConfigAsync(
            PutProvisionedConcurrencyConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<PutRuntimeManagementConfigResponse> PutRuntimeManagementConfigAsync(
            PutRuntimeManagementConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<RemoveLayerVersionPermissionResponse> RemoveLayerVersionPermissionAsync(
            RemoveLayerVersionPermissionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            string fn,
            string sid,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<RemovePermissionResponse> RemovePermissionAsync(
            RemovePermissionRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TagResourceResponse> TagResourceAsync(
            TagResourceRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UntagResourceResponse> UntagResourceAsync(
            UntagResourceRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateAliasResponse> UpdateAliasAsync(
            UpdateAliasRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateCodeSigningConfigResponse> UpdateCodeSigningConfigAsync(
            UpdateCodeSigningConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateEventSourceMappingResponse> UpdateEventSourceMappingAsync(
            UpdateEventSourceMappingRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionCodeResponse> UpdateFunctionCodeAsync(
            UpdateFunctionCodeRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionConfigurationResponse> UpdateFunctionConfigurationAsync(
            UpdateFunctionConfigurationRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionEventInvokeConfigResponse> UpdateFunctionEventInvokeConfigAsync(
            UpdateFunctionEventInvokeConfigRequest r,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<UpdateFunctionUrlConfigResponse> UpdateFunctionUrlConfigAsync(
            UpdateFunctionUrlConfigRequest r,
            CancellationToken ct = default
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
