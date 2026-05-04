using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fixtures;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// Exercises the metadata-passing branches of every TrainBus.RunAsync overload
/// (generic + non-generic, with and without CancellationToken).
/// </summary>
[TestFixture]
public class TrainBusMetadataTests
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
        if (_serviceProvider is IDisposable d)
            d.Dispose();
        TrainBus.ClearMethodCache();
    }

    private static Metadata NewPendingMetadata(string name) =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

    [Test]
    public async Task RunAsyncGeneric_WithPendingMetadata_RunsTrain()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-1", 0);
        var metadata = NewPendingMetadata("test-train");

        var result = await trainBus.RunAsync<MemoryTestOutput>(input, metadata);

        result.Should().NotBeNull();
        result.Id.Should().Be("metadata-1");
    }

    [Test]
    public async Task RunAsyncGeneric_WithNonPendingMetadata_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-2", 0);
        var metadata = NewPendingMetadata("test-train");
        metadata.TrainState = TrainState.Completed;

        Func<Task> act = () => trainBus.RunAsync<MemoryTestOutput>(input, metadata);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Pending*");
    }

    [Test]
    public async Task RunAsyncGenericWithCt_WithPendingMetadata_RunsTrain()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-3", 0);
        var metadata = NewPendingMetadata("test-train");

        var result = await trainBus.RunAsync<MemoryTestOutput>(
            input,
            CancellationToken.None,
            metadata
        );

        result.Should().NotBeNull();
        result.Id.Should().Be("metadata-3");
    }

    [Test]
    public async Task RunAsyncGenericWithCt_WithNonPendingMetadata_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-4", 0);
        var metadata = NewPendingMetadata("test-train");
        metadata.TrainState = TrainState.InProgress;

        Func<Task> act = () =>
            trainBus.RunAsync<MemoryTestOutput>(input, CancellationToken.None, metadata);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Pending*");
    }

    [Test]
    public async Task RunAsyncNonGeneric_WithPendingMetadata_RunsTrain()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-5", 0);
        var metadata = NewPendingMetadata("test-train");

        await trainBus.RunAsync(input, metadata);
    }

    [Test]
    public async Task RunAsyncNonGeneric_WithNonPendingMetadata_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-6", 0);
        var metadata = NewPendingMetadata("test-train");
        metadata.TrainState = TrainState.Failed;

        Func<Task> act = () => trainBus.RunAsync(input, metadata);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Pending*");
    }

    [Test]
    public async Task RunAsyncNonGenericWithCt_WithPendingMetadata_RunsTrain()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-7", 0);
        var metadata = NewPendingMetadata("test-train");

        await trainBus.RunAsync(input, CancellationToken.None, metadata);
    }

    [Test]
    public async Task RunAsyncNonGenericWithCt_WithNonPendingMetadata_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();
        var input = MemoryTestModelFactory.CreateInput("metadata-8", 0);
        var metadata = NewPendingMetadata("test-train");
        metadata.TrainState = TrainState.Completed;

        Func<Task> act = () => trainBus.RunAsync(input, CancellationToken.None, metadata);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Pending*");
    }

    [Test]
    public async Task InitializeTrain_NullInput_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        Action act = () => trainBus.InitializeTrain(null!);

        act.Should().Throw<TrainException>().WithMessage("*null*");
    }

    [Test]
    public async Task InitializeTrain_UnregisteredInputType_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        Action act = () => trainBus.InitializeTrain(new UnregisteredInput());

        act.Should().Throw<TrainException>().WithMessage("*Could not find train*");
    }

    [Test]
    public async Task RunAsync_UnregisteredInputType_Throws()
    {
        var trainBus = _serviceProvider.GetRequiredService<ITrainBus>();

        Func<Task> act = () => trainBus.RunAsync<object>(new UnregisteredInput());

        await act.Should().ThrowAsync<TrainException>();
    }

    private class UnregisteredInput { }
}
