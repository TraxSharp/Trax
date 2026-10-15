using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.RunStarters;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// A run records its caller as its starter only when the caller asks for it, as the GraphQL mutations do: the hex
/// SHA-256 of the principal id, never the id, on the work queue entry of a queued run and on the metadata of a run
/// that runs now.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class RunStarterTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    private const string Principal = "apikey:alice";

    private static readonly string Hash = Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes("p\0" + Principal))
    );

    private static string Train => typeof(QueueInputTests.IOptionalInputTrain).FullName!;

    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<ICurrentPrincipalProvider>(new FixedPrincipal(Principal));

    [Test]
    public async Task A_queued_run_records_its_caller_when_asked()
    {
        var recorded = await Execution.QueueAsync(
            Train,
            null,
            new QueueTrainOptions { RecordCaller = true }
        );
        var unrecorded = await Execution.QueueAsync(Train, null, new QueueTrainOptions());

        (await QueuedStarterAsync(recorded.WorkQueueId))
            .Should()
            .Be(Hash, "the caller who asked is the run's starter, per " + Adr);
        (await QueuedStarterAsync(unrecorded.WorkQueueId))
            .Should()
            .BeNull("a run nobody asked to record has no starter, per " + Adr);
    }

    [Test]
    public async Task A_run_that_runs_now_records_its_caller_on_its_metadata()
    {
        var recorded = await Execution.RunAsync(
            Train,
            "{}",
            new RunTrainOptions { RecordCaller = true }
        );
        var unrecorded = await Execution.RunAsync(Train, "{}");

        (await RunStarterAsync(recorded.MetadataId)).Should().Be(Hash);
        (await RunStarterAsync(unrecorded.MetadataId)).Should().BeNull();
    }

    [Test]
    public async Task The_caller_is_told_only_of_the_runs_they_started()
    {
        var starters = Scope.ServiceProvider.GetRequiredService<IRunStarters>();
        var queued = await Execution.QueueAsync(
            Train,
            null,
            new QueueTrainOptions { RecordCaller = true }
        );
        var ran = await Execution.RunAsync(
            Train,
            "{}",
            new RunTrainOptions { RecordCaller = true }
        );
        var nobodys = await Execution.RunAsync(Train, "{}");

        (await starters.StartedByCurrentCallerAsync(queued.ExternalId)).Should().BeTrue();
        (await starters.StartedByCurrentCallerAsync(ran.ExternalId)).Should().BeTrue();
        (await starters.StartedByCurrentCallerAsync(nobodys.ExternalId))
            .Should()
            .BeFalse("a run nobody started is nobody's, per " + Adr);
        (await starters.StartedByCurrentCallerAsync(Guid.NewGuid().ToString("N")))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task The_latest_execution_is_the_callers_own_under_a_shared_id()
    {
        var starters = Scope.ServiceProvider.GetRequiredService<IRunStarters>();
        var mine = await Execution.RunAsync(
            Train,
            "{}",
            new RunTrainOptions { RecordCaller = true }
        );
        var theirs = await Execution.RunAsync(Train, "{}");
        await ExecuteAsync(
            $"UPDATE trax.metadata SET external_id = '{mine.ExternalId}', starter_hash = 'someone-else' "
                + $"WHERE id = {theirs.MetadataId}"
        );

        (await starters.LatestExecutionStartedByCurrentCallerAsync(mine.ExternalId))
            .Should()
            .Be(
                mine.MetadataId,
                "a newer execution another caller recorded under the same id is not the caller's, per "
                    + Adr
            );
        (await starters.LatestExecutionStartedByCurrentCallerAsync(Guid.NewGuid().ToString("N")))
            .Should()
            .BeNull();
    }

    private async Task ExecuteAsync(string sql)
    {
        using var context = await ContextAsync();
        await ((DbContext)context).Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<string?> QueuedStarterAsync(long id)
    {
        using var context = await ContextAsync();
        return await ((DbContext)context)
            .Database.SqlQuery<string?>(
                $"SELECT starter_hash AS \"Value\" FROM trax.work_queue WHERE id = {id}"
            )
            .SingleAsync();
    }

    private async Task<string?> RunStarterAsync(long id)
    {
        using var context = await ContextAsync();
        return await ((DbContext)context)
            .Database.SqlQuery<string?>(
                $"SELECT starter_hash AS \"Value\" FROM trax.metadata WHERE id = {id}"
            )
            .SingleAsync();
    }

    private Task<Trax.Effect.Data.Services.DataContext.IDataContext> ContextAsync() =>
        Scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);

    private sealed class FixedPrincipal(string id) : ICurrentPrincipalProvider
    {
        public string? GetCurrentPrincipalId() => id;
    }
}
