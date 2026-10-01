using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DeadLetterCleanupPollingService;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A polling interval changed at runtime applies to the running poller's next wait, without a
/// restart.
/// </summary>
[TestFixture]
public class PollingIntervalLiveTests
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Shortening_the_dispatcher_interval_at_runtime_ends_the_current_wait()
    {
        var calls = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IJobDispatcherTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    first.TrySetResult();
                else
                    second.TrySetResult();
            });

        var config = new SchedulerConfiguration
        {
            JobDispatcherPollingInterval = TimeSpan.FromHours(1),
        };

        var sp = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scoped = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(scoped);
        scopeFactory.CreateScope().Returns(scope);
        sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        scoped.GetService(typeof(IJobDispatcherTrain)).Returns(train);

        var service = new JobDispatcherPollingService(
            sp,
            config,
            new SchedulerLivenessMonitor(TimeProvider.System),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        try
        {
            // The first cycle runs at start.
            await first.Task.WaitAsync(SyncTimeout);

            // An hour-long wait is under way; the operator shortens the interval.
            config.JobDispatcherPollingInterval = TimeSpan.FromMilliseconds(50);

            // The new interval applies to the wait already under way; before, this waited an hour.
            await second.Task.WaitAsync(SyncTimeout);
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Shortening_the_manifest_manager_interval_at_runtime_ends_the_current_wait()
    {
        var calls = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    first.TrySetResult();
                else
                    second.TrySetResult();
            });

        var config = new SchedulerConfiguration
        {
            ManifestManagerPollingInterval = TimeSpan.FromHours(1),
            HasDatabaseProvider = false,
        };
        var service = new ManifestManagerPollingService(
            Provide(typeof(IManifestManagerTrain), train),
            config,
            NullLogger<ManifestManagerPollingService>.Instance,
            sqlDialect: null
        );

        await AssertShorteningEndsTheWait(
            service,
            first,
            second,
            () => config.ManifestManagerPollingInterval = TimeSpan.FromMilliseconds(50)
        );
    }

    [Test]
    public async Task Shortening_the_dead_letter_cleanup_interval_at_runtime_ends_the_current_wait()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeDeadLetterCleanupTrain? train = null;
        train = new FakeDeadLetterCleanupTrain(() =>
        {
            if (train!.Runs == 1)
                first.TrySetResult();
            else
                second.TrySetResult();
        });

        var config = new SchedulerConfiguration
        {
            DeadLetterCleanupInterval = TimeSpan.FromHours(1),
        };
        var service = new DeadLetterCleanupPollingService(
            Provide(typeof(IDeadLetterCleanupTrain), train),
            config,
            NullLogger<DeadLetterCleanupPollingService>.Instance
        );

        await AssertShorteningEndsTheWait(
            service,
            first,
            second,
            () => config.DeadLetterCleanupInterval = TimeSpan.FromMilliseconds(50)
        );
    }

    [Test]
    public async Task Shortening_the_metadata_cleanup_interval_at_runtime_ends_the_current_wait()
    {
        var calls = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IMetadataCleanupTrain>();
        train
            .When(t => t.Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    first.TrySetResult();
                else
                    second.TrySetResult();
            });

        var config = new SchedulerConfiguration
        {
            MetadataCleanup = new MetadataCleanupConfiguration
            {
                CleanupInterval = TimeSpan.FromHours(1),
            },
        };
        var service = new MetadataCleanupPollingService(
            Provide(typeof(IMetadataCleanupTrain), train),
            config,
            NullLogger<MetadataCleanupPollingService>.Instance
        );

        await AssertShorteningEndsTheWait(
            service,
            first,
            second,
            () => config.MetadataCleanup.CleanupInterval = TimeSpan.FromMilliseconds(50)
        );
    }

    private static async Task AssertShorteningEndsTheWait(
        BackgroundService service,
        TaskCompletionSource first,
        TaskCompletionSource second,
        Action shorten
    )
    {
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        try
        {
            // The first cycle runs at start, then an hour-long wait begins.
            await first.Task.WaitAsync(SyncTimeout);

            shorten();

            // The next cycle runs after the new interval, not after the hour.
            await second.Task.WaitAsync(SyncTimeout);
        }
        finally
        {
            cts.Cancel();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static IServiceProvider Provide(Type service, object instance)
    {
        var sp = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scoped = Substitute.For<IServiceProvider>();
        scope.ServiceProvider.Returns(scoped);
        scopeFactory.CreateScope().Returns(scope);
        sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        scoped.GetService(service).Returns(instance);
        return sp;
    }
}
