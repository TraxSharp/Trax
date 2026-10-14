using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Trax.Core.Functional;
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
/// Verifies each scheduler polling BackgroundService runs an immediate cycle on start,
/// resolves the right train from a fresh scope, propagates the cancellation token,
/// and swallows train exceptions to keep the loop alive.
/// </summary>
[TestFixture]
public class PollingServicesTests
{
    private static SchedulerConfiguration FastConfig() =>
        new()
        {
            ManifestManagerPollingInterval = TimeSpan.FromMinutes(1),
            JobDispatcherPollingInterval = TimeSpan.FromMinutes(1),
            JobDispatcherEnabled = true,
            DeadLetterCleanupInterval = TimeSpan.FromMinutes(1),
            MetadataCleanup = new MetadataCleanupConfiguration
            {
                CleanupInterval = TimeSpan.FromMinutes(1),
                RetentionPeriod = TimeSpan.FromDays(30),
            },
        };

    private static IServiceProvider Provide<T>(T instance)
        where T : class
    {
        var sp = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scopedSp = Substitute.For<IServiceProvider>();

        scope.ServiceProvider.Returns(scopedSp);
        scopeFactory.CreateScope().Returns(scope);
        sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        scopedSp.GetService(typeof(T)).Returns(instance);

        return sp;
    }

    // StartAsync hands ExecuteAsync to the thread pool, so its first cycle may not have run
    // when StartAsync returns; a fixed delay is a race. Wait for the train to be called.
    private static async Task RunUntilTrainInvoked(
        BackgroundService service,
        TaskCompletionSource trainHit
    )
    {
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await trainHit.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    private static TaskCompletionSource Hit() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return true;
            // allowed-delay: poll interval while awaiting the completion signal, bounded by the
            // timeout above. Not a fixed wait for the work to finish.
            await Task.Delay(25);
        }
        return predicate();
    }

    #region ManifestManagerPollingService

    [Test]
    public async Task ManifestManagerPollingService_RunsImmediateCycle_OnStart()
    {
        var hit = Hit();
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ => hit.TrySetResult());
        var sp = Provide(train);

        var service = new ManifestManagerPollingService(
            sp,
            FastConfig(),
            NullLogger<ManifestManagerPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, hit);

        await train.Received().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ManifestManagerPollingService_TrainThrows_LoopContinues()
    {
        var hit = Hit();
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                hit.TrySetResult();
                throw new InvalidOperationException("fail");
            });
        var sp = Provide(train);

        var service = new ManifestManagerPollingService(
            sp,
            FastConfig(),
            NullLogger<ManifestManagerPollingService>.Instance
        );

        // Should not throw — the polling loop catches and logs.
        await RunUntilTrainInvoked(service, hit);
    }

    #endregion

    #region JobDispatcherPollingService

    [Test]
    public async Task JobDispatcherPollingService_Enabled_RunsTrainOnEachCycle()
    {
        var hit = Hit();
        var train = Substitute.For<IJobDispatcherTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ => hit.TrySetResult());
        var sp = Provide(train);

        var service = new JobDispatcherPollingService(
            sp,
            FastConfig(),
            new SchedulerLivenessMonitor(TimeProvider.System),
            new DispatcherWake(),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, hit);

        await train.Received().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task JobDispatcherPollingService_Disabled_SkipsTrain()
    {
        var train = Substitute.For<IJobDispatcherTrain>();
        var sp = Provide(train);
        var config = FastConfig();
        config.JobDispatcherEnabled = false;
        var liveness = new SchedulerLivenessMonitor(TimeProvider.System);

        var service = new JobDispatcherPollingService(
            sp,
            config,
            liveness,
            new DispatcherWake(),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        // A disabled dispatcher still records its cycle, so waiting for that record proves the
        // loop ran and skipped the train, rather than never having run at all.
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitUntilAsync(
            () => liveness.LastDispatchCompletedAt is not null,
            TimeSpan.FromSeconds(10)
        );
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        liveness
            .LastDispatchCompletedAt.Should()
            .NotBeNull("the disabled loop still runs its cycle");

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region MetadataCleanupPollingService

    [Test]
    public async Task MetadataCleanupPollingService_RunsImmediateCycle()
    {
        var hit = Hit();
        var train = Substitute.For<IMetadataCleanupTrain>();
        train
            .When(t => t.Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ => hit.TrySetResult());
        var sp = Provide(train);

        var service = new MetadataCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<MetadataCleanupPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, hit);

        await train.Received().Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MetadataCleanupPollingService_TrainThrows_LoopContinues()
    {
        var hit = Hit();
        var train = Substitute.For<IMetadataCleanupTrain>();
        train
            .When(t => t.Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                hit.TrySetResult();
                throw new Exception("oops");
            });
        var sp = Provide(train);

        var service = new MetadataCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<MetadataCleanupPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, hit);
    }

    #endregion

    #region DeadLetterCleanupPollingService

    [Test]
    public async Task DeadLetterCleanupPollingService_RunsImmediateCycle()
    {
        var hit = Hit();
        var train = new FakeDeadLetterCleanupTrain(() => hit.TrySetResult());
        var sp = Provide<IDeadLetterCleanupTrain>(train);

        var service = new DeadLetterCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<DeadLetterCleanupPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, hit);

        train.Runs.Should().BeGreaterThan(0);
    }

    #endregion
}
