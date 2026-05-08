using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Unit tests that exercise the LocalWorkerOptions branches of
/// <see cref="OperationsService.UpdateSchedulerConfigAsync"/>. The integration fixture
/// uses <c>UseInMemoryWorkers()</c> so it never registers <c>LocalWorkerOptions</c>;
/// these tests construct the service directly with a stubbed instance to cover the
/// branch.
/// </summary>
[TestFixture]
public class OperationsServiceLocalWorkerTests
{
    private static OperationsService BuildServiceWithLocalWorkers(
        out SchedulerConfiguration cfg,
        out LocalWorkerOptions workerOpts,
        out IDataContextProviderFactory factory
    )
    {
        cfg = new SchedulerConfiguration();
        workerOpts = new LocalWorkerOptions { WorkerCount = 4 };
        factory = Substitute.For<IDataContextProviderFactory>();
        var discovery = Substitute.For<ITrainDiscoveryService>();

        // Provide a no-op data context for PersistAsync calls. Use a real InMemory
        // context if persistence is needed; for change-detection tests below we only
        // need the singleton mutation path, and a stubbed factory throws if persisted.
        return new OperationsService(discovery, factory, cfg, workerOpts);
    }

    [Test]
    public async Task UpdateSchedulerConfig_LocalWorkerCount_ChangesSingleton()
    {
        var service = BuildServiceWithLocalWorkers(out _, out var workerOpts, out _);

        // PersistAsync will throw because the substitute factory has no real DB. The
        // singleton mutation runs before the persistence call, so the worker count
        // change still happens.
        try
        {
            await service.UpdateSchedulerConfigAsync(
                new UpdateSchedulerConfigInput(LocalWorkerCount: 12),
                CancellationToken.None
            );
        }
        catch { }

        workerOpts.WorkerCount.Should().Be(12);
    }

    [Test]
    public async Task UpdateSchedulerConfig_ClearLocalWorkerCount_ResetsToProcessorCount()
    {
        var service = BuildServiceWithLocalWorkers(out _, out var workerOpts, out _);
        workerOpts.WorkerCount = 999; // far from Environment.ProcessorCount

        try
        {
            await service.UpdateSchedulerConfigAsync(
                new UpdateSchedulerConfigInput(ClearLocalWorkerCount: true),
                CancellationToken.None
            );
        }
        catch { }

        workerOpts.WorkerCount.Should().Be(Environment.ProcessorCount);
    }

    [Test]
    public async Task UpdateSchedulerConfig_ClearLocalWorkerCount_AlreadyDefault_NoChange()
    {
        var service = BuildServiceWithLocalWorkers(out _, out var workerOpts, out _);
        workerOpts.WorkerCount = Environment.ProcessorCount;

        var result = await service.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(ClearLocalWorkerCount: true),
            CancellationToken.None
        );

        // No change → no persistence call → no exception from the stubbed factory.
        result.Count.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Test]
    public async Task BootstrapHostedService_FailedDependencyResolution_LogsAndReturns()
    {
        // Service provider with no registrations at all → GetRequiredService throws
        // inside StartAsync; the hosted service should swallow the exception.
        var emptyServices = new ServiceCollection().BuildServiceProvider();
        var hosted = new SchedulerConfigBootstrapHostedService(
            emptyServices,
            NullLogger<SchedulerConfigBootstrapHostedService>.Instance
        );

        // Should NOT throw.
        Func<Task> act = () => hosted.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
