using AwesomeAssertions;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// An invoked run's outcome is one committed write to the instance's row, whichever path applies it, and sends
/// exactly one change notice on <c>trax_snapshot_changed</c> carrying the row's <c>row_id</c>: through the hook on
/// the host that ran the train, or through the reconciler on another host. The advance into the invoking state is
/// tested in <c>Trax.Mediator.Tests.StateMachine.Integration</c>'s test of the same name; these two live here
/// because only <see cref="InvokeCluster"/> dispatches and runs a train across hosts. Postgres only: the notice is a
/// Postgres <c>NOTIFY</c>. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class SnapshotChangeOutcomeNotifyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private InvokeCluster _cluster = null!;

    // Registers the machines: their hook and their reconciler. No scheduler.
    private ClusterHost _api = null!;

    // A second API host over the same database.
    private ClusterHost _api2 = null!;

    // Dispatches and runs trains, and registers no machines, so nothing it runs is delivered by a hook.
    private ClusterHost _worker = null!;

    // Dispatches and runs trains and registers the machines, so its hook delivers each outcome at once.
    private ClusterHost _full = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(ClusterStore.Postgres);
        _api = _cluster.Host(machines: true, scheduler: false);
        _api2 = _cluster.Host(machines: true, scheduler: false);
        _worker = _cluster.Host(machines: false, scheduler: true);
        _full = _cluster.Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _api2.DisposeAsync();
        await _worker.DisposeAsync();
        await _full.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.Reset();
    }

    [TearDown]
    public void ReleaseGate() => InvokedStepGate.Release();

    [Test]
    public async Task An_outcome_applied_by_the_hook_notifies_once()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var started = await SystemRow(instance.Id);
        await using var listener = await SnapshotNoticeSession.Open(_cluster.ConnectionString);
        // A fresh LISTEN can be handed notices committed just before it; only what the act sends counts.
        await listener.Drain();

        await _full.DispatchAndRun(started.InvokeToken!);
        (await SystemRow(instance.Id)).State.Should().Be("Done", "the run's own host applied it");

        (await listener.Drain())
            .Where(p => p == started.RowId.ToString())
            .Should()
            .ContainSingle(
                $"the hook's outcome is one committed write, and it sends one notice. See {Adr}"
            );
    }

    [Test]
    public async Task An_outcome_applied_by_the_reconciler_on_a_second_host_notifies_once()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var started = await SystemRow(instance.Id);
        await _worker.DispatchAndRun(started.InvokeToken!);
        await using var listener = await SnapshotNoticeSession.Open(_cluster.ConnectionString);
        // A fresh LISTEN can be handed notices committed just before it; only what the act sends counts.
        await listener.Drain();

        (await _api2.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<InvokeDelivery.Moved>("the worker ran the train and no hook delivered it");

        (await listener.Drain())
            .Where(p => p == started.RowId.ToString())
            .Should()
            .ContainSingle(
                $"the reconciler's outcome is one committed write, and it sends one notice. See {Adr}"
            );
    }

    private async Task<SnapshotDraft> SystemRow(Guid id) =>
        (await _api.Row(id, SystemStepMachine.MachineId))!;
}
