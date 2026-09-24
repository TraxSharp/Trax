using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
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
        // TrainNotFoundException extends InvalidOperationException; it uses a generic
        // public message so unauthenticated probers can't distinguish "missing" from
        // "present but gated".
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*requested train was not found*");
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
            new SlowExecInput(),
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
        using var gate = HoldUntilReleased.Arm();

        // Act — fire 3 concurrent requests, limit is 2
        var task1 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);
        var task2 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);
        var task3 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);

        (await gate.WaitForEntriesAsync(2))
            .Should()
            .BeTrue("two runs fit under the limit, so both reach the train and hold a permit");

        // negative-wait: nothing signals that the third run is parked on the limiter, so the
        // test gives it a short window to wrongly reach the train, and requires that it does not.
        (await gate.WaitForEntriesAsync(1, TimeSpan.FromMilliseconds(250)))
            .Should()
            .BeFalse("the third run waits for a permit while the first two hold both");
        task3.IsCompleted.Should().BeFalse();

        gate.Release();

        // Third completes once one of the first two hands its permit back
        var results = await Task.WhenAll(task1, task2, task3).WaitAsync(HoldUntilReleased.Timeout);
        results.Should().OnlyContain(r => r.MetadataId > 0);
        (await gate.WaitForEntriesAsync(1))
            .Should()
            .BeTrue("the third run reached the train after a permit was returned");
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
            new SlowExecInput(),
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );
        using var gate = HoldUntilReleased.Arm();

        // Act — the first request reaches the train, so it holds the only permit
        var task1 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson);
        (await gate.WaitForEntriesAsync(1))
            .Should()
            .BeTrue("the first run has to be holding the permit before the second asks for it");

        using var cts = new CancellationTokenSource();
        var task2 = executionService.RunAsync(nameof(ISlowExecTrain), inputJson, cts.Token);
        task2.IsCompleted.Should().BeFalse("the second run is waiting for the held permit");

        await cts.CancelAsync();

        // Assert
        var waiting = async () => await task2.WaitAsync(HoldUntilReleased.Timeout);
        await waiting.Should().ThrowAsync<OperationCanceledException>();
        gate.Entries.Should().Be(0, "the cancelled run never got a permit, so never ran");

        // The first run still holds its permit and finishes normally once released
        gate.Release();
        (await task1.WaitAsync(HoldUntilReleased.Timeout)).MetadataId.Should().BeGreaterThan(0);
    }

    #endregion

    #region Test Trains

    public record SlowExecInput;

    public interface ISlowExecTrain : IServiceTrain<SlowExecInput, Unit>;

    public class SlowExecTrain : ServiceTrain<SlowExecInput, Unit>, ISlowExecTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<HoldUntilReleased>().Resolve();
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
        protected override Task<Either<Exception, TypedExecOutput>> Junctions() =>
            Chain<BuildTypedExecOutput>().Resolve();
    }

    public interface IUnitExecTrain : IServiceTrain<UnitExecInput, Unit>;

    public class UnitExecTrain : ServiceTrain<UnitExecInput, Unit>, IUnitExecTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    #endregion

    /// <summary>
    /// Holds each run inside the train, and so inside its concurrency permit, until the test
    /// releases it. Every run that gets this far counts as an entry, which is how a test knows a
    /// run holds a permit without guessing how long acquiring one takes.
    /// </summary>
    internal sealed class HoldUntilReleased : Junction<SlowExecInput, Unit>
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private static Gate _current = new();

        public static Gate Arm() => _current = new Gate();

        public override async Task<Unit> Run(SlowExecInput input)
        {
            var gate = _current;
            gate.Enter();
            await gate.Released.WaitAsync(CancellationToken);

            return Unit.Default;
        }

        internal sealed class Gate : IDisposable
        {
            private readonly SemaphoreSlim _entered = new(0);
            private readonly TaskCompletionSource _released = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            public Task Released => _released.Task;

            /// <summary>Entries not yet consumed by <see cref="WaitForEntriesAsync"/>.</summary>
            public int Entries => _entered.CurrentCount;

            public void Enter() => _entered.Release();

            public void Release() => _released.TrySetResult();

            public async Task<bool> WaitForEntriesAsync(int count, TimeSpan? within = null)
            {
                for (var i = 0; i < count; i++)
                {
                    if (!await _entered.WaitAsync(within ?? Timeout))
                        return false;
                }

                return true;
            }

            // Released on the way out so a failed assertion cannot leave a run parked.
            public void Dispose() => Release();
        }
    }

    /// <summary>Builds the typed output these execution tests assert on.</summary>
    internal sealed class BuildTypedExecOutput : Junction<TypedExecInput, TypedExecOutput>
    {
        public override Task<TypedExecOutput> Run(TypedExecInput input) =>
            Task.FromResult(new TypedExecOutput { Value = $"processed:{input.Name}", Count = 42 });
    }
}
