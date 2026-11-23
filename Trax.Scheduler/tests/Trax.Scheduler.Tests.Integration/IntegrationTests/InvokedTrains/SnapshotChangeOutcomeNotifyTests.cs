using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.StateMachine.Persistence;

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

    private InvokeCluster? _cluster;

    // Registers the machines: their hook and their reconciler. No scheduler.
    private ClusterHost? _api;

    // A second API host over the same database.
    private ClusterHost? _api2;

    // Dispatches and runs trains, and registers no machines, so nothing it runs is delivered by a hook.
    private ClusterHost? _worker;

    // Dispatches and runs trains and registers the machines, so its hook delivers each outcome at once.
    private ClusterHost? _full;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(ClusterStore.Postgres);
        _api = _cluster.NotNull().Host(machines: true, scheduler: false);
        _api2 = _cluster.NotNull().Host(machines: true, scheduler: false);
        _worker = _cluster.NotNull().Host(machines: false, scheduler: true);
        _full = _cluster.NotNull().Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        if (_api is not null)
            await _api.DisposeAsync();
        if (_api2 is not null)
            await _api2.DisposeAsync();
        if (_worker is not null)
            await _worker.DisposeAsync();
        if (_full is not null)
            await _full.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.NotNull().Reset();
    }

    [TearDown]
    public void ReleaseGate() => InvokedStepGate.Release();

    [Test]
    public async Task An_outcome_applied_by_the_hook_notifies_once()
    {
        var instance = await _api.NotNull().Start(StepMachine.Context(InvokedStepModes.Ok));
        var started = await SystemRow(instance.Id);
        await using var listener = await SnapshotNoticeSession.Open(
            _cluster.NotNull().ConnectionString
        );
        // A fresh LISTEN can be handed notices committed just before it; only what the act sends counts.
        await listener.Drain();

        await _full.NotNull().DispatchAndRun(started.InvokeToken.NotNull());
        (await SystemRow(instance.Id)).State.Should().Be("Done", "the run's own host applied it");

        (await listener.Drain())
            .Where(p =>
                string.Equals(
                    p,
                    started.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal
                )
            )
            .Should()
            .ContainSingle(
                $"the hook's outcome is one committed write, and it sends one notice. See {Adr}"
            );
    }

    [Test]
    public async Task An_outcome_applied_by_the_reconciler_on_a_second_host_notifies_once()
    {
        var instance = await _api.NotNull().Start(StepMachine.Context(InvokedStepModes.Ok));
        var started = await SystemRow(instance.Id);
        await _worker.NotNull().DispatchAndRun(started.InvokeToken.NotNull());
        await using var listener = await SnapshotNoticeSession.Open(
            _cluster.NotNull().ConnectionString
        );
        // A fresh LISTEN can be handed notices committed just before it; only what the act sends counts.
        await listener.Drain();

        (await _api2.NotNull().Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<InvokeDelivery.Moved>("the worker ran the train and no hook delivered it");

        (await listener.Drain())
            .Where(p =>
                string.Equals(
                    p,
                    started.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal
                )
            )
            .Should()
            .ContainSingle(
                $"the reconciler's outcome is one committed write, and it sends one notice. See {Adr}"
            );
    }

    private async Task<SnapshotDraft> SystemRow(Guid id) =>
        (await _api.NotNull().Row(id, SystemStepMachine.MachineId).ConfigureAwait(false)).NotNull();
}
