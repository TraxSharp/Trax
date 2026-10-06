using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Unit tests that exercise the LocalWorkerOptions branches of
/// <see cref="OperationsService.UpdateSchedulerConfigAsync"/>. The integration fixture
/// uses <c>UseInMemoryWorkers()</c> so it never registers <c>LocalWorkerOptions</c>;
/// these tests construct the service directly, over an InMemory database, to cover the
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
        cfg = new SchedulerConfiguration { IsSchedulerHost = true };
        workerOpts = new LocalWorkerOptions { WorkerCount = 4 };
        var discovery = Substitute.For<ITrainDiscoveryService>();

        // A fresh InMemory database per service: the save reads and writes the settings row.
        factory = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider()
            .GetRequiredService<IDataContextProviderFactory>();

        // These tests exercise scheduler-config mutation, not enqueueing, so the execution
        // service is never called — but it is required rather than optional so that no code
        // path can enqueue without going through authorization.
        return new OperationsService(
            discovery,
            factory,
            cfg,
            Substitute.For<ITrainExecutionService>(),
            workerOpts
        );
    }

    [Test]
    public async Task UpdateSchedulerConfig_LocalWorkerCount_ChangesSingleton()
    {
        var service = BuildServiceWithLocalWorkers(out _, out var workerOpts, out _);

        var result = await service.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(LocalWorkerCount: 12),
            CancellationToken.None
        );

        result.Count.Should().Be(1);

        workerOpts.WorkerCount.Should().Be(12);
    }

    [Test]
    public async Task UpdateSchedulerConfig_ClearLocalWorkerCount_ResetsToProcessorCount()
    {
        var service = BuildServiceWithLocalWorkers(out _, out var workerOpts, out _);
        workerOpts.WorkerCount = 999; // far from Environment.ProcessorCount

        await service.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(ClearLocalWorkerCount: true),
            CancellationToken.None
        );

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

    [Test]
    public async Task UpdateSchedulerConfig_PatchNamingNoWorkerCount_LeavesTheWorkerCountAlone()
    {
        var service = BuildServiceWithLocalWorkers(out var cfg, out var workerOpts, out _);

        var result = await service.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(MaxActiveJobs: 5),
            CancellationToken.None
        );

        result.Count.Should().Be(1, "only MaxActiveJobs was named");
        cfg.MaxActiveJobs.Should().Be(5);
        workerOpts.WorkerCount.Should().Be(4);
    }

    [Test]
    public async Task BootstrapHostedService_StoredWorkerCount_AppliesToTheLocalWorkers()
    {
        var (hosted, _, workerOpts, _) = await BootstrapWithStoredWorkerCount(6);
        try
        {
            workerOpts.WorkerCount.Should().Be(6, "the stored count replaces the configured 4");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task BootstrapHostedService_StoredWorkerCountOutOfRange_IsSkippedAndLogged()
    {
        var (hosted, _, workerOpts, warnings) = await BootstrapWithStoredWorkerCount(1_000_000);
        try
        {
            workerOpts.WorkerCount.Should().Be(4, "a count the host cannot run is not applied");
            warnings
                .Should()
                .ContainSingle(w => w.Contains("LocalWorkerCount") && w.Contains("between 1 and"));
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task BootstrapHostedService_StoredRowWithoutAWorkerCount_KeepsTheConfiguredCount()
    {
        var (hosted, cfg, workerOpts, _) = await BootstrapWithStoredWorkerCount(null);
        try
        {
            cfg.DefaultMaxRetries.Should().Be(6, "the rest of the row applies");
            workerOpts
                .WorkerCount.Should()
                .Be(4, "an unset column leaves the host's configured count");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task BootstrapHostedService_StoppedAfterItWasDisposed_DoesNotThrow()
    {
        var (hosted, _, _, _) = await BootstrapWithStoredWorkerCount(null);

        // A host can dispose its services before or while it stops them.
        hosted.Dispose();
        var stop = () => hosted.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync("stopping a disposed settings service is a no-op");
        await stop.Should().NotThrowAsync("and so is stopping it twice");
        hosted.Invoking(h => h.Dispose()).Should().NotThrow("disposing twice is harmless too");
    }

    [Test]
    public async Task BootstrapHostedService_StoppedTwice_DoesNotThrow()
    {
        var (hosted, _, _, _) = await BootstrapWithStoredWorkerCount(null);
        var stop = () => hosted.StopAsync(CancellationToken.None);

        await stop.Should().NotThrowAsync();
        await stop.Should().NotThrowAsync("a host may stop a service twice");
        hosted.Dispose();
    }

    /// <summary>
    /// Stores a settings row with <paramref name="workerCount"/> and starts the settings service
    /// on a host that runs four local workers.
    /// </summary>
    private static async Task<(
        SchedulerConfigBootstrapHostedService Hosted,
        SchedulerConfiguration Configuration,
        LocalWorkerOptions Workers,
        List<string> Warnings
    )> BootstrapWithStoredWorkerCount(int? workerCount)
    {
        var cfg = new SchedulerConfiguration { IsSchedulerHost = true };
        var workerOpts = new LocalWorkerOptions { WorkerCount = 4 };
        var services = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .AddSingleton(cfg)
            .AddSingleton(workerOpts)
            .BuildServiceProvider();

        var factory = services.GetRequiredService<IDataContextProviderFactory>();
        using (var db = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            db.SchedulerConfigs.Add(
                new Trax.Effect.Models.SchedulerConfig.SchedulerConfig
                {
                    DefaultMaxRetries = 6,
                    LocalWorkerCount = workerCount,
                    UpdatedAt = DateTime.UtcNow,
                }
            );
            await db.SaveChanges(CancellationToken.None);
        }

        var logger = new WarningLogger();
        var hosted = new SchedulerConfigBootstrapHostedService(services, logger);
        await hosted.StartAsync(CancellationToken.None);
        return (hosted, cfg, workerOpts, logger.Warnings);
    }

    private sealed class WarningLogger : ILogger<SchedulerConfigBootstrapHostedService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                lock (Warnings)
                    Warnings.Add(formatter(state, exception));
        }
    }
}
