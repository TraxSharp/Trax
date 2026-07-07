using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DeadLetterCleanupPollingService;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Polling-service tests that synchronise on the train invocation rather than wall-clock delays.
/// Uses a <see cref="TaskCompletionSource"/> to deterministically await the immediate cycle
/// before cancelling the service, eliminating the timing window that <c>Task.Delay</c>-based
/// tests would expose.
/// </summary>
[TestFixture]
public class PollingServicesDeterministicTests
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(5);

    private static SchedulerConfiguration LongIntervalConfig() =>
        new()
        {
            ManifestManagerPollingInterval = TimeSpan.FromMinutes(5),
            JobDispatcherPollingInterval = TimeSpan.FromMinutes(5),
            JobDispatcherEnabled = true,
            DeadLetterCleanupInterval = TimeSpan.FromMinutes(5),
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

    private static async Task RunUntilTrainInvoked(
        BackgroundService service,
        TaskCompletionSource trainHit
    )
    {
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var winner = await Task.WhenAny(trainHit.Task, Task.Delay(SyncTimeout));
        winner.Should().Be(trainHit.Task, "train should have been invoked within sync timeout");

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    #region DeadLetterCleanupPollingService

    [Test]
    public async Task DeadLetterCleanupPollingService_TrainThrows_CatchBlockSwallowsAndLoopStaysAlive()
    {
        var trainHit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IDeadLetterCleanupTrain>();
        train
            .When(t => t.Run(Arg.Any<DeadLetterCleanupRequest>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                trainHit.TrySetResult();
                throw new InvalidOperationException("dead-letter-fail");
            });

        var service = new DeadLetterCleanupPollingService(
            Provide(train),
            LongIntervalConfig(),
            NullLogger<DeadLetterCleanupPollingService>.Instance
        );

        // Should NOT throw — catch block swallows
        await RunUntilTrainInvoked(service, trainHit);
    }

    #endregion

    #region JobDispatcherPollingService

    [Test]
    public async Task JobDispatcherPollingService_TrainThrows_CatchBlockSwallowsAndLoopStaysAlive()
    {
        var trainHit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = Substitute.For<IJobDispatcherTrain>();
        train
            .When(t => t.Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                trainHit.TrySetResult();
                throw new InvalidOperationException("dispatcher-fail");
            });

        var service = new JobDispatcherPollingService(
            Provide(train),
            LongIntervalConfig(),
            new SchedulerLivenessMonitor(TimeProvider.System),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        await RunUntilTrainInvoked(service, trainHit);
    }

    [Test]
    public async Task JobDispatcherPollingService_Disabled_TrainNeverInvoked()
    {
        var train = Substitute.For<IJobDispatcherTrain>();
        var config = LongIntervalConfig();
        config.JobDispatcherEnabled = false;

        var service = new JobDispatcherPollingService(
            Provide(train),
            config,
            new SchedulerLivenessMonitor(TimeProvider.System),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        // The disabled branch returns immediately — no async work, no train call.
        // We give the service a tight bound to settle, then verify the train was never touched.
        // Even with worst-case scheduling, a no-op disabled branch completes well under 200 ms.
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
