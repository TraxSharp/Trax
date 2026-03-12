using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class RunExecutorTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(RunExecutorTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        TrainBus.ClearMethodCache();
    }

    #region IRunExecutor Registration

    [Test]
    public void DefaultRunExecutor_IsLocalRunExecutor()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();
        executor.Should().BeOfType<LocalRunExecutor>();
    }

    [Test]
    public void TrainExecutionService_IsRegistered()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        service.Should().BeOfType<TrainExecutionService>();
    }

    #endregion

    #region LocalRunExecutor — Typed Output

    [Test]
    public async Task LocalRunExecutor_TypedTrain_ReturnsOutputAndMetadataId()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();

        var input = new RunExecInput { Name = "test-player" };

        var result = await executor.ExecuteAsync(
            typeof(RunExecTrain).FullName!,
            input,
            typeof(RunExecOutput)
        );

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().NotBeNull();
        result.Output.Should().BeOfType<RunExecOutput>();
        var output = (RunExecOutput)result.Output!;
        output.Value.Should().Be("processed:test-player");
        output.Count.Should().Be(42);
    }

    [Test]
    public async Task LocalRunExecutor_UnitTrain_ReturnsNullOutput()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();

        var input = new RunUnitInput { Id = "abc" };

        var result = await executor.ExecuteAsync(
            typeof(RunUnitTrain).FullName!,
            input,
            typeof(Unit)
        );

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeNull("Unit trains should have null output");
    }

    [Test]
    public async Task LocalRunExecutor_MultipleExecutions_HaveUniqueMetadataIds()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();

        var result1 = await executor.ExecuteAsync(
            typeof(RunExecTrain).FullName!,
            new RunExecInput { Name = "a" },
            typeof(RunExecOutput)
        );

        var result2 = await executor.ExecuteAsync(
            typeof(RunExecTrain).FullName!,
            new RunExecInput { Name = "b" },
            typeof(RunExecOutput)
        );

        result1.MetadataId.Should().NotBe(result2.MetadataId);
    }

    #endregion

    #region TrainExecutionService Delegation

    [Test]
    public async Task RunAsync_DelegatesToRunExecutor_TypedOutput()
    {
        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new RunExecInput { Name = "delegate-test" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(IRunExecTrain), inputJson);

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeOfType<RunExecOutput>();
        var output = (RunExecOutput)result.Output!;
        output.Value.Should().Be("processed:delegate-test");
    }

    [Test]
    public async Task RunAsync_DelegatesToRunExecutor_UnitOutput()
    {
        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new RunUnitInput { Id = "unit-delegate" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(IRunUnitTrain), inputJson);

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeNull();
    }

    [Test]
    public async Task RunAsync_UnknownTrain_ThrowsInvalidOperationException()
    {
        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () => await executionService.RunAsync("NonExistent.Train", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No train found*");
    }

    [Test]
    public async Task RunAsync_InvalidJson_ThrowsJsonException()
    {
        using var scope = _serviceProvider.CreateScope();
        var executionService = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await executionService.RunAsync(nameof(IRunExecTrain), "not valid json");

        await act.Should().ThrowAsync<JsonException>();
    }

    #endregion

    #region Custom IRunExecutor Override

    [Test]
    public async Task CustomRunExecutor_OverridesDefault()
    {
        // Simulate what UseRemoteRun() does — register a custom IRunExecutor
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(RunExecutorTests).Assembly])
        );

        // Override with a fake executor (last registration wins)
        services.AddScoped<IRunExecutor, FakeRunExecutor>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();
        executor.Should().BeOfType<FakeRunExecutor>();

        // Verify TrainExecutionService uses the overridden executor
        var executionService = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new RunExecInput { Name = "custom" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(IRunExecTrain), inputJson);

        result.MetadataId.Should().Be(99999);
        result.Output.Should().Be("fake-output");
    }

    #endregion

    #region LocalRunExecutor — Cancellation

    [Test]
    public async Task LocalRunExecutor_CancellationToken_PropagatedToTrain()
    {
        using var scope = _serviceProvider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IRunExecutor>();
        using var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        var act = async () =>
            await executor.ExecuteAsync(
                typeof(SlowRunTrain).FullName!,
                new SlowRunInput(),
                typeof(Unit),
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Test Trains

    public record RunExecInput
    {
        public string Name { get; init; } = "";
    }

    public record RunExecOutput
    {
        public string Value { get; init; } = "";
        public int Count { get; init; }
    }

    public record RunUnitInput
    {
        public string Id { get; init; } = "";
    }

    public record SlowRunInput;

    public interface IRunExecTrain : IServiceTrain<RunExecInput, RunExecOutput>;

    public class RunExecTrain : ServiceTrain<RunExecInput, RunExecOutput>, IRunExecTrain
    {
        protected override Task<Either<Exception, RunExecOutput>> RunInternal(RunExecInput input) =>
            Task.FromResult<Either<Exception, RunExecOutput>>(
                new RunExecOutput { Value = $"processed:{input.Name}", Count = 42 }
            );
    }

    public interface IRunUnitTrain : IServiceTrain<RunUnitInput, Unit>;

    public class RunUnitTrain : ServiceTrain<RunUnitInput, Unit>, IRunUnitTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(RunUnitInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface ISlowRunTrain : IServiceTrain<SlowRunInput, Unit>;

    public class SlowRunTrain : ServiceTrain<SlowRunInput, Unit>, ISlowRunTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(SlowRunInput input)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
            return Unit.Default;
        }
    }

    public class FakeRunExecutor : IRunExecutor
    {
        public Task<RunTrainResult> ExecuteAsync(
            string trainName,
            object input,
            Type outputType,
            CancellationToken ct = default
        ) => Task.FromResult(new RunTrainResult(99999, "fake-ext", "fake-output"));
    }

    #endregion
}
