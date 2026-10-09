using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// <see cref="IMachineInstances.Advance{TMachine}"/> moves a system-owned instance from code, as the system: leaving
/// an invoking state cancels its run and entering one queues a new run under a new token, in one transaction, and
/// the triggers a user's advance may not fire are refused here too. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class SystemInstanceAdvanceTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InvokeHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Leaving_and_reentering_the_invoking_state_cancels_the_run_and_queues_a_new_one()
    {
        var key = MachineKey.Of("partition", Guid.NewGuid().ToString());
        var started = await _host.Start<PartitionMachine>(key);
        var first = (await _host.Runs(started.Id)).Single();

        (await Advance<PartitionMachine>(key, "Stop"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Idle");
        (await _host.Runs(started.Id))
            .Single()
            .Status.Should()
            .Be(WorkQueueStatus.Cancelled, $"leaving the state cancels its run. See {Adr}");
        (await _host.Row(started.Id, PartitionMachine.MachineId))!.InvokeToken.Should().BeNull();

        (await Advance<PartitionMachine>(key, "Go"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Running");
        var runs = await _host.Runs(started.Id);
        runs.Should().HaveCount(2, "entering the state again queues a new run");
        runs[1].ExternalId.Should().NotBe(first.ExternalId);
        runs[1].InvokingOwnerKind.Should().Be(SnapshotOwnerKind.System);
        (await _host.Row(started.Id, PartitionMachine.MachineId))!
            .InvokeToken.Should()
            .Be(runs[1].ExternalId, "the token names the new entry's run");
    }

    [Test]
    public async Task An_outcome_trigger_is_refused_and_nothing_is_written()
    {
        var key = MachineKey.Of("partition", Guid.NewGuid().ToString());
        var started = await _host.Start<PartitionMachine>(key);
        var before = await _host.Row(started.Id, PartitionMachine.MachineId);

        (await Advance<PartitionMachine>(key, "Running.done"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("outcome-bound", $"only the run's outcome applies it. See {Adr}");

        var after = await _host.Row(started.Id, PartitionMachine.MachineId);
        after!.State.Should().Be("Running");
        after
            .ConcurrencyToken.Should()
            .Be(before!.ConcurrencyToken, "a refused advance writes nothing");
        (await _host.Runs(started.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task A_trigger_with_no_edge_from_the_state_is_a_no_transition()
    {
        var key = MachineKey.Of("partition", Guid.NewGuid().ToString());
        await _host.Start<PartitionMachine>(key);

        (await Advance<PartitionMachine>(key, "Retry"))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("no-transition");
    }

    [Test]
    public async Task A_key_with_no_instance_is_not_found()
    {
        (await Advance<PartitionMachine>(MachineKey.Of("never-started"), "Stop"))
            .Should()
            .BeOfType<AdvanceOutcome.NotFound>();
    }

    [Test]
    public async Task A_user_owned_machine_is_refused()
    {
        var act = () => Advance<GoodMachine>(MachineKey.Of("anything"), "Go");

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*user-owned machine*advance*");
    }

    private async Task<AdvanceOutcome> Advance<TMachine>(MachineKey key, string trigger)
        where TMachine : IMachine
    {
        using var scope = _host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Advance<TMachine>(key, trigger);
    }
}
