using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Utils;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the remote run execution feature.
/// Tests IRunExecutor, TrainExecutionService delegation, and the
/// UseTraxRunEndpoint request/response serialization contract.
/// </summary>
[TestFixture]
public class RemoteRunExecutionTests : TestSetup
{
    #region IRunExecutor — Default Registration

    [Test]
    public void DefaultRunExecutor_IsLocalRunExecutor()
    {
        var executor = Scope.ServiceProvider.GetRequiredService<IRunExecutor>();
        executor.Should().BeOfType<LocalRunExecutor>();
    }

    #endregion

    #region TrainExecutionService.RunAsync — Typed Output

    [Test]
    public async Task RunAsync_TypedOutputTrain_ReturnsOutput()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new TypedOutputSchedulerTestInput { Value = "integration-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(
            nameof(ITypedOutputSchedulerTestTrain),
            inputJson
        );

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().NotBeNull();
        result.Output.Should().BeOfType<string>();
        ((string)result.Output!).Should().Be("processed-integration-test");
    }

    [Test]
    public async Task RunAsync_UnitOutputTrain_ReturnsNullOutput()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new SchedulerTestInput { Value = "unit-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(ISchedulerTestTrain), inputJson);

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeNull();
    }

    [Test]
    public async Task RunAsync_MultipleExecutions_UniqueMetadataIds()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var input1 = JsonSerializer.Serialize(
            new TypedOutputSchedulerTestInput { Value = "a" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
        var input2 = JsonSerializer.Serialize(
            new TypedOutputSchedulerTestInput { Value = "b" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result1 = await executionService.RunAsync(
            nameof(ITypedOutputSchedulerTestTrain),
            input1
        );
        var result2 = await executionService.RunAsync(
            nameof(ITypedOutputSchedulerTestTrain),
            input2
        );

        result1.MetadataId.Should().NotBe(result2.MetadataId);
    }

    #endregion

    #region TrainExecutionService.RunAsync — Error Handling

    [Test]
    public async Task RunAsync_FailingTrain_ThrowsTrainException()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new FailingSchedulerTestInput { FailureMessage = "remote-run-error" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var act = async () =>
            await executionService.RunAsync(nameof(IFailingSchedulerTestTrain), inputJson);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*remote-run-error*");
    }

    [Test]
    public async Task RunAsync_UnknownTrain_ThrowsInvalidOperationException()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () => await executionService.RunAsync("NonExistent.Train", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No train found*");
    }

    [Test]
    public async Task RunAsync_InvalidJson_ThrowsException()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await executionService.RunAsync(nameof(ISchedulerTestTrain), "not valid json");

        await act.Should().ThrowAsync<JsonException>();
    }

    #endregion

    #region RemoteRunResponse — Simulated Endpoint Serialization

    [Test]
    public async Task SimulatedEndpoint_TypedOutput_ProducesValidResponse()
    {
        // Simulate what UseTraxRunEndpoint does:
        // 1. Call ITrainExecutionService.RunAsync
        // 2. Serialize the output into RemoteRunResponse
        // 3. Verify HttpRunExecutor can deserialize it

        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new TypedOutputSchedulerTestInput { Value = "endpoint-sim" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Step 1: Run the train (as the endpoint would)
        var result = await executionService.RunAsync(
            nameof(ITypedOutputSchedulerTestTrain),
            inputJson
        );

        // Step 2: Serialize the response (as the endpoint would)
        string? outputJson = null;
        string? outputType = null;
        if (result.Output is not null)
        {
            outputType = result.Output.GetType().FullName;
            outputJson = JsonSerializer.Serialize(
                result.Output,
                result.Output.GetType(),
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        var response = new RemoteRunResponse(result.MetadataId, outputJson, outputType);
        var wireJson = JsonSerializer.Serialize(response);

        // Step 3: Deserialize (as HttpRunExecutor would)
        var received = JsonSerializer.Deserialize<RemoteRunResponse>(wireJson);
        received.Should().NotBeNull();
        received!.MetadataId.Should().Be(result.MetadataId);
        received.IsError.Should().BeFalse();
        received.OutputType.Should().Be(typeof(string).FullName);
        received.OutputJson.Should().NotBeNull();

        // Step 4: Deserialize the output value
        var resolvedType = Type.GetType(received.OutputType!)!;
        var output = JsonSerializer.Deserialize(
            received.OutputJson!,
            resolvedType,
            TraxJsonSerializationOptions.ManifestProperties
        );
        output.Should().Be("processed-endpoint-sim");
    }

    [Test]
    public async Task SimulatedEndpoint_UnitOutput_ProducesValidResponse()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new SchedulerTestInput { Value = "unit-endpoint-sim" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(ISchedulerTestTrain), inputJson);

        // Build response as the endpoint would
        var response = new RemoteRunResponse(result.MetadataId);
        var wireJson = JsonSerializer.Serialize(response);

        var received = JsonSerializer.Deserialize<RemoteRunResponse>(wireJson);
        received.Should().NotBeNull();
        received!.MetadataId.Should().BeGreaterThan(0);
        received.OutputJson.Should().BeNull();
        received.OutputType.Should().BeNull();
        received.IsError.Should().BeFalse();
    }

    [Test]
    public async Task SimulatedEndpoint_FailingTrain_ProducesErrorResponse()
    {
        var executionService = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new FailingSchedulerTestInput { FailureMessage = "endpoint-failure" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Simulate what the endpoint does on error
        RemoteRunResponse response;
        try
        {
            await executionService.RunAsync(nameof(IFailingSchedulerTestTrain), inputJson);
            response = new RemoteRunResponse(0); // should not reach here
        }
        catch (Exception ex)
        {
            response = new RemoteRunResponse(
                MetadataId: 0,
                IsError: true,
                ErrorMessage: ex.Message
            );
        }

        // Verify the error response
        var wireJson = JsonSerializer.Serialize(response);
        var received = JsonSerializer.Deserialize<RemoteRunResponse>(wireJson);

        received.Should().NotBeNull();
        received!.IsError.Should().BeTrue();
        received.ErrorMessage.Should().Contain("endpoint-failure");
    }

    #endregion

    #region RemoteRunRequest — Simulated Client Serialization

    [Test]
    public void SimulatedClient_ProducesValidRequest()
    {
        // Simulate what HttpRunExecutor sends
        var input = new TypedOutputSchedulerTestInput { Value = "client-sim" };
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var request = new RemoteRunRequest(
            nameof(ITypedOutputSchedulerTestTrain),
            inputJson,
            input.GetType().FullName!
        );

        var wireJson = JsonSerializer.Serialize(request);

        // Simulate what the endpoint receives
        var received = JsonSerializer.Deserialize<RemoteRunRequest>(wireJson);
        received.Should().NotBeNull();
        received!.TrainName.Should().Be(nameof(ITypedOutputSchedulerTestTrain));
        received.InputType.Should().Be(typeof(TypedOutputSchedulerTestInput).FullName);

        // Verify the input can be deserialized back
        var resolvedType = Type.GetType(
            received.InputType
                + ", "
                + typeof(TypedOutputSchedulerTestInput).Assembly.GetName().Name
        )!;
        var deserializedInput = JsonSerializer.Deserialize(
            received.InputJson,
            resolvedType,
            TraxJsonSerializationOptions.ManifestProperties
        );
        deserializedInput.Should().BeOfType<TypedOutputSchedulerTestInput>();
        ((TypedOutputSchedulerTestInput)deserializedInput!).Value.Should().Be("client-sim");
    }

    #endregion
}
