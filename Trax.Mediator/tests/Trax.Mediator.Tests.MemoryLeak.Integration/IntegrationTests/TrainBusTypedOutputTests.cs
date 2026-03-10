using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fixtures;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Tests that TrainBus.RunAsync (non-generic) correctly handles trains with non-Unit output types.
/// The non-generic overloads must await the Task without casting to Task&lt;Unit&gt;, since
/// the actual train may return any TOutput.
/// </summary>
[TestFixture]
public class TrainBusTypedOutputTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();
        _serviceProvider = TestSetup.CreateMemoryOnlyTestServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        TrainBus.ClearMethodCache();
    }

    [Test]
    public async Task RunAsync_NonGeneric_WorksWithTypedOutputTrain()
    {
        // Arrange — MemoryTestTrain returns MemoryTestOutput, not Unit
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput(id: "typed-output-1", processingDelayMs: 0);

        // Act — non-generic RunAsync should not throw InvalidCastException
        await trainBus.RunAsync(input);
    }

    [Test]
    public async Task RunAsync_NonGeneric_WithCancellationToken_WorksWithTypedOutputTrain()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput(id: "typed-output-ct", processingDelayMs: 0);

        // Act
        await trainBus.RunAsync(input, CancellationToken.None);
    }

    [Test]
    public async Task RunAsync_Generic_ReturnsTypedOutput()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput(id: "typed-generic", processingDelayMs: 0);

        // Act
        var result = await trainBus.RunAsync<MemoryTestOutput>(input);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("typed-generic");
        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_Generic_WithCancellationToken_ReturnsTypedOutput()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput(
            id: "typed-generic-ct",
            processingDelayMs: 0
        );

        // Act
        var result = await trainBus.RunAsync<MemoryTestOutput>(input, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("typed-generic-ct");
        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_NonGeneric_MultipleTypedTrains_AllSucceed()
    {
        // Arrange — run multiple typed output trains to verify no cross-contamination
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act — run several trains with non-Unit output via non-generic RunAsync
        for (var i = 0; i < 10; i++)
        {
            var input = MemoryTestModelFactory.CreateInput(id: $"batch-{i}", processingDelayMs: 0);
            await trainBus.RunAsync(input);
        }
    }

    [Test]
    public async Task RunAsync_NonGeneric_ConcurrentTypedTrains_AllSucceed()
    {
        // Arrange — concurrent execution to verify thread safety
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var tasks = Enumerable
            .Range(0, 20)
            .Select(i =>
            {
                var input = MemoryTestModelFactory.CreateInput(
                    id: $"concurrent-{i}",
                    processingDelayMs: 0
                );
                return trainBus.RunAsync(input);
            })
            .ToList();

        // Act & Assert — all should complete without InvalidCastException
        await Task.WhenAll(tasks);
    }
}
