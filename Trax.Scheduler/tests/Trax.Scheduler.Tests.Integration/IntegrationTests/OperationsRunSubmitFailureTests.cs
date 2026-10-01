using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>OperationsService.RunTrainAsync</c> fails the run's row when its submit throws, but a
/// submitter that throws may still have delivered the job, and the runner that has it moves the
/// row out of Pending. The failure is written only while the row is still Pending, so it never
/// lands over a run a runner already started, even when the two writes race.
/// </summary>
[TestFixture]
public class OperationsRunSubmitFailureTests
{
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        _provider = new ServiceCollection()
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, StartsTheRunThenThrows>()
                        )
                    )
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _provider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        using var scope = _provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [Test]
    public async Task A_failed_submit_does_not_overwrite_a_run_the_runner_claimed_meanwhile()
    {
        using var scope = _provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();

        var result = await operations.RunTrainAsync(
            new RunTrainInput(typeof(ISchedulerTestTrain).FullName!, """{"value":"x"}"""),
            CancellationToken.None
        );

        result
            .Success.Should()
            .BeTrue("the runner has the run, so the submit is reported as done, not as an error");

        var runner = StartsTheRunThenThrows.Runner;
        runner.Should().NotBeNull("the submitter started the run");
        await runner!;

        using var check = _provider.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<IDataContext>();
        var run = await db.Metadatas.AsNoTracking().SingleAsync();
        run.TrainState.Should()
            .Be(
                TrainState.InProgress,
                "the runner claimed the run before the failure could be written; the failure is "
                    + "written only to a row that is still Pending"
            );
    }

    /// <summary>
    /// Stands in for a runner that accepted the job and is claiming it (an uncommitted move to
    /// InProgress holding the row's lock) while the submitter reports a failure. The claim commits
    /// once the service's own write to the row is waiting on that lock.
    /// </summary>
    private sealed class StartsTheRunThenThrows : IJobSubmitter
    {
        public static Task? Runner { get; private set; }

        public Task<string> EnqueueAsync(long metadataId) =>
            EnqueueAsync(metadataId, CancellationToken.None);

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            EnqueueAsync(metadataId, CancellationToken.None);

        public Task<string> EnqueueAsync(long metadataId, object input, CancellationToken ct) =>
            EnqueueAsync(metadataId, ct);

        public async Task<string> EnqueueAsync(long metadataId, CancellationToken ct)
        {
            var connection = new NpgsqlConnection(TestPostgres.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            var claim = await connection.BeginTransactionAsync(CancellationToken.None);
            await using (
                var update = new NpgsqlCommand(
                    "UPDATE trax.metadata SET train_state = 'in_progress' WHERE id = @id",
                    connection,
                    claim
                )
            )
            {
                update.Parameters.AddWithValue("id", metadataId);
                await update.ExecuteNonQueryAsync(CancellationToken.None);
            }

            Runner = CommitOnceAnotherWriterWaits(connection, claim);
            throw new InvalidOperationException("the runner answered with an error");
        }

        private static async Task CommitOnceAnotherWriterWaits(
            NpgsqlConnection connection,
            NpgsqlTransaction claim
        )
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            await using (var probe = new NpgsqlConnection(TestPostgres.ConnectionString))
            {
                await probe.OpenAsync();
                while (DateTime.UtcNow < deadline)
                {
                    await using var waiting = new NpgsqlCommand(
                        """
                        SELECT count(*) FROM pg_stat_activity
                        WHERE wait_event_type = 'Lock' AND query ILIKE '%UPDATE%metadata%'
                        """,
                        probe
                    );
                    if ((long)(await waiting.ExecuteScalarAsync())! > 0)
                        break;
                    await Task.Yield();
                }
            }

            await claim.CommitAsync();
            await claim.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
