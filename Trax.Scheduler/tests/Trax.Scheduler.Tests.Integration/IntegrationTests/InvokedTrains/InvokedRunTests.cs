using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// A junction of a train a machine invokes learns the instance and its owner through
/// <see cref="InvokedRun"/>, resolved from the instance's row, and only there: anywhere else it throws.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokedRunTests(ClusterStore store)
{
    private InvokeCluster _cluster = null!;

    private ClusterHost _full = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _full = _cluster.Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _full.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.Reset();
    }

    [Test]
    public async Task Junction_reads_the_invoking_owner()
    {
        // Bob holds an instance with the same id: the owner is the one the run recorded, not either.
        var id = Guid.NewGuid();
        await StepMachine.Seed(
            _full.Services,
            "bob",
            id,
            UserStepMachine.MachineId,
            "Idle",
            StepMachine.Context(InvokedStepModes.Ok)
        );
        await _full.EnterRunning("alice", id, StepMachine.Context(InvokedStepModes.Owner));

        var token = (await AliceRow(id)).InvokeToken!;
        var runId = await _full.DispatchAndRun(token);

        (await _full.Run(runId)).TrainState.Should().Be(TrainState.Completed);
        var row = await AliceRow(id);
        row.State.Should().Be("Done");
        JsonNode.Parse(row.Context)!["artifact"]!
            .GetValue<string>()
            .Should()
            .Be("owner:alice", "the junction reads the owner of the instance that invoked it");
    }

    [Test]
    public async Task Resolving_it_outside_an_invoked_run_throws()
    {
        using (var scope = _full.Services.CreateScope())
            scope
                .Invoking(s => s.ServiceProvider.GetRequiredService<InvokedRun>())
                .Should()
                .Throw<InvalidOperationException>();

        // A train run directly, not by a machine, cannot read an owner either.
        using var run = _full.Services.CreateScope();
        await run
            .ServiceProvider.GetRequiredService<ITrainBus>()
            .Awaiting(bus =>
                bus.RunAsync<InvokedStepOutput>(
                    new InvokedStepInput(InvokedStepModes.Owner, "direct")
                )
            )
            .Should()
            .ThrowAsync<Exception>();
    }

    private async Task<SnapshotDraft> AliceRow(Guid id) =>
        (await _full.Rows(id, UserStepMachine.MachineId)).Single(r => r.UserKey == "alice");
}
