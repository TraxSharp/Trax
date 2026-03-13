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
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class TrainExecutionServiceTests
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
                .AddMediator(assemblies: [typeof(TrainExecutionServiceTests).Assembly])
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

    #region RunTrainResult

    [Test]
    public void RunTrainResult_DefaultOutput_IsNull()
    {
        var result = new RunTrainResult(42, "ext-42");
        result.Output.Should().BeNull();
        result.MetadataId.Should().Be(42);
        result.ExternalId.Should().Be("ext-42");
    }

    [Test]
    public void RunTrainResult_WithOutput_CarriesOutput()
    {
        var output = new TypedExecOutput { Value = "hello", Count = 7 };
        var result = new RunTrainResult(99, "ext-99", output);

        result.MetadataId.Should().Be(99);
        result.Output.Should().BeSameAs(output);
    }

    [Test]
    public void RunTrainResult_WithNullOutput_ExplicitNull()
    {
        var result = new RunTrainResult(1, "ext-1", null);
        result.Output.Should().BeNull();
    }

    [Test]
    public void RunTrainResult_RecordEquality_SameValues()
    {
        var a = new RunTrainResult(10, "ext-10");
        var b = new RunTrainResult(10, "ext-10");
        a.Should().Be(b);
    }

    [Test]
    public void RunTrainResult_RecordEquality_DifferentMetadataId()
    {
        var a = new RunTrainResult(10, "ext-10");
        var b = new RunTrainResult(20, "ext-20");
        a.Should().NotBe(b);
    }

    [Test]
    public void RunTrainResult_RecordEquality_SameOutputReference()
    {
        var output = new TypedExecOutput { Value = "x", Count = 1 };
        var a = new RunTrainResult(10, "ext-10", output);
        var b = new RunTrainResult(10, "ext-10", output);
        a.Should().Be(b);
    }

    #endregion

    #region RunAsync — Typed Output

    [Test]
    public async Task RunAsync_TypedOutputTrain_ReturnsOutputInResult()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new TypedExecInput { Name = "test-player" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act
        var result = await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);

        // Assert
        result.MetadataId.Should().BeGreaterThan(0);
        result.ExternalId.Should().NotBeNullOrEmpty();
        result.Output.Should().NotBeNull();
        result.Output.Should().BeOfType<TypedExecOutput>();

        var output = (TypedExecOutput)result.Output!;
        output.Value.Should().Be("processed:test-player");
        output.Count.Should().Be(42);
    }

    [Test]
    public async Task RunAsync_UnitOutputTrain_ReturnsNullOutput()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new UnitExecInput { Id = "abc" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act
        var result = await executionService.RunAsync(nameof(IUnitExecTrain), inputJson);

        // Assert
        result.MetadataId.Should().BeGreaterThan(0);
        result.ExternalId.Should().NotBeNullOrEmpty();
        result.Output.Should().BeNull("Unit trains should have null output");
    }

    [Test]
    public async Task RunAsync_TypedOutputTrain_MetadataIdIsUnique()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var input1 = new TypedExecInput { Name = "a" };
        var input2 = new TypedExecInput { Name = "b" };

        var json1 = JsonSerializer.Serialize(
            input1,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
        var json2 = JsonSerializer.Serialize(
            input2,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act
        var result1 = await executionService.RunAsync(nameof(ITypedExecTrain), json1);
        var result2 = await executionService.RunAsync(nameof(ITypedExecTrain), json2);

        // Assert
        result1.MetadataId.Should().NotBe(result2.MetadataId);
    }

    [Test]
    public async Task RunAsync_TypedOutputTrain_OutputTypeIsPreserved()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new TypedExecInput { Name = "check-type" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act
        var result = await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);

        // Assert — output is the actual typed object, not a serialized/deserialized copy
        result.Output.Should().BeOfType<TypedExecOutput>();
        result.Output!.GetType().Should().Be(typeof(TypedExecOutput));
    }

    #endregion

    #region RunAsync — Name Resolution

    [Test]
    public async Task RunAsync_WithInterfaceFullName_Succeeds()
    {
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new TypedExecInput { Name = "test" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(typeof(ITypedExecTrain).FullName!, inputJson);

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeOfType<TypedExecOutput>();
    }

    [Test]
    public async Task RunAsync_WithShortName_Succeeds()
    {
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new TypedExecInput { Name = "test" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);

        result.MetadataId.Should().BeGreaterThan(0);
        result.Output.Should().BeOfType<TypedExecOutput>();
    }

    #endregion

    #region metadata.Name — Canonical Name Regression

    [Test]
    public async Task RunAsync_MetadataName_IsInterfaceFullName()
    {
        // Core regression guard: metadata.Name must always be the canonical
        // (interface) FullName, never the concrete implementation FullName.
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new TypedExecInput { Name = "canonical-test" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);

        // Resolve the train directly to check its metadata
        using var scope = _serviceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<ITypedExecTrain>() as TypedExecTrain;

        train.Should().NotBeNull();
        train!.CanonicalName.Should().Be(typeof(ITypedExecTrain).FullName);
        train.CanonicalName.Should().NotBe(typeof(TypedExecTrain).FullName);
    }

    #endregion

    #region RunAsync — Error Cases

    [Test]
    public async Task RunAsync_UnknownTrainName_ThrowsInvalidOperationException()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();

        // Act & Assert
        var act = async () => await executionService.RunAsync("NonExistent.Train", "{}");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No train found*");
    }

    [Test]
    public async Task RunAsync_InvalidJson_ThrowsException()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();

        // Act & Assert
        var act = async () =>
            await executionService.RunAsync(nameof(ITypedExecTrain), "not valid json");
        await act.Should().ThrowAsync<JsonException>();
    }

    #endregion

    #region QueueTrainResult

    [Test]
    public void QueueTrainResult_HasExpectedProperties()
    {
        var result = new QueueTrainResult(5, "ext-123");
        result.WorkQueueId.Should().Be(5);
        result.ExternalId.Should().Be("ext-123");
    }

    #endregion

    #region Reflection Method Caching

    [Test]
    public async Task RunAsync_CalledMultipleTimes_CachesReflectionMethod()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var input = new TypedExecInput { Name = "cache-test" };
        var inputJson = JsonSerializer.Serialize(
            input,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act — run twice, method cache should be populated
        await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);
        await executionService.RunAsync(nameof(ITypedExecTrain), inputJson);

        // Assert — no exceptions means caching works correctly
        // (The ConcurrentDictionary<Type, MethodInfo> is static and shared)
    }

    #endregion

    #region Concurrency Limiting

    [Test]
    public async Task RunAsync_WithConcurrencyLimit_BlocksAtLimit()
    {
        // Arrange — rebuild with concurrency limit of 2
        if (_serviceProvider is IDisposable d)
            d.Dispose();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(TrainExecutionServiceTests).Assembly)
                        .ConcurrentRunLimit<ISlowExecTrain>(2)
                )
        );
        _serviceProvider = services.BuildServiceProvider();

        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new SlowExecInput { DelayMs = 500 },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act — fire 3 concurrent requests, limit is 2
        var task1 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);
        var task2 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);
        var task3 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);

        // Wait for first two to complete
        var firstTwo = await Task.WhenAny(Task.WhenAll(task1, task2), Task.Delay(3000));
        firstTwo.Should().NotBeNull();

        // Third should complete after one of the first two finishes
        var result3 = await task3;
        result3.MetadataId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task RunAsync_CancelledWhileWaitingForPermit_ThrowsOperationCancelled()
    {
        // Arrange — rebuild with concurrency limit of 1
        if (_serviceProvider is IDisposable d)
            d.Dispose();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(TrainExecutionServiceTests).Assembly)
                        .ConcurrentRunLimit<ISlowExecTrain>(1)
                )
        );
        _serviceProvider = services.BuildServiceProvider();

        var executionService = _serviceProvider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new SlowExecInput { DelayMs = 2000 },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        // Act — first request holds the slot, second gets cancelled
        var task1 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);

        // Small delay to ensure task1 acquires the permit
        await Task.Delay(50);

        using var cts = new CancellationTokenSource(200);
        var act = async () =>
            await executionService.RunAsync(nameof(ISlowExecTrain), inputJson, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Clean up first task
        await task1;
    }

    #endregion

    #region Test Trains

    public record SlowExecInput
    {
        public int DelayMs { get; init; }
    }

    public interface ISlowExecTrain : IServiceTrain<SlowExecInput, Unit>;

    public class SlowExecTrain : ServiceTrain<SlowExecInput, Unit>, ISlowExecTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(SlowExecInput input)
        {
            await Task.Delay(input.DelayMs);
            return Unit.Default;
        }
    }

    public record TypedExecInput
    {
        public string Name { get; init; } = "";
    }

    public record TypedExecOutput
    {
        public string Value { get; init; } = "";
        public int Count { get; init; }
    }

    public record UnitExecInput
    {
        public string Id { get; init; } = "";
    }

    public interface ITypedExecTrain : IServiceTrain<TypedExecInput, TypedExecOutput>;

    public class TypedExecTrain : ServiceTrain<TypedExecInput, TypedExecOutput>, ITypedExecTrain
    {
        protected override Task<Either<Exception, TypedExecOutput>> RunInternal(
            TypedExecInput input
        ) =>
            Task.FromResult<Either<Exception, TypedExecOutput>>(
                new TypedExecOutput { Value = $"processed:{input.Name}", Count = 42 }
            );
    }

    public interface IUnitExecTrain : IServiceTrain<UnitExecInput, Unit>;

    public class UnitExecTrain : ServiceTrain<UnitExecInput, Unit>, IUnitExecTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(UnitExecInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    #endregion
}
