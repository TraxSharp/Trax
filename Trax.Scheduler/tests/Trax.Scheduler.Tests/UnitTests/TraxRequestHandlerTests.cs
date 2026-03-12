using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class TraxRequestHandlerTests
{
    #region ExecuteJobAsync

    [Test]
    public async Task ExecuteJobAsync_WithInput_DeserializesAndRunsJobRunner()
    {
        // Arrange
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

        // Act
        var result = await handler.ExecuteJobAsync(request);

        // Assert
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
        // Arrange
        var request = new RemoteJobRequest(MetadataId: 200);
        var jobRunner = new FakeJobRunnerTrain();
        var handler = CreateHandler(jobRunner: jobRunner);

        // Act
        var result = await handler.ExecuteJobAsync(request);

        // Assert
        result.MetadataId.Should().Be(200);
        jobRunner.ReceivedRequest.Should().NotBeNull();
        jobRunner.ReceivedRequest!.MetadataId.Should().Be(200);
        jobRunner.ReceivedRequest.Input.Should().BeNull();
    }

    [Test]
    public async Task ExecuteJobAsync_TrainFails_Throws()
    {
        // Arrange
        var request = new RemoteJobRequest(MetadataId: 300);
        var jobRunner = new FakeJobRunnerTrain
        {
            ExceptionToThrow = new InvalidOperationException("Train exploded"),
        };
        var handler = CreateHandler(jobRunner: jobRunner);

        // Act
        var act = async () => await handler.ExecuteJobAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Train exploded");
    }

    #endregion

    #region RunTrainAsync

    [Test]
    public async Task RunTrainAsync_Success_ReturnsSerializedOutput()
    {
        // Arrange
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

        // Act
        var response = await handler.RunTrainAsync(request);

        // Assert
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
        // Arrange
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

        // Act
        var response = await handler.RunTrainAsync(request);

        // Assert
        response.MetadataId.Should().Be(600);
        response.IsError.Should().BeFalse();
        response.OutputJson.Should().BeNull();
        response.OutputType.Should().BeNull();
    }

    [Test]
    public async Task RunTrainAsync_TrainFails_ReturnsErrorResponse()
    {
        // Arrange
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

        // Act
        var response = await handler.RunTrainAsync(request);

        // Assert
        response.IsError.Should().BeTrue();
        response.MetadataId.Should().Be(0);
        response.ErrorMessage.Should().Be("Something broke");
        response.OutputJson.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static TraxRequestHandler CreateHandler(
        FakeJobRunnerTrain? jobRunner = null,
        FakeTrainExecutionService? executionService = null
    )
    {
        return new TraxRequestHandler(
            jobRunner ?? new FakeJobRunnerTrain(),
            executionService ?? new FakeTrainExecutionService(),
            NullLogger<TraxRequestHandler>.Instance
        );
    }

    #endregion

    #region Test Types

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

        public Task<QueueTrainResult> QueueAsync(
            string trainName,
            string inputJson,
            int priority = 0,
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
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(
                ResultToReturn ?? new RunTrainResult(MetadataId: 0, ExternalId: "")
            );
        }
    }

    #endregion
}
