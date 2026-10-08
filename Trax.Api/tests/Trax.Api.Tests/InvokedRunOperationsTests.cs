using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
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

    [Test]
    public async Task No_operator_surface_returns_a_snapshots_context()
    {
        var secret = "secret-" + Guid.NewGuid().ToString("N");
        var machine = "ingest-" + Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        var system = await SeedDraftAsync(machine, SnapshotOwnerKind.System, secret, token);
        var run = await SeedInvokedRunAsync(machine, system.Id, SnapshotOwnerKind.System, token);
        await using (var db = await _factory.CreateDbContextAsync(default))
            await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE trax.metadata SET invoke_output = {secret} WHERE id = {run}"
            );
        var operations = Operations();
        var key = new MachineInstanceKey(machine, SnapshotOwnerKind.System, system.Id);

        var views = new object?[]
        {
            await new OperationsQueries().GetMachineInstance(
                machine,
                SnapshotOwnerKind.System,
                system.Id,
                operations,
                default
            ),
            await new OperationsQueries().GetMachineInstances(operations, default, machine),
            await operations.GetMachineInstanceAsync(key, default),
            await operations.GetMachineInstanceRunsAsync(key, default),
            await new OperationsMutations().CancelMachineInstance(
                machine,
                SnapshotOwnerKind.System,
                system.Id,
                operations,
                default
            ),
            await operations.CancelMachineInstanceAsync(key, default),
        };

        views.Should().AllSatisfy(v => v.Should().NotBeNull());
        var detail = (MachineInstanceDetail)views[0]!;
        detail
            .InvokedRuns.Should()
            .ContainSingle("the run the instance invoked is listed")
            .Which.IsLive.Should()
            .BeTrue();
        foreach (var view in views)
            JsonSerializer
                .Serialize(view)
                .Should()
                .NotContain(
                    secret,
                    $"neither the context nor the output a machine reads reaches an operator. See {Adr}"
                );
    }

    [Test]
    public async Task The_invoked_runs_are_the_same_on_GraphQL_and_the_dashboard()
    {
        var machine = "ingest-" + Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        var system = await SeedDraftAsync(machine, SnapshotOwnerKind.System, "{}", token);
        var older = await SeedInvokedRunAsync(machine, system.Id, SnapshotOwnerKind.System);
        var live = await SeedInvokedRunAsync(machine, system.Id, SnapshotOwnerKind.System, token);
        var operations = Operations();

        var graphQl = (
            await new OperationsQueries().GetMachineInstance(
                machine,
                SnapshotOwnerKind.System,
                system.Id,
                operations,
                default
            )
        )!;
        var dashboard = (
            await operations.GetMachineInstanceRunsAsync(
                new MachineInstanceKey(machine, SnapshotOwnerKind.System, system.Id),
                default
            )
        )!;

        graphQl.InvokedRuns.Select(r => r.Id).Should().Equal(live, older);
        graphQl
            .InvokedRuns.Should()
            .BeEquivalentTo(
                dashboard.Items.Select(r => new
                {
                    r.Id,
                    r.ExternalId,
                    Name = r.TrainName,
                    r.TrainState,
                    r.StartTime,
                    r.EndTime,
                    r.FailureClass,
                    r.CancellationRequested,
                    r.IsLive,
                }),
                o => o.WithStrictOrdering(),
                "the dashboard's instance page and machineInstance read one call (central docs/0022)"
            );
        graphQl.IsInvokedRunsCapped.Should().Be(dashboard.Capped);
        graphQl.QueuedInvokedRunEntryId.Should().Be(dashboard.QueuedEntryId);
    }

    [Test]
    public async Task Cancel_returns_one_outcome_and_reason_on_both_surfaces()
    {
        var machine = "ingest-" + Guid.NewGuid().ToString("N");
        var operations = Operations();
        var user = await SeedDraftAsync(machine, SnapshotOwnerKind.User, "{}", null);
        var idle = await SeedDraftAsync(machine, SnapshotOwnerKind.System, "{}", null);

        // Each refusal, through the mutation and through the service the dashboard calls.
        foreach (
            var (ownerKind, id, outcome) in new[]
            {
                (SnapshotOwnerKind.User, user.Id, MachineInstanceCancelOutcome.UserOwned),
                (SnapshotOwnerKind.System, Guid.NewGuid(), MachineInstanceCancelOutcome.NotFound),
                (SnapshotOwnerKind.System, idle.Id, MachineInstanceCancelOutcome.NoLiveRun),
            }
        )
        {
            var graphQl = await new OperationsMutations().CancelMachineInstance(
                machine,
                ownerKind,
                id,
                operations,
                default
            );
            var dashboard = await operations.CancelMachineInstanceAsync(
                new MachineInstanceKey(
                    machine,
                    ownerKind,
                    id,
                    ownerKind == SnapshotOwnerKind.User ? user.RowId : null
                ),
                default
            );

            graphQl.Success.Should().BeFalse();
            graphQl.Outcome.Should().Be(outcome);
            dashboard.Outcome.Should().Be(outcome);
            graphQl.Message.Should().Be(dashboard.Message, "one reason on both surfaces");
        }

        // A queued run, cancelled on each surface: one instance each, the same outcome and words.
        var viaGraphQl = await SeedQueuedInstanceAsync(machine);
        var viaDashboard = await SeedQueuedInstanceAsync(machine);

        var cancelledByGraphQl = await new OperationsMutations().CancelMachineInstance(
            machine,
            SnapshotOwnerKind.System,
            viaGraphQl.Id,
            operations,
            default
        );
        var cancelledByDashboard = await operations.CancelMachineInstanceAsync(
            new MachineInstanceKey(machine, SnapshotOwnerKind.System, viaDashboard.Id),
            default
        );

        cancelledByGraphQl.Success.Should().BeTrue();
        cancelledByGraphQl.Outcome.Should().Be(MachineInstanceCancelOutcome.RunCancelled);
        cancelledByDashboard.Outcome.Should().Be(MachineInstanceCancelOutcome.RunCancelled);
        cancelledByGraphQl
            .Message.Should()
            .Be(
                OperationsService.RunCancelledMessage(
                    new MachineInstanceKey(machine, SnapshotOwnerKind.System, viaGraphQl.Id)
                )
            );
        cancelledByDashboard
            .Message.Should()
            .Be(
                OperationsService.RunCancelledMessage(
                    new MachineInstanceKey(machine, SnapshotOwnerKind.System, viaDashboard.Id)
                )
            );

        await using var db = await _factory.CreateDbContextAsync(default);
        (
            await db
                .WorkQueues.AsNoTracking()
                .Where(w => w.InvokingMachine == machine)
                .Select(w => w.Status)
                .ToListAsync()
        )
            .Should()
            .AllBeEquivalentTo(WorkQueueStatus.Cancelled);
    }

    private async Task<SnapshotDraft> SeedDraftAsync(
        string machine,
        SnapshotOwnerKind owner,
        string secretOrContext,
        string? invokeToken,
        Guid? id = null
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var row = new SnapshotDraft
        {
            Id = id ?? Guid.NewGuid(),
            OwnerKind = owner,
            UserKey = owner == SnapshotOwnerKind.User ? $"user-{Guid.NewGuid():N}" : null,
            Machine = machine,
            Version = 1,
            State = "Running",
            Context = secretOrContext.StartsWith('{')
                ? secretOrContext
                : JsonSerializer.Serialize(new { apiKey = secretOrContext }),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            InvokeToken = invokeToken,
        };
        db.SnapshotDrafts.Add(row);
        await db.SaveChanges(default);
        return row;
    }

    private async Task<SnapshotDraft> SeedQueuedInstanceAsync(string machine)
    {
        var id = Guid.NewGuid();
        await using var db = await _factory.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Ingest.IStageTrain",
                InvokedBy = new InvokedBy(machine, id, SnapshotOwnerKind.System),
            }
        );
        await db.Track(entry);
        await db.SaveChanges(default);
        return await SeedDraftAsync(machine, SnapshotOwnerKind.System, "{}", entry.ExternalId, id);
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

    private Task<long> SeedInvokedRunAsync() =>
        SeedInvokedRunAsync("ingest", Guid.NewGuid(), SnapshotOwnerKind.System);

    private async Task<long> SeedInvokedRunAsync(
        string machine,
        Guid instance,
        SnapshotOwnerKind owner,
        string? externalId = null
    )
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Ingest.IFetchTrain",
                ExternalId = externalId ?? Guid.NewGuid().ToString("N"),
                Input = null,
                InvokedBy = new InvokedBy(machine, instance, owner),
            }
        );
        run.TrainState = TrainState.Failed;
        run.EndTime = DateTime.UtcNow;
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }
}
