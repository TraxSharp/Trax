using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fixtures;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class TrainBusErrorTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();
        _serviceProvider = TestSetup.CreateMemoryLeakTestServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        TrainBus.ClearMethodCache();
    }

    [Test]
    public void GetMethodCacheSize_AfterClear_ReturnsZero()
    {
        // Act
        var size = TrainBus.GetMethodCacheSize();

        // Assert
        size.Should().Be(0);
    }

    [Test]
    public void InitializeTrain_NullInput_ThrowsTrainException()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act & Assert
        var act = () => trainBus.InitializeTrain(null!);
        act.Should().Throw<TrainException>();
    }

    [Test]
    public void InitializeTrain_UnregisteredType_ThrowsTrainException()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act & Assert — pass an input type that has no registered train
        var act = () => trainBus.InitializeTrain("not a registered input type");
        act.Should().Throw<TrainException>();
    }

    [Test]
    public async Task RunAsync_WithNullInput_ThrowsTrainException()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act & Assert
        var act = async () => await trainBus.RunAsync(null!);
        await act.Should().ThrowAsync<TrainException>();
    }

    [Test]
    public async Task RunAsync_WithCancellationToken_NullInput_ThrowsTrainException()
    {
        // Arrange
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        // Act & Assert
        var act = async () => await trainBus.RunAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<TrainException>();
    }
}
