using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Core.Functional;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DeadLetterCleanupPollingService;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Polling-service tests that synchronise on the train invocation rather than wall-clock delays.
/// Uses a <see cref="TaskCompletionSource"/> to deterministically await the immediate cycle
/// before cancelling the service, eliminating the timing window that fixed-delay tests would
/// expose.
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

        await trainHit.Task.WaitAsync(SyncTimeout);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

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

    #region DeadLetterCleanupPollingService

    [Test]
    public async Task DeadLetterCleanupPollingService_TrainThrows_CatchBlockSwallowsAndLoopStaysAlive()
    {
        var trainHit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var train = new FakeDeadLetterCleanupTrain(() =>
        {
            trainHit.TrySetResult();
            throw new InvalidOperationException("dead-letter-fail");
        });

        var service = new DeadLetterCleanupPollingService(
            Provide<IDeadLetterCleanupTrain>(train),
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
            new DispatcherWake(),
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
        var liveness = new SchedulerLivenessMonitor(TimeProvider.System);

        var service = new JobDispatcherPollingService(
            Provide(train),
            config,
            liveness,
            new DispatcherWake(),
            NullLogger<JobDispatcherPollingService>.Instance
        );

        // A disabled dispatcher still records its cycle, so waiting for that record proves the
        // loop ran and skipped the train, rather than never having run at all.
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitUntilAsync(() => liveness.LastDispatchCompletedAt is not null, SyncTimeout);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        liveness
            .LastDispatchCompletedAt.Should()
            .NotBeNull("the disabled loop still runs its cycle");

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
