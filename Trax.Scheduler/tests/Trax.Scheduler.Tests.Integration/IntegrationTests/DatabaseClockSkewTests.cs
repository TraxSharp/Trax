using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Every due time the scheduler writes for the dispatcher to compare with <c>now()</c> is written
/// by the database's clock, not this process's. The database's clock is put three hours ahead of
/// this process's for these tests (a <c>now()</c> earlier on the search path than
/// <c>pg_catalog</c>'s), so a time written by the process's clock is three hours off and fails.
/// </summary>
/// <remarks>
/// Without the skew the two clocks are the same machine's, and a test of which one wrote a time
/// cannot fail.
/// </remarks>
[TestFixture]
public class DatabaseClockSkewTests
{
    private static readonly TimeSpan Skew = TimeSpan.FromHours(3);

    /// <summary>How far a written time may be from the expected one: the test's own run time.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(1);

    private SchedulerE2EFixture _fx = null!;

    [OneTimeSetUp]
    public async Task CreateSkewedClock()
    {
        await using var connection = new NpgsqlConnection(TestPostgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE SCHEMA IF NOT EXISTS clock_skew;
            CREATE OR REPLACE FUNCTION clock_skew.now() RETURNS timestamptz
                LANGUAGE sql STABLE AS $$ SELECT pg_catalog.now() + interval '{(int)
                Skew.TotalHours} hours' $$;
            """;
        await command.ExecuteNonQueryAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_fx is not null)
            await _fx.DisposeAsync();
    }

    private static string SkewedConnectionString =>
        new NpgsqlConnectionStringBuilder(TestPostgres.ConnectionString)
        {
            Options = "-c search_path=clock_skew,pg_catalog,trax,public",
        }.ConnectionString;

    private async Task CreateFixture(bool failingSubmitter = false)
    {
        _fx = await SchedulerE2EFixture.CreateAsync(
            scheduler =>
            {
                if (failingSubmitter)
                    scheduler.OverrideSubmitter(s =>
                        s.AddScoped<IJobSubmitter, FailingJobSubmitter>()
                    );
            },
            connectionString: SkewedConnectionString
        );

        // The skew is in place: the database's clock, as the scheduler reads it, is ahead.
        (await DatabaseNow())
            .Should()
            .BeCloseTo(DateTime.UtcNow + Skew, Tolerance, "the test's clock skew must apply");
    }

    [Test]
    public async Task A_manifests_retry_backoff_is_due_by_the_database_clock()
    {
        await CreateFixture();
        _fx.Configuration.DefaultRetryDelay = TimeSpan.FromMinutes(5);
        var manifest = await CreateManifestWhoseLastRunFailed();

        await _fx.RunManifestManagerAsync();
        _fx.DataContext.Reset();

        var entry = await _fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued);
        entry
            .ScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow + Skew + TimeSpan.FromMinutes(5), Tolerance);
    }

    [Test]
    public async Task A_failed_dispatchs_backoff_is_due_by_the_database_clock()
    {
        await CreateFixture(failingSubmitter: true);
        _fx.Configuration.MaxDispatchAttempts = 5;
        var entry = await QueueEntry(
            """{"Value":"clock"}""",
            typeof(SchedulerTestInput).AssemblyQualifiedName
        );

        await _fx.RunJobDispatcherAsync();
        _fx.DataContext.Reset();

        var requeued = await _fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == entry.Id);
        requeued.Status.Should().Be(WorkQueueStatus.Queued);
        requeued.DispatchAttempts.Should().Be(1);
        requeued
            .ScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow + Skew + TimeSpan.FromSeconds(5), Tolerance);
    }

    [Test]
    public async Task An_unknown_input_types_deferral_is_due_by_the_database_clock()
    {
        await CreateFixture();
        var entry = await QueueEntry(
            """{"Value":"clock"}""",
            "Not.A.Registered.Input, Not.An.Assembly"
        );

        await _fx.RunJobDispatcherAsync();
        _fx.DataContext.Reset();

        var deferred = await _fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == entry.Id);
        deferred.Status.Should().Be(WorkQueueStatus.Queued);
        deferred.DispatchAttempts.Should().Be(1);
        deferred
            .ScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow + Skew + TimeSpan.FromSeconds(5), Tolerance);
    }

    /// <summary>
    /// The database's clock as a connection with the scheduler's search path reads <c>now()</c>.
    /// </summary>
    private static async Task<DateTime> DatabaseNow()
    {
        await using var connection = new NpgsqlConnection(SkewedConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT now()";
        var value = (DateTime)(await command.ExecuteScalarAsync())!;
        return value.ToUniversalTime();
    }

    private async Task<WorkQueue> QueueEntry(string input, string? inputTypeName)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = input,
                InputTypeName = inputTypeName,
            }
        );
        await _fx.DataContext.Track(entry);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();
        return entry;
    }

    /// <summary>An interval manifest due now whose latest run failed an hour ago.</summary>
    private async Task<Manifest> CreateManifestWhoseLastRunFailed()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            _fx.DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "clock" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-5);
        await _fx.DataContext.Track(manifest);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();

        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "clock" },
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.Failed;
        run.StartTime = DateTime.UtcNow.AddHours(-1);
        run.EndTime = run.StartTime.AddMinutes(1);
        await _fx.DataContext.Track(run);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();
        return manifest;
    }

    /// <summary>A submitter whose every delivery fails, as an unreachable worker's would.</summary>
    private sealed class FailingJobSubmitter : IJobSubmitter
    {
        private static Task<string> Fail() =>
            throw new HttpRequestException("Simulated enqueue failure");

        public Task<string> EnqueueAsync(long metadataId) => Fail();

        public Task<string> EnqueueAsync(long metadataId, object input) => Fail();

        public Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken) =>
            Fail();

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        ) => Fail();
    }
}
