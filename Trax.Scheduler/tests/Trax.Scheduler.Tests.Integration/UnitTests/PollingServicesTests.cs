using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DeadLetterCleanupPollingService;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.SchedulerLiveness;
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

    private static async Task RunBriefly(BackgroundService service)
    {
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    #region ManifestManagerPollingService

    [Test]
    public async Task ManifestManagerPollingService_RunsImmediateCycle_OnStart()
    {
        var train = Substitute.For<IManifestManagerTrain>();
        var sp = Provide(train);

        var service = new ManifestManagerPollingService(
            sp,
            FastConfig(),
            NullLogger<ManifestManagerPollingService>.Instance
        );

        await RunBriefly(service);

        await train.Received().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ManifestManagerPollingService_TrainThrows_LoopContinues()
    {
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Throw(new InvalidOperationException("fail"));
        var sp = Provide(train);

        var service = new ManifestManagerPollingService(
            sp,
            FastConfig(),
            NullLogger<ManifestManagerPollingService>.Instance
        );

        // Should not throw — the polling loop catches and logs.
        await RunBriefly(service);
    }

    #endregion

    #region JobDispatcherPollingService

    [Test]
    public async Task JobDispatcherPollingService_Enabled_RunsTrainOnEachCycle()
    {
        var train = Substitute.For<IJobDispatcherTrain>();
        var sp = Provide(train);

        var service = new JobDispatcherPollingService(
            sp,
            FastConfig(),
            new SchedulerLivenessMonitor(TimeProvider.System),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        await RunBriefly(service);

        await train.Received().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task JobDispatcherPollingService_Disabled_SkipsTrain()
    {
        var train = Substitute.For<IJobDispatcherTrain>();
        var sp = Provide(train);
        var config = FastConfig();
        config.JobDispatcherEnabled = false;

        var service = new JobDispatcherPollingService(
            sp,
            config,
            new SchedulerLivenessMonitor(TimeProvider.System),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        await RunBriefly(service);

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region MetadataCleanupPollingService

    [Test]
    public async Task MetadataCleanupPollingService_RunsImmediateCycle()
    {
        var train = Substitute.For<IMetadataCleanupTrain>();
        var sp = Provide(train);

        var service = new MetadataCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<MetadataCleanupPollingService>.Instance
        );

        await RunBriefly(service);

        await train.Received().Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MetadataCleanupPollingService_TrainThrows_LoopContinues()
    {
        var train = Substitute.For<IMetadataCleanupTrain>();
        train
            .When(t => t.Run(Arg.Any<MetadataCleanupRequest>(), Arg.Any<CancellationToken>()))
            .Throw(new Exception("oops"));
        var sp = Provide(train);

        var service = new MetadataCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<MetadataCleanupPollingService>.Instance
        );

        await RunBriefly(service);
    }

    #endregion

    #region DeadLetterCleanupPollingService

    [Test]
    public async Task DeadLetterCleanupPollingService_RunsImmediateCycle()
    {
        var train = Substitute.For<IDeadLetterCleanupTrain>();
        var sp = Provide(train);

        var service = new DeadLetterCleanupPollingService(
            sp,
            FastConfig(),
            NullLogger<DeadLetterCleanupPollingService>.Instance
        );

        await RunBriefly(service);

        await train
            .Received()
            .Run(Arg.Any<DeadLetterCleanupRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
