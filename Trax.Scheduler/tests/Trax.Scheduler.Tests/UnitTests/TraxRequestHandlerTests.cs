using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The hosting-agnostic request handler behind every runner entry point.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class TraxRequestHandlerTests
{
    #region ExecuteJobAsync

    [Test]
    public async Task ExecuteJobAsync_WithInput_DeserializesAndRunsJobRunner()
    {
        var input = new TestInput { Name = "hello", Value = 42 };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxJsonSerializationOptions.ManifestProperties
        );
        var request = new RemoteJobRequest(
            MetadataId: 100,
            Input: inputJson,
            InputType: typeof(TestInput).FullName
        );
        var jobRunner = new FakeJobRunnerTrain();
        var handler = CreateHandler(jobRunner: jobRunner);

        var result = await handler.ExecuteJobAsync(request);

        result.MetadataId.Should().Be(100);
        jobRunner.ReceivedRequest.Should().NotBeNull();
        jobRunner.ReceivedRequest!.MetadataId.Should().Be(100);
        jobRunner.ReceivedRequest.Input.Should().NotBeNull();
        jobRunner.ReceivedRequest.Input.Should().BeOfType<TestInput>();
        var receivedInput = (TestInput)jobRunner.ReceivedRequest.Input!;
        receivedInput.Name.Should().Be("hello");
        receivedInput.Value.Should().Be(42);
    }

    [Test]
    public async Task ExecuteJobAsync_WithoutInput_RunsWithNullInput()
    {
        var request = new RemoteJobRequest(MetadataId: 200);
        var jobRunner = new FakeJobRunnerTrain();
        var handler = CreateHandler(jobRunner: jobRunner);

        var result = await handler.ExecuteJobAsync(request);

        result.MetadataId.Should().Be(200);
        jobRunner.ReceivedRequest.Should().NotBeNull();
        jobRunner.ReceivedRequest!.MetadataId.Should().Be(200);
        jobRunner.ReceivedRequest.Input.Should().BeNull();
    }

    [Test]
    public async Task ExecuteJobAsync_TrainFails_Throws()
    {
        var request = new RemoteJobRequest(MetadataId: 300);
        var jobRunner = new FakeJobRunnerTrain
        {
            ExceptionToThrow = new InvalidOperationException("Train exploded"),
        };
        var handler = CreateHandler(jobRunner: jobRunner);

        var act = async () => await handler.ExecuteJobAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Train exploded");
    }

    [Test]
    public async Task ExecuteJobAsync_InputTypeNoRegisteredTrainTakes_IsRefusedWithoutRunning()
    {
        var request = new RemoteJobRequest(
            MetadataId: 400,
            Input: "{}",
            InputType: typeof(UnregisteredInput).FullName
        );
        var jobRunner = new FakeJobRunnerTrain();
        var handler = CreateHandler(jobRunner: jobRunner);

        var act = async () => await handler.ExecuteJobAsync(request);

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("*not the input of any registered train*");
        jobRunner.ReceivedRequest.Should().BeNull();
    }

    [Test]
    public async Task ExecuteJobAsync_InputTypeNamingALoadableType_IsRefusedWithoutRunning()
    {
        // A name Type.GetType would resolve, but that no registered train takes.
        var request = new RemoteJobRequest(
            MetadataId: 401,
            Input: "\"x\"",
            InputType: typeof(string).AssemblyQualifiedName
        );
        var jobRunner = new FakeJobRunnerTrain();
        var handler = CreateHandler(jobRunner: jobRunner);

        var act = async () => await handler.ExecuteJobAsync(request);

        await act.Should().ThrowAsync<TrainException>();
        jobRunner.ReceivedRequest.Should().BeNull();
    }

    #endregion

    #region RunTrainAsync — Success

    [Test]
    public async Task RunTrainAsync_Success_ReturnsSerializedOutput()
    {
        var output = new TestOutput { Result = "success", Count = 7 };
        var executionService = new FakeTrainExecutionService
        {
            ResultToReturn = new RunTrainResult(
                MetadataId: 500,
                ExternalId: "ext-500",
                Output: output
            ),
        };
        var request = new RemoteRunRequest(
            TrainName: "My.Namespace.IMyTrain",
            InputJson: """{"name":"test"}""",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.MetadataId.Should().Be(500);
        response.ExternalId.Should().Be("ext-500");
        response.IsError.Should().BeFalse();
        response.OutputType.Should().Be(typeof(TestOutput).FullName);
        response.OutputJson.Should().NotBeNull();

        var deserialized = JsonSerializer.Deserialize<TestOutput>(
            response.OutputJson!,
            TraxJsonSerializationOptions.ManifestProperties
        );
        deserialized.Should().NotBeNull();
        deserialized!.Result.Should().Be("success");
        deserialized.Count.Should().Be(7);
    }

    [Test]
    public async Task RunTrainAsync_NullOutput_ReturnsNullOutputFields()
    {
        var executionService = new FakeTrainExecutionService
        {
            ResultToReturn = new RunTrainResult(MetadataId: 600, ExternalId: "ext-600"),
        };
        var request = new RemoteRunRequest(
            TrainName: "My.UnitTrain",
            InputJson: "{}",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.MetadataId.Should().Be(600);
        response.IsError.Should().BeFalse();
        response.OutputJson.Should().BeNull();
        response.OutputType.Should().BeNull();
    }

    #endregion

    #region RunTrainAsync — Error Handling

    [Test]
    public async Task RunTrainAsync_TrainFailsWithPlainException_ReportsTypeButNotMessageOrStack()
    {
        var executionService = new FakeTrainExecutionService
        {
            ExceptionToThrow = new InvalidOperationException("Something broke"),
        };
        var request = new RemoteRunRequest(
            TrainName: "My.FailingTrain",
            InputJson: "{}",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.IsError.Should().BeTrue();
        response.MetadataId.Should().Be(0);
        response.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
        response.ExceptionType.Should().Be("InvalidOperationException");
        response.StackTrace.Should().BeNull();
        response.FailureJunction.Should().BeNull();
        response.OutputJson.Should().BeNull();
    }

    [Test]
    public async Task RunTrainAsync_TrainFailsWithTrainExceptionData_PopulatesStructuredErrorFields()
    {
        var exceptionData = new TrainExceptionData
        {
            TrainName = "My.Namespace.IMyTrain",
            TrainExternalId = "ext-123",
            Type = "ArgumentException",
            Junction = "ValidateInputJunction",
            Message = "Input was invalid",
        };
        var serializedData = JsonSerializer.Serialize(exceptionData);
        var executionService = new FakeTrainExecutionService
        {
            ExceptionToThrow = new TrainException(serializedData),
        };
        var request = new RemoteRunRequest(
            TrainName: "My.FailingTrain",
            InputJson: "{}",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.IsError.Should().BeTrue();
        response.ExceptionType.Should().Be("ArgumentException");
        response.FailureJunction.Should().Be("ValidateInputJunction");
        response.ErrorMessage.Should().Be("Input was invalid");
        response.StackTrace.Should().BeNull();
    }

    [Test]
    public async Task RunTrainAsync_TrainFailsWithNestedInnerException_CapturesOuterExceptionType()
    {
        var inner = new ArgumentException("Bad argument");
        var outer = new InvalidOperationException("Operation failed", inner);
        var executionService = new FakeTrainExecutionService { ExceptionToThrow = outer };
        var request = new RemoteRunRequest(
            TrainName: "My.Train",
            InputJson: "{}",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.IsError.Should().BeTrue();
        response.ExceptionType.Should().Be("InvalidOperationException");
        response.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
    }

    [Test]
    public async Task RunTrainAsync_TrainFails_StackTraceIsNotSent()
    {
        var executionService = new FakeTrainExecutionService
        {
            ExceptionToThrow = new InvalidOperationException("boom"),
        };
        var request = new RemoteRunRequest(
            TrainName: "My.Train",
            InputJson: "{}",
            InputType: typeof(TestInput).FullName!
        );
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(request);

        response.IsError.Should().BeTrue();
        response.StackTrace.Should().BeNull();
    }

    #endregion

    #region BuildErrorResponse

    [Test]
    public void BuildErrorResponse_WithTrainExceptionData_ExtractsStructuredFields()
    {
        var data = new TrainExceptionData
        {
            TrainName = "My.Train",
            TrainExternalId = "ext-1",
            Type = "NullReferenceException",
            Junction = "LoadDataJunction",
            Message = "Object reference not set",
        };
        var ex = new TrainException(JsonSerializer.Serialize(data));

        var response = TraxRequestHandler.BuildErrorResponse(ex);

        response.IsError.Should().BeTrue();
        response.ExceptionType.Should().Be("NullReferenceException");
        response.FailureJunction.Should().Be("LoadDataJunction");
        response.ErrorMessage.Should().Be("Object reference not set");
    }

    [Test]
    public void BuildErrorResponse_WithPlainException_ReportsTypeOnly()
    {
        var ex = new ArgumentException("Bad value");

        var response = TraxRequestHandler.BuildErrorResponse(ex);

        response.IsError.Should().BeTrue();
        response.ExceptionType.Should().Be("ArgumentException");
        response.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
        response.FailureJunction.Should().BeNull();
    }

    [Test]
    public void BuildErrorResponse_WithNonJsonMessage_FallsBackGracefully()
    {
        var ex = new InvalidOperationException("This is not { valid JSON");

        var response = TraxRequestHandler.BuildErrorResponse(ex);

        response.IsError.Should().BeTrue();
        response.ExceptionType.Should().Be("InvalidOperationException");
        response.ErrorMessage.Should().Be(TraxRequestHandler.UnreportedFailureMessage);
    }

    #endregion

    #region RunTrainAsync — Scheduler Trains

    [Test]
    public async Task RunTrainAsync_SchedulerTrainFullName_IsRefusedWithoutRunning()
    {
        var executionService = new FakeTrainExecutionService();
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(
            new RemoteRunRequest(
                typeof(Trax.Scheduler.Trains.JobRunner.IJobRunnerTrain).FullName!,
                "{}",
                "ignored"
            )
        );

        response.IsError.Should().BeTrue();
        response.ErrorMessage.Should().Contain("scheduler's own");
        executionService
            .Calls.Should()
            .Be(
                0,
                "a runner does not run the scheduler's own trains (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [Test]
    public async Task RunTrainAsync_SchedulerTrainShortName_IsRefusedWithoutRunning()
    {
        var executionService = new FakeTrainExecutionService();
        var handler = CreateHandler(executionService: executionService);

        var response = await handler.RunTrainAsync(
            new RemoteRunRequest("IManifestManagerTrain", "{}", "ignored")
        );

        response.IsError.Should().BeTrue();
        response.ErrorMessage.Should().Contain("scheduler's own");
        executionService
            .Calls.Should()
            .Be(
                0,
                "a runner does not run the scheduler's own trains (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [Test]
    public async Task RunTrainAsync_ShortNameAHostTrainShares_StillRuns()
    {
        var executionService = new FakeTrainExecutionService();
        var handler = CreateHandler(
            executionService: executionService,
            registeredTrains: typeof(HostTrains.IJobRunnerTrain)
        );

        var response = await handler.RunTrainAsync(
            new RemoteRunRequest("IJobRunnerTrain", "{}", "ignored")
        );

        response.IsError.Should().BeFalse();
        executionService
            .Calls.Should()
            .Be(
                1,
                "only the scheduler's trains are refused, not a host train sharing a short name (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    #endregion

    #region Helpers

    private static TraxRequestHandler CreateHandler(
        FakeJobRunnerTrain? jobRunner = null,
        FakeTrainExecutionService? executionService = null,
        params Type[] registeredTrains
    )
    {
        return new TraxRequestHandler(
            jobRunner ?? new FakeJobRunnerTrain(),
            executionService ?? new FakeTrainExecutionService(),
            new TrustedExecutionScope(),
            new FakeTrainRegistry(typeof(TestInput)),
            new FakeTrainDiscovery(registeredTrains),
            NullLogger<TraxRequestHandler>.Instance
        );
    }

    #endregion

    #region Test Types

    public record UnregisteredInput;

    private sealed class FakeTrainRegistry(params Type[] inputTypes)
        : Trax.Mediator.Services.TrainRegistry.ITrainRegistry
    {
        public Dictionary<Type, Type> InputTypeToTrain { get; set; } =
            inputTypes.ToDictionary(t => t, _ => typeof(IJobRunnerTrain));
    }

    private sealed class FakeTrainDiscovery(Type[] serviceTypes)
        : Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService
    {
        public IReadOnlyList<Trax.Mediator.Services.TrainDiscovery.TrainRegistration> DiscoverTrains() =>
            serviceTypes
                .Select(t => new Trax.Mediator.Services.TrainDiscovery.TrainRegistration
                {
                    ServiceType = t,
                    ImplementationType = t,
                    InputType = typeof(TestInput),
                    OutputType = typeof(Unit),
                    Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
                    ServiceTypeName = t.Name,
                    ImplementationTypeName = t.Name,
                    InputTypeName = nameof(TestInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = false,
                    IsBroadcastEnabled = false,
                    IsRemote = false,
                    GraphQLOperations = default,
                })
                .ToList();
    }

    /// <summary>A host train that shares its short name with the scheduler's JobRunner.</summary>
    public static class HostTrains
    {
        public interface IJobRunnerTrain;
    }

    public record TestInput
    {
        public string Name { get; init; } = "";
        public int Value { get; init; }
    }

    public record TestOutput
    {
        public string Result { get; init; } = "";
        public int Count { get; init; }
    }

    #endregion

    #region Fakes

    private class FakeJobRunnerTrain : IJobRunnerTrain
    {
        public RunJobRequest? ReceivedRequest { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Metadata? Metadata => null;

        public Task<Unit> Run(RunJobRequest input, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            ReceivedRequest = input;
            return Task.FromResult(Unit.Default);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    private class FakeTrainExecutionService : ITrainExecutionService
    {
        public RunTrainResult? ResultToReturn { get; init; }
        public Exception? ExceptionToThrow { get; init; }
        public int Calls { get; private set; }

        public Task<QueueTrainResult> QueueAsync(
            string trainName,
            string? inputJson,
            int priority = 0,
            DateTime? scheduledAt = null,
            CancellationToken ct = default
        )
        {
            throw new NotImplementedException();
        }

        public Task<RunTrainResult> RunAsync(
            string trainName,
            string inputJson,
            CancellationToken ct = default
        )
        {
            Calls++;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(
                ResultToReturn ?? new RunTrainResult(MetadataId: 0, ExternalId: "")
            );
        }
    }

    #endregion
}
