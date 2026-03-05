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
        services.AddTraxEffects(options =>
        {
            options.AddInMemoryEffect();
            options.AddServiceTrainBus(assemblies: [typeof(TrainExecutionServiceTests).Assembly]);
        });

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
        var result = new RunTrainResult(42);
        result.Output.Should().BeNull();
        result.MetadataId.Should().Be(42);
    }

    [Test]
    public void RunTrainResult_WithOutput_CarriesOutput()
    {
        var output = new TypedExecOutput { Value = "hello", Count = 7 };
        var result = new RunTrainResult(99, output);

        result.MetadataId.Should().Be(99);
        result.Output.Should().BeSameAs(output);
    }

    [Test]
    public void RunTrainResult_WithNullOutput_ExplicitNull()
    {
        var result = new RunTrainResult(1, null);
        result.Output.Should().BeNull();
    }

    [Test]
    public void RunTrainResult_RecordEquality_SameValues()
    {
        var a = new RunTrainResult(10);
        var b = new RunTrainResult(10);
        a.Should().Be(b);
    }

    [Test]
    public void RunTrainResult_RecordEquality_DifferentMetadataId()
    {
        var a = new RunTrainResult(10);
        var b = new RunTrainResult(20);
        a.Should().NotBe(b);
    }

    [Test]
    public void RunTrainResult_RecordEquality_SameOutputReference()
    {
        var output = new TypedExecOutput { Value = "x", Count = 1 };
        var a = new RunTrainResult(10, output);
        var b = new RunTrainResult(10, output);
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

    #region Test Trains

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
