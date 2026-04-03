using System.Net;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Lambda.Services;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class LambdaRetryHelperTests
{
    private static LambdaRetryOptions FastOptions(int maxRetries = 3) =>
        new()
        {
            MaxRetries = maxRetries,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

    private static InvokeRequest DefaultRequest =>
        new() { FunctionName = "test-fn", Payload = "{}" };

    #region Retry on Transient Exceptions

    [Test]
    public async Task InvokeWithRetryAsync_429ThenSuccess_RetriesAndReturnsResponse()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.TooManyRequests),
            Succeed(),
        ]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(2);
    }

    [Test]
    public async Task InvokeWithRetryAsync_502ThenSuccess_RetriesAndReturnsResponse()
    {
        var client = new SequentialMockLambdaClient([Throw(HttpStatusCode.BadGateway), Succeed()]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(2);
    }

    [Test]
    public async Task InvokeWithRetryAsync_503ThenSuccess_RetriesAndReturnsResponse()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.ServiceUnavailable),
            Succeed(),
        ]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(2);
    }

    [Test]
    public async Task InvokeWithRetryAsync_504ThenSuccess_RetriesAndReturnsResponse()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.GatewayTimeout),
            Succeed(),
        ]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(2);
    }

    [Test]
    public async Task InvokeWithRetryAsync_HttpRequestExceptionThenSuccess_Retries()
    {
        var client = new SequentialMockLambdaClient([ThrowHttp("connection reset"), Succeed()]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(2);
    }

    [Test]
    public async Task InvokeWithRetryAsync_Multiple429ThenSuccess_RetriesMultipleTimes()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
            Succeed(),
        ]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(5),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(4);
    }

    #endregion

    #region Exhaust Retries

    [Test]
    public async Task InvokeWithRetryAsync_429ExceedsMaxRetries_ThrowsLastException()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
        ]);

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                FastOptions(3),
                NullLogger.Instance,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<AmazonServiceException>();
        client.InvokeCount.Should().Be(4); // 1 initial + 3 retries
    }

    #endregion

    #region Non-Transient Exceptions

    [Test]
    public async Task InvokeWithRetryAsync_ResourceNotFound_DoesNotRetry()
    {
        var client = new SequentialMockLambdaClient([
            ThrowCustom(new ResourceNotFoundException("not found")),
        ]);

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                FastOptions(),
                NullLogger.Instance,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<ResourceNotFoundException>();
        client.InvokeCount.Should().Be(1);
    }

    [Test]
    public async Task InvokeWithRetryAsync_InvalidParameterValue_DoesNotRetry()
    {
        var client = new SequentialMockLambdaClient([
            ThrowCustom(new InvalidParameterValueException("bad param")),
        ]);

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                FastOptions(),
                NullLogger.Instance,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidParameterValueException>();
        client.InvokeCount.Should().Be(1);
    }

    [Test]
    public async Task InvokeWithRetryAsync_500InternalServerError_DoesNotRetry()
    {
        var client = new SequentialMockLambdaClient([Throw(HttpStatusCode.InternalServerError)]);

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                FastOptions(),
                NullLogger.Instance,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<AmazonServiceException>();
        client.InvokeCount.Should().Be(1);
    }

    #endregion

    #region MaxRetries = 0 (Disabled)

    [Test]
    public async Task InvokeWithRetryAsync_MaxRetriesZero_DoesNotRetry()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.TooManyRequests),
            Succeed(),
        ]);

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                FastOptions(0),
                NullLogger.Instance,
                CancellationToken.None
            );

        await act.Should().ThrowAsync<AmazonServiceException>();
        client.InvokeCount.Should().Be(1);
    }

    #endregion

    #region Success Without Retry

    [Test]
    public async Task InvokeWithRetryAsync_ImmediateSuccess_DoesNotRetry()
    {
        var client = new SequentialMockLambdaClient([Succeed()]);

        var response = await LambdaRetryHelper.InvokeWithRetryAsync(
            client,
            DefaultRequest,
            FastOptions(),
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(200);
        client.InvokeCount.Should().Be(1);
    }

    #endregion

    #region ComputeDelay

    [Test]
    public void ComputeDelay_Attempt0_ReturnsAroundBaseDelay()
    {
        var options = new LambdaRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        var delay = LambdaRetryHelper.ComputeDelay(0, options);

        // Base delay * 2^0 = 1s, with +/-25% jitter -> 0.75s to 1.25s
        delay.TotalMilliseconds.Should().BeInRange(750, 1250);
    }

    [Test]
    public void ComputeDelay_Attempt3_ReturnsExponentiallyHigher()
    {
        var options = new LambdaRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        var delay = LambdaRetryHelper.ComputeDelay(3, options);

        // Base delay * 2^3 = 8s, with +/-25% jitter -> 6s to 10s
        delay.TotalMilliseconds.Should().BeInRange(6000, 10000);
    }

    [Test]
    public void ComputeDelay_LargeAttempt_CapsAtMaxDelay()
    {
        var options = new LambdaRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        var delay = LambdaRetryHelper.ComputeDelay(10, options);

        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ComputeDelay_JitterApplied_ProducesDifferentValues()
    {
        var options = new LambdaRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        var delays = Enumerable
            .Range(0, 20)
            .Select(_ => LambdaRetryHelper.ComputeDelay(2, options))
            .ToList();

        delays.Distinct().Count().Should().BeGreaterThan(1);
    }

    #endregion

    #region IsTransient

    [Test]
    public void IsTransient_429_ReturnsTrue()
    {
        var ex = new AmazonServiceException("throttle")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
        };
        LambdaRetryHelper.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_502_ReturnsTrue()
    {
        var ex = new AmazonServiceException("bad gateway")
        {
            StatusCode = HttpStatusCode.BadGateway,
        };
        LambdaRetryHelper.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_503_ReturnsTrue()
    {
        var ex = new AmazonServiceException("unavailable")
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
        };
        LambdaRetryHelper.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_504_ReturnsTrue()
    {
        var ex = new AmazonServiceException("timeout")
        {
            StatusCode = HttpStatusCode.GatewayTimeout,
        };
        LambdaRetryHelper.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_HttpRequestException_ReturnsTrue()
    {
        var ex = new HttpRequestException("connection reset");
        LambdaRetryHelper.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_500_ReturnsFalse()
    {
        var ex = new AmazonServiceException("internal error")
        {
            StatusCode = HttpStatusCode.InternalServerError,
        };
        LambdaRetryHelper.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_ResourceNotFoundException_ReturnsFalse()
    {
        var ex = new ResourceNotFoundException("not found");
        LambdaRetryHelper.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_InvalidOperationException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("bad state");
        LambdaRetryHelper.IsTransient(ex).Should().BeFalse();
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task InvokeWithRetryAsync_CancelledDuringRetry_ThrowsOperationCancelled()
    {
        var client = new SequentialMockLambdaClient([
            Throw(HttpStatusCode.TooManyRequests),
            Throw(HttpStatusCode.TooManyRequests),
            Succeed(),
        ]);

        var options = new LambdaRetryOptions
        {
            MaxRetries = 5,
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () =>
            await LambdaRetryHelper.InvokeWithRetryAsync(
                client,
                DefaultRequest,
                options,
                NullLogger.Instance,
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Helpers

    private static Func<InvokeResponse> Succeed() => () => new InvokeResponse { StatusCode = 200 };

    private static Func<InvokeResponse> Throw(HttpStatusCode statusCode) =>
        () =>
            throw new AmazonServiceException($"AWS error {statusCode}") { StatusCode = statusCode };

    private static Func<InvokeResponse> ThrowHttp(string message) =>
        () => throw new HttpRequestException(message);

    private static Func<InvokeResponse> ThrowCustom(Exception ex) => () => throw ex;

    #endregion

    #region SequentialMockLambdaClient

    private class SequentialMockLambdaClient(List<Func<InvokeResponse>> behaviors) : IAmazonLambda
    {
        private int _callIndex;
        public int InvokeCount => _callIndex;

        public IClientConfig Config => throw new NotImplementedException();
        public ILambdaPaginatorFactory Paginators => throw new NotImplementedException();

        public Task<InvokeResponse> InvokeAsync(
            InvokeRequest request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = _callIndex < behaviors.Count ? _callIndex : behaviors.Count - 1;
            _callIndex++;

            var response = behaviors[index]();
            return Task.FromResult(response);
        }

        // Minimal interface stubs
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
            AmazonWebServiceRequest r
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
