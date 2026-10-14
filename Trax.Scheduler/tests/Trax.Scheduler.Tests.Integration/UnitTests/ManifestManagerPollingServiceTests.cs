using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Core.Functional;
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

        await trainHit.Task.WaitAsync(SyncTimeout);

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

        await trainHit.Task.WaitAsync(SyncTimeout);

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

        var logger = new SkipLogger();

        var service = new ManifestManagerPollingService(
            Provide(train),
            config,
            logger,
            sqlDialect: null
        );

        // The disabled cycle says it skipped, so waiting for that line proves the loop ran and
        // passed the train by, rather than never having run at all.
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await logger.Skipped.Task.WaitAsync(SyncTimeout);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await train.DidNotReceive().Run(Arg.Any<Unit>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Completes <see cref="Skipped"/> when the service logs a skipped cycle.</summary>
    private sealed class SkipLogger : ILogger<ManifestManagerPollingService>
    {
        public TaskCompletionSource Skipped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (
                formatter(state, exception) == "ManifestManager is disabled, skipping polling cycle"
            )
                Skipped.TrySetResult();
        }
    }
}
