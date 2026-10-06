using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.ManifestManager.Junctions;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest holds at most one queued work queue entry (the database enforces it). Triggering a
/// manifest that already has one leaves that entry to run and queues nothing more, and one such
/// manifest does not stop the others in a group or a polling cycle from being queued.
/// </summary>
[TestFixture]
public class TriggerAlreadyQueuedTests
{
    private const string Group = "already-queued-group";

    private static Task<SchedulerE2EFixture> CreateAsync(CapturingLoggerProvider? logs = null) =>
        SchedulerE2EFixture.CreateAsync(
            s =>
                s.Schedule<ISchedulerTestTrain>(
                        "aq-a",
                        new SchedulerTestInput(),
                        Every.Hours(1),
                        opts => opts.Group(Group)
                    )
                    .Schedule<ISchedulerTestTrain>(
                        "aq-b",
                        new SchedulerTestInput(),
                        Every.Hours(1),
                        opts => opts.Group(Group)
                    ),
            services =>
            {
                if (logs is not null)
                    services.AddSingleton<ILoggerProvider>(logs);
            }
        );

    private static async Task<Dictionary<string, int>> QueuedPerManifest(SchedulerE2EFixture fx)
    {
        fx.DataContext.Reset();
        return await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Where(w => w.Status == WorkQueueStatus.Queued && w.Manifest != null)
            .GroupBy(w => w.Manifest!.ExternalId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    [Test]
    public async Task Triggering_a_group_queues_the_manifests_not_yet_queued_and_reports_the_rest_skipped()
    {
        var logs = new CapturingLoggerProvider();
        await using var fx = await CreateAsync(logs);
        await fx.MaterializePendingManifestsAsync();
        await fx.Scheduler.TriggerAsync("aq-a");

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == Group);
        var queued = await fx.Scheduler.TriggerGroupAsync(group.Id);

        queued.Should().Be(1, "only aq-b was not already queued");
        var perManifest = await QueuedPerManifest(fx);
        perManifest.Should().Equal(new Dictionary<string, int> { ["aq-a"] = 1, ["aq-b"] = 1 });
        logs.Messages.Should()
            .Contain(m => m.Contains("1 already queued"), "the skipped manifest is reported");
    }

    [Test]
    public async Task Triggering_a_manifest_that_is_already_queued_leaves_its_entry_and_does_not_throw()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        await fx.Scheduler.TriggerAsync("aq-a");

        var act = () => fx.Scheduler.TriggerAsync("aq-a");

        await act.Should().NotThrowAsync();
        (await QueuedPerManifest(fx)).Should().Equal(new Dictionary<string, int> { ["aq-a"] = 1 });
    }

    [Test]
    public async Task Triggering_an_already_queued_manifest_with_a_delay_does_not_throw()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        await fx.Scheduler.TriggerAsync("aq-a");

        var act = () => fx.Scheduler.TriggerAsync("aq-a", TimeSpan.FromMinutes(5));

        await act.Should().NotThrowAsync();
        (await QueuedPerManifest(fx)).Should().Equal(new Dictionary<string, int> { ["aq-a"] = 1 });
    }

    [Test]
    public async Task One_manifest_already_queued_does_not_stop_the_cycle_queueing_the_others()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .Where(m => m.ExternalId == "aq-a" || m.ExternalId == "aq-b")
            .OrderBy(m => m.ExternalId)
            .ToListAsync();

        // The cycle loaded both as due, then aq-a was queued by a trigger before the cycle wrote.
        await fx.Scheduler.TriggerAsync("aq-a");
        fx.DataContext.Reset();

        var views = manifests
            .Select(m => new ManifestDispatchView
            {
                Manifest = m,
                ManifestGroup = m.ManifestGroup,
                FailedCount = 0,
                HasAwaitingDeadLetter = false,
                HasQueuedWork = false,
                HasActiveExecution = false,
                HasSuccessfulMetadata = false,
            })
            .ToList();

        var context = fx.Services.GetRequiredService<IDataContext>();
        using (var transaction = await context.BeginTransaction())
        {
            var junction = new CreateWorkQueueEntriesJunction(
                context,
                fx.Configuration,
                NullLogger<CreateWorkQueueEntriesJunction>.Instance,
                fx.Services.GetRequiredService<RetryDecisionReplay>()
            );
            await junction.Run(views);

            // A write after the junction, as the reapers make later in the same leader
            // transaction, still succeeds.
            context.WorkQueues.Add(
                WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = typeof(ISchedulerTestTrain).FullName!,
                        Input = "{}",
                        InputTypeName = typeof(SchedulerTestInput).FullName!,
                    }
                )
            );
            await context.SaveChanges(CancellationToken.None);
            await context.CommitTransaction();
        }

        (await QueuedPerManifest(fx))
            .Should()
            .Equal(new Dictionary<string, int> { ["aq-a"] = 1, ["aq-b"] = 1 });
    }

    [Test]
    public async Task Triggering_a_group_whose_manifests_are_all_queued_queues_nothing_and_signals_nothing()
    {
        var recording = new RecordingChangeSignal();
        await using var fx = await SchedulerE2EFixture.CreateAsync(
            s =>
                s.Schedule<ISchedulerTestTrain>(
                        "aq-a",
                        new SchedulerTestInput(),
                        Every.Hours(1),
                        opts => opts.Group(Group)
                    )
                    .Schedule<ISchedulerTestTrain>(
                        "aq-b",
                        new SchedulerTestInput(),
                        Every.Hours(1),
                        opts => opts.Group(Group)
                    ),
            services => services.AddSingleton<ITraxChangeSignal>(recording)
        );
        await fx.MaterializePendingManifestsAsync();
        await fx.Scheduler.TriggerAsync("aq-a");
        await fx.Scheduler.TriggerAsync("aq-b");
        recording.Clear();

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == Group);
        var queued = await fx.Scheduler.TriggerGroupAsync(group.Id);

        queued.Should().Be(0, "both manifests already have their queued entry");
        (await QueuedPerManifest(fx))
            .Should()
            .Equal(new Dictionary<string, int> { ["aq-a"] = 1, ["aq-b"] = 1 });
        recording
            .Domains.Should()
            .BeEmpty("nothing was written, so no dashboard or subscriber needs to refresh");
    }

    #region An entry that commits between the trigger's check and its insert

    [Test]
    public async Task Triggering_while_another_writer_queues_the_same_manifest_leaves_that_entry_and_does_not_throw()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        var manifestId = await ManifestId(fx, "aq-a");

        // Another writer has inserted the manifest's queued entry but not yet committed it, so
        // the trigger's check sees no entry and its insert waits on the unique index.
        await using var other = await OpenConnectionAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await ExecuteAsync(
            other,
            "INSERT INTO trax.work_queue (external_id, train_name, manifest_id) "
                + $"VALUES ('other-writer', 'other', {manifestId})"
        );

        var trigger = fx.Scheduler.TriggerAsync("aq-a");
        (await WaitUntilBlockedOnLockAsync())
            .Should()
            .BeTrue("the trigger's insert should wait on the other writer's uncommitted entry");
        await otherTransaction.CommitAsync();

        await trigger.Invoking(t => t).Should().NotThrowAsync();
        fx.DataContext.Reset();
        var queued = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Where(w => w.ManifestId == manifestId && w.Status == WorkQueueStatus.Queued)
            .Select(w => w.ExternalId)
            .ToListAsync();
        queued
            .Should()
            .Equal(
                ["other-writer"],
                "the entry the other writer committed runs the manifest; the trigger adds none"
            );
    }

    [Test]
    public async Task Triggering_when_the_insert_fails_for_another_reason_throws_the_failure()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        var manifestId = await ManifestId(fx, "aq-a");

        // Another writer deletes the manifest and has not committed, so the trigger still loads
        // it and its insert waits on the row lock; once the delete commits the insert fails on
        // the foreign key, which is not an entry already queued.
        await using var other = await OpenConnectionAsync();
        await using var otherTransaction = await other.BeginTransactionAsync();
        await ExecuteAsync(other, $"DELETE FROM trax.manifest WHERE id = {manifestId}");

        var trigger = fx.Scheduler.TriggerAsync("aq-a");
        (await WaitUntilBlockedOnLockAsync())
            .Should()
            .BeTrue("the trigger's insert should wait on the other writer's uncommitted delete");
        await otherTransaction.CommitAsync();

        await trigger
            .Invoking(t => t)
            .Should()
            .ThrowAsync<DbUpdateException>(
                "only a queued entry that already exists makes a failed insert a no-op"
            );
    }

    private static async Task<long> ManifestId(SchedulerE2EFixture fx, string externalId) =>
        await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId == externalId)
            .Select(m => m.Id)
            .SingleAsync();

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(TestPostgres.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Waits until a session on the test database is waiting for a lock, which is the trigger's
    /// insert waiting on the other writer's uncommitted row.
    /// </summary>
    private static async Task<bool> WaitUntilBlockedOnLockAsync()
    {
        await using var probe = await OpenConnectionAsync();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity "
                    + "WHERE datname = current_database() AND wait_event_type = 'Lock'",
                probe
            );
            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return true;

            // determinism: polls the condition, bounded by the deadline above.
            await Task.Delay(25);
        }
        return false;
    }

    #endregion

    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => messages.Enqueue(formatter(state, exception));
        }
    }

    [Test]
    public async Task Triggering_a_manifest_whose_retry_waits_out_its_backoff_brings_the_retry_forward_to_now()
    {
        var logs = new CapturingLoggerProvider();
        await using var fx = await CreateAsync(logs);
        await fx.MaterializePendingManifestsAsync();
        var retryId = await QueueRetryAsync(fx, "aq-a", DateTime.UtcNow.AddMinutes(30));

        await fx.Scheduler.TriggerAsync("aq-a");

        fx.DataContext.Reset();
        var retry = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == retryId);
        retry.Status.Should().Be(WorkQueueStatus.Queued);
        retry
            .ScheduledAt.Should()
            .BeNull(
                "brought forward to now, it stores no time, as an immediate trigger's new entry "
                    + "does, so it waits on no clock"
            );
        (await QueuedPerManifest(fx)).Should().Equal(new Dictionary<string, int> { ["aq-a"] = 1 });
        logs.Messages.Should()
            .Contain(m => m.Contains("moved it forward"), "the trigger says what it did");
    }

    [Test]
    public async Task A_delayed_trigger_brings_a_later_retry_forward_to_the_requested_time()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        var retryId = await QueueRetryAsync(fx, "aq-a", DateTime.UtcNow.AddMinutes(30));

        await fx.Scheduler.TriggerAsync("aq-a", TimeSpan.FromMinutes(5));

        fx.DataContext.Reset();
        var retry = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == retryId);
        retry
            .ScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task A_delayed_trigger_leaves_an_entry_due_sooner_at_its_own_time()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        var dueAt = DateTime.UtcNow.AddMinutes(2);
        var retryId = await QueueRetryAsync(fx, "aq-a", dueAt);

        await fx.Scheduler.TriggerAsync("aq-a", TimeSpan.FromMinutes(30));

        fx.DataContext.Reset();
        var retry = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == retryId);
        retry.ScheduledAt.Should().BeCloseTo(dueAt, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>A retry the ManifestManager queued with a backoff, due at <paramref name="dueAt"/>.</summary>
    private static async Task<long> QueueRetryAsync(
        SchedulerE2EFixture fx,
        string externalId,
        DateTime dueAt
    )
    {
        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == externalId);
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                ScheduledAt = dueAt,
            }
        );
        await fx.DataContext.Track(entry);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();
        return entry.Id;
    }
}
