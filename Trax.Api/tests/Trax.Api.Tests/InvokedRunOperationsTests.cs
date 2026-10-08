using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// A run a state machine's invoking state queued belongs to that entry of the state, so an operator cannot requeue
/// it: <c>requeueExecution</c> and the dashboard's Re-queue button call the one
/// <see cref="IOperationsService.RequeueExecutionAsync(long, bool, CancellationToken)"/>, and both refuse it with
/// the same reason (central ADR 0022). See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class InvokedRunOperationsTests
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private const string Database = "trax_api_invoked_runs";

    private ServiceProvider _provider = null!;
    private IDataContextProviderFactory _factory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(t =>
            t.AddEffects(e => e.UsePostgres(AuthE2EHost.ConnectionString(Database)))
        );
        _provider = services.BuildServiceProvider();
        _factory = _provider.GetRequiredService<IDataContextProviderFactory>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _provider.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task Requeue_of_an_invoked_run_is_refused_with_one_reason_on_both_surfaces()
    {
        var run = await SeedInvokedRunAsync();
        var operations = Operations();
        var expected = OperationsService.InvokedRunRequeueRefusal(run, "ingest");

        var graphQl = await new OperationsMutations().RequeueExecution(run, operations, default);
        var graphQlAfresh = await new OperationsMutations().RequeueExecution(
            run,
            operations,
            default,
            askAfresh: true
        );
        var dashboard = await operations.RequeueExecutionAsync(run, askAfresh: false, default);

        graphQl.Success.Should().BeFalse($"an invoked run is retried by its machine. See {Adr}");
        graphQl.Message.Should().Be(expected);
        graphQlAfresh.Success.Should().BeFalse();
        graphQlAfresh.Message.Should().Be(expected);
        dashboard.Success.Should().BeFalse();
        dashboard.Message.Should().Be(expected, "the dashboard refuses with the same reason");

        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.WorkQueues.AsNoTracking().CountAsync(w => w.TrainName == "Ingest.IFetchTrain"))
            .Should()
            .Be(0, "nothing was queued");
    }

    private IOperationsService Operations() =>
        new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            _factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            new ServiceCollection()
                .AddSingleton(Substitute.For<ICancellationRegistry>())
                .BuildServiceProvider()
        );

    private async Task<long> SeedInvokedRunAsync()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Ingest.IFetchTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                InvokedBy = new InvokedBy("ingest", Guid.NewGuid(), SnapshotOwnerKind.System),
            }
        );
        run.TrainState = TrainState.Failed;
        run.EndTime = DateTime.UtcNow;
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }
}
