using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.LocalWorkerService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A local worker keeps its claim on a job for as long as the job runs, so a job that runs longer
/// than <see cref="LocalWorkerOptions.VisibilityTimeout"/> is not claimed again by another worker,
/// and a cancel still reaches it.
/// </summary>
[TestFixture]
public class LocalWorkerHeartbeatTests : TestSetup
{
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(2);

    [Test]
    public async Task A_job_running_past_the_visibility_timeout_is_not_claimed_again_and_can_still_be_cancelled()
    {
        var key = Guid.NewGuid().ToString("N");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeliveryProbeTrain.Gates[key] = gate;
        var started = DeliveryProbeTrain.Started.GetOrAdd(
            key,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );

        var metadata = await QueueLocalJob(new DeliveryProbeInput { Key = key });

        // Two workers on one host share its cancellation registry.
        var registry = new CancellationRegistry();
        using var stop = new CancellationTokenSource();
        var first = NewWorker(registry);
        await first.StartAsync(stop.Token);

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var claimedAt = DateTime.UtcNow;

            var second = NewWorker(registry);
            await second.StartAsync(stop.Token);

            // negative-wait: the job must still be held by the first worker well after its
            // visibility timeout has passed, which only elapsed time can show.
            while (DateTime.UtcNow - claimedAt < VisibilityTimeout * 2.5)
                await Task.Delay(TimeSpan.FromMilliseconds(100));

            DataContext.Reset();
            var jobRunnerRuns = await DataContext
                .Metadatas.AsNoTracking()
                .CountAsync(m => m.Name.Contains(nameof(JobRunnerTrain)));
            jobRunnerRuns.Should().Be(1, "only the first worker claimed the job");
            DeliveryProbeTrain.Runs[key].Should().Be(1);

            registry.TryCancel(metadata.Id).Should().BeTrue("the running job is still registered");
            DeliveryProbeTrain.Tokens[key].IsCancellationRequested.Should().BeTrue();

            await WaitForRunState(metadata.Id, TrainState.Cancelled);

            await second.StopAsync(CancellationToken.None);
        }
        finally
        {
            gate.TrySetResult();
            stop.Cancel();
            await first.StopAsync(CancellationToken.None);
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task Jobs_claimed_in_a_batch_keep_their_claim_while_an_earlier_job_in_it_runs()
    {
        // A worker claims a batch and runs it in order. Only the running job's claim used to be
        // refreshed, so the jobs waiting behind a long one became claimable again once
        // VisibilityTimeout passed, and another worker took them.
        var first = Guid.NewGuid().ToString("N");
        var waiting = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeliveryProbeTrain.Gates[first] = gate;
        var started = DeliveryProbeTrain.Started.GetOrAdd(
            first,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );

        await QueueLocalJob(new DeliveryProbeInput { Key = first });
        var waitingRuns = new List<Metadata>();
        foreach (var key in waiting)
            waitingRuns.Add(await QueueLocalJob(new DeliveryProbeInput { Key = key }));

        var registry = new CancellationRegistry();
        using var stop = new CancellationTokenSource();
        var batching = NewWorker(registry, batchSize: 3);
        await batching.StartAsync(stop.Token);
        LocalWorkerService? other = null;

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var claimedAt = DateTime.UtcNow;

            other = NewWorker(registry, batchSize: 1);
            await other.StartAsync(stop.Token);

            // negative-wait: the waiting jobs must still be held well after their visibility
            // timeout has passed, which only elapsed time can show.
            while (DateTime.UtcNow - claimedAt < VisibilityTimeout * 2.5)
                await Task.Delay(TimeSpan.FromMilliseconds(100));

            foreach (var key in waiting)
                DeliveryProbeTrain
                    .Runs.ContainsKey(key)
                    .Should()
                    .BeFalse("the batch's worker still holds the jobs it has not reached");

            gate.TrySetResult();
            foreach (var run in waitingRuns)
                await WaitForRunState(run.Id, TrainState.Completed);
            foreach (var key in waiting)
                DeliveryProbeTrain.Runs[key].Should().Be(1);
        }
        finally
        {
            gate.TrySetResult();
            stop.Cancel();
            if (other is not null)
                await other.StopAsync(CancellationToken.None);
            await batching.StopAsync(CancellationToken.None);
            DeliveryProbeTrain.Forget(first);
            foreach (var key in waiting)
                DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task A_claim_refresh_that_fails_is_logged_and_the_job_still_runs_to_completion()
    {
        var key = Guid.NewGuid().ToString("N");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeliveryProbeTrain.Gates[key] = gate;

        var metadata = await QueueLocalJob(new DeliveryProbeInput { Key = key });
        var jobId = await DataContext
            .BackgroundJobs.AsNoTracking()
            .Where(j => j.MetadataId == metadata.Id)
            .Select(j => j.Id)
            .SingleAsync();

        // The database refuses every refresh of this job's claim; the claim itself, which sets
        // fetched_at from null, is allowed.
        var function = $"refuse_refresh_{jobId}";
        await ExecuteSqlAsync(
            $"CREATE FUNCTION trax.{function}() RETURNS trigger LANGUAGE plpgsql AS $$ "
                + $"BEGIN IF OLD.id = {jobId} AND OLD.fetched_at IS NOT NULL THEN "
                + "RAISE EXCEPTION 'refresh refused'; END IF; RETURN NEW; END $$; "
                + $"CREATE TRIGGER {function} BEFORE UPDATE ON trax.background_job "
                + $"FOR EACH ROW EXECUTE FUNCTION trax.{function}();"
        );

        var logger = new CapturingLogger();
        using var stop = new CancellationTokenSource();
        var worker = NewWorker(
            new CancellationRegistry(),
            logger,
            visibilityTimeout: TimeSpan.FromSeconds(1)
        );
        try
        {
            await worker.StartAsync(stop.Token);

            (
                await WaitUntilAsync(
                    () => logger.Has(LogLevel.Warning, "could not refresh its claim"),
                    TimeSpan.FromSeconds(30)
                )
            )
                .Should()
                .BeTrue("a refresh the database refuses should be logged as a warning");

            gate.TrySetResult();
            await WaitForRunState(metadata.Id, TrainState.Completed);
            logger
                .Has(LogLevel.Error, "failed job")
                .Should()
                .BeFalse("a failed refresh does not fail the job it was keeping claimed");
        }
        finally
        {
            gate.TrySetResult();
            stop.Cancel();
            await worker.StopAsync(CancellationToken.None);
            await ExecuteSqlAsync(
                $"DROP TRIGGER IF EXISTS {function} ON trax.background_job; "
                    + $"DROP FUNCTION IF EXISTS trax.{function}();"
            );
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task A_visibility_timeout_too_short_to_divide_runs_the_job_without_a_heartbeat()
    {
        var key = Guid.NewGuid().ToString("N");
        var metadata = await QueueLocalJob(new DeliveryProbeInput { Key = key });

        var logger = new CapturingLogger();
        using var stop = new CancellationTokenSource();
        var worker = NewWorker(
            new CancellationRegistry(),
            logger,
            visibilityTimeout: TimeSpan.Zero
        );
        try
        {
            await worker.StartAsync(stop.Token);

            await WaitForRunState(metadata.Id, TrainState.Completed);
            (
                await WaitUntilAsync(
                    () => logger.Has(LogLevel.Debug, "completed job"),
                    TimeSpan.FromSeconds(30)
                )
            )
                .Should()
                .BeTrue();
            logger
                .Entries.Should()
                .NotContain(
                    e => e.Level >= LogLevel.Warning,
                    "a zero timeout has no refresh interval, so there is no heartbeat to start or fail"
                );
        }
        finally
        {
            stop.Cancel();
            await worker.StopAsync(CancellationToken.None);
            DeliveryProbeTrain.Forget(key);
        }
    }

    // A third of either is outside what a PeriodicTimer accepts (one millisecond to about 49.7
    // days). The timer's refusal surfaced from the finally around the run, so a job that had
    // succeeded was logged as failed and never left the cancellation registry.
    [TestCase(1, TestName = "A visibility timeout of a millisecond")]
    [TestCase(150 * 24 * 60 * 60 * 1000L, TestName = "A visibility timeout of 150 days")]
    public async Task A_visibility_timeout_outside_the_timer_range_completes_the_job_and_unregisters_it(
        long visibilityTimeoutMs
    )
    {
        var key = Guid.NewGuid().ToString("N");
        var metadata = await QueueLocalJob(new DeliveryProbeInput { Key = key });

        var logger = new CapturingLogger();
        var registry = new CancellationRegistry();
        using var stop = new CancellationTokenSource();
        var worker = NewWorker(
            registry,
            logger,
            visibilityTimeout: TimeSpan.FromMilliseconds(visibilityTimeoutMs)
        );
        try
        {
            await worker.StartAsync(stop.Token);

            await WaitForRunState(metadata.Id, TrainState.Completed);
            (
                await WaitUntilAsync(
                    () => logger.Has(LogLevel.Debug, "completed job"),
                    TimeSpan.FromSeconds(30)
                )
            )
                .Should()
                .BeTrue();
            logger
                .Has(LogLevel.Error, "failed job")
                .Should()
                .BeFalse("the job succeeded, whatever became of its heartbeat");
            registry
                .TryCancel(metadata.Id)
                .Should()
                .BeFalse("a finished job leaves the cancellation registry");
        }
        finally
        {
            stop.Cancel();
            await worker.StopAsync(CancellationToken.None);
            DeliveryProbeTrain.Forget(key);
        }
    }

    private static async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TestPostgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            // determinism: polls the condition, bounded by the timeout above.
            await Task.Delay(25);
        }
        return condition();
    }

    private LocalWorkerService NewWorker(
        ICancellationRegistry registry,
        ILogger<LocalWorkerService> logger,
        TimeSpan visibilityTimeout
    ) =>
        new(
            Scope.ServiceProvider,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(100),
                VisibilityTimeout = visibilityTimeout,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            },
            registry,
            logger,
            Scope.ServiceProvider.GetRequiredService<ISqlDialect>()
        );

    private sealed class CapturingLogger : ILogger<LocalWorkerService>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(
            LogLevel Level,
            string Message
        )> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries.ToList();

        public bool Has(LogLevel level, string fragment) =>
            _entries.Any(e => e.Level == level && e.Message.Contains(fragment));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => _entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private LocalWorkerService NewWorker(ICancellationRegistry registry, int batchSize = 1) =>
        new(
            Scope.ServiceProvider,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(100),
                VisibilityTimeout = VisibilityTimeout,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                BatchSize = batchSize,
            },
            registry,
            Scope.ServiceProvider.GetRequiredService<ILogger<LocalWorkerService>>(),
            Scope.ServiceProvider.GetRequiredService<ISqlDialect>()
        );

    private async Task WaitForRunState(long metadataId, TrainState state)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            var row = await DataContext
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == metadataId);
            if (row.TrainState == state)
                return;
            await Task.Yield();
        }

        throw new TimeoutException($"Metadata {metadataId} did not reach {state}.");
    }

    private async Task<Metadata> QueueLocalJob(DeliveryProbeInput input)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(DeliveryProbeTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadata.Id,
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputType = typeof(DeliveryProbeInput).FullName,
            }
        );
        await DataContext.Track(job);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }
}
