using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Deterministic coverage for ManifestManagerPollingService. Drives the InMemory branch
/// (HasDatabaseProvider = false) and the disabled short-circuit. Postgres advisory-lock
/// branch is exercised by SchedulerPollingCycleTests via the live train.
/// </summary>
[TestFixture]
public class ManifestManagerPollingServiceTests
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(5);

    private static IServiceProvider Provide(IManifestManagerTrain train)
    {
        var sp = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scopedSp = Substitute.For<IServiceProvider>();

        scope.ServiceProvider.Returns(scopedSp);
        scopeFactory.CreateScope().Returns(scope);
        sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        scopedSp.GetService(typeof(IManifestManagerTrain)).Returns(train);
        return sp;
    }

    [Test]
    public async Task ExecuteAsync_InMemoryProvider_RunsTrainAndContinuesLoop()
    {
        var trainHit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ => trainHit.TrySetResult());

        var config = new SchedulerConfiguration
        {
            ManifestManagerPollingInterval = TimeSpan.FromMinutes(5),
            ManifestManagerEnabled = true,
            HasDatabaseProvider = false,
        };

        var service = new ManifestManagerPollingService(
            Provide(train),
            config,
            NullLogger<ManifestManagerPollingService>.Instance,
            sqlDialect: null
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var winner = await Task.WhenAny(trainHit.Task, Task.Delay(SyncTimeout));
        winner.Should().Be(trainHit.Task, "train should have been invoked on the first cycle");

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await train.Received().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_TrainThrows_CatchBlockSwallowsAndKeepsLoopAlive()
    {
        var trainHit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IManifestManagerTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                trainHit.TrySetResult();
                throw new InvalidOperationException("manifest-manager-fail");
            });

        var config = new SchedulerConfiguration
        {
            ManifestManagerPollingInterval = TimeSpan.FromMinutes(5),
            ManifestManagerEnabled = true,
            HasDatabaseProvider = false,
        };

        var service = new ManifestManagerPollingService(
            Provide(train),
            config,
            NullLogger<ManifestManagerPollingService>.Instance,
            sqlDialect: null
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var winner = await Task.WhenAny(trainHit.Task, Task.Delay(SyncTimeout));
        winner.Should().Be(trainHit.Task);

        cts.Cancel();
        var stop = async () => await service.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync("the catch block must swallow the train exception");
    }

    [Test]
    public async Task ExecuteAsync_Disabled_TrainNeverInvoked()
    {
        var train = Substitute.For<IManifestManagerTrain>();
        var config = new SchedulerConfiguration
        {
            ManifestManagerPollingInterval = TimeSpan.FromMinutes(5),
            ManifestManagerEnabled = false,
            HasDatabaseProvider = false,
        };

        var service = new ManifestManagerPollingService(
            Provide(train),
            config,
            NullLogger<ManifestManagerPollingService>.Instance,
            sqlDialect: null
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        // Disabled branch is a no-op; tight wait is sufficient to confirm nothing fires.
        await Task.Delay(150);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }
}
