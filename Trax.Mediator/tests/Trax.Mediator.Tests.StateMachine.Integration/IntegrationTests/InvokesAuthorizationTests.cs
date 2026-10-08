using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// A user-owned machine's train is authorized against the user entering the state, at entry, and one user holds at
/// most a machine's cap of live runs; a system-owned machine's train is authorized inside the trusted execution
/// scope and is not capped. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokesAuthorizationTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        RecordingAuthorization.Calls.Clear();
        RecordingAuthorization.Refused.Clear();
        _host = InvokeHost.Create(provider);
    }

    [TearDown]
    public void TearDown()
    {
        RecordingAuthorization.Refused.Clear();
        _host.Dispose();
    }

    private static string NewUser() => "user-" + Guid.NewGuid().ToString("N");

    [Test]
    public async Task The_eleventh_live_run_for_one_user_is_refused_with_its_reason()
    {
        var user = NewUser();
        for (var i = 0; i < 10; i++)
            (await _host.EnterRunning(user, Guid.NewGuid(), GoodMachine.MachineId))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        var eleventh = Guid.NewGuid();
        (await _host.EnterRunning(user, eleventh, GoodMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("invoke-limit-reached", $"a user holds at most 10 live runs by default. See {Adr}");
        (await _host.Row(eleventh, GoodMachine.MachineId))!.State.Should().Be("Idle");
        (await _host.Runs(eleventh)).Should().BeEmpty();

        // Another user is not held to the first one's runs.
        (await _host.EnterRunning(NewUser(), Guid.NewGuid(), GoodMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }

    [Test]
    public async Task A_machine_sets_its_own_cap_and_a_left_state_frees_its_slot()
    {
        var user = NewUser();
        var first = Guid.NewGuid();
        await _host.EnterRunning(user, first, CappedMachine.MachineId);
        await _host.EnterRunning(user, Guid.NewGuid(), CappedMachine.MachineId);

        (await _host.EnterRunning(user, Guid.NewGuid(), CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("invoke-limit-reached");

        using (var scope = _host.Scope())
            await _host.Service(scope, CappedMachine.MachineId).Advance(user, first, "Stop");

        (await _host.EnterRunning(user, Guid.NewGuid(), CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>("leaving a state ends its run's hold on the cap");
    }

    [Test]
    public async Task System_owners_are_not_capped()
    {
        var instances = new List<MachineInstance>();
        for (var i = 0; i < 12; i++)
            instances.Add(
                await _host.Start<PartitionMachine>(
                    MachineKey.Of("uncapped", Guid.NewGuid().ToString())
                )
            );

        foreach (var instance in instances)
            (await _host.Runs(instance.Id))
                .Should()
                .ContainSingle(
                    $"the dispatcher's MaxActiveJobs bounds system runs, not a cap. See {Adr}"
                );
    }

    [Test]
    public async Task A_user_owned_machines_train_runs_as_the_entering_user()
    {
        var user = NewUser();

        await _host.EnterRunning(user, Guid.NewGuid(), GoodMachine.MachineId);

        RecordingAuthorization
            .Calls.Should()
            .ContainSingle(c => c.Train == typeof(IGoodTrain).FullName)
            .Which.Should()
            .Be(
                new AuthorizationCall(typeof(IGoodTrain).FullName!, user, Trusted: false),
                $"the train is authorized against the user entering the state, at entry. See {Adr}"
            );

        // A user the train refuses cannot enter the state, and nothing is written.
        var refused = NewUser();
        RecordingAuthorization.Refused.Add(refused);
        var id = Guid.NewGuid();
        (await _host.EnterRunning(refused, id, GoodMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("invoke-forbidden");
        (await _host.Row(id, GoodMachine.MachineId))!.State.Should().Be("Idle");
        (await _host.Runs(id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_system_owned_machines_train_runs_under_the_trusted_scope()
    {
        await _host.Start<PartitionMachine>(MachineKey.Of("trusted", Guid.NewGuid().ToString()));

        RecordingAuthorization
            .Calls.Should()
            .ContainSingle(c => c.Train == typeof(IGoodTrain).FullName)
            .Which.Trusted.Should()
            .BeTrue(
                $"a system-owned machine's train is authorized as a scheduled manifest run is. See {Adr}"
            );
    }

    [Test]
    public async Task A_run_launched_from_an_outcome_of_a_user_owned_instance_is_refused()
    {
        // The startup check refuses a user-owned machine whose outcome enters an invoking state, so this launch
        // never happens; if one does, the launcher refuses it rather than authorize it in the trusted scope.
        TestPrincipal.Become(null);
        var userRun = Guid.NewGuid().ToString("N");

        var launch = () =>
            LaunchFromOutcome(userRun, UserChainMachine.MachineId, SnapshotOwnerKind.User);

        var refused = await launch.Should().ThrowAsync<UnauthorizedAccessException>();
        refused
            .Which.Message.Should()
            .Contain("user-owned")
            .And.Contain(UserChainMachine.MachineId);
        RecordingAuthorization
            .Calls.Should()
            .BeEmpty($"nothing is authorized, in the trusted scope or out of it. See {Adr}");
        (await Entries(userRun)).Should().Be(0);

        // A system-owned instance's chained run is authorized in the trusted scope and queued.
        var systemRun = Guid.NewGuid().ToString("N");
        await LaunchFromOutcome(systemRun, SystemChainMachine.MachineId, SnapshotOwnerKind.System);
        RecordingAuthorization.Calls.Should().ContainSingle().Which.Trusted.Should().BeTrue();
        (await Entries(systemRun)).Should().Be(1);
    }

    private async Task LaunchFromOutcome(string externalId, string machine, SnapshotOwnerKind owner)
    {
        using var scope = _host.Scope();
        await scope
            .ServiceProvider.GetRequiredService<IInvokedTrainLauncher>()
            .Launch(
                new InvokedTrainLaunch(
                    typeof(IGoodTrain),
                    new GoodInput("repo"),
                    externalId,
                    new InvokedBy(machine, Guid.NewGuid(), owner)
                )
                {
                    FromOutcome = true,
                },
                scope.ServiceProvider.GetRequiredService<IDataContext>()
            );
    }

    private async Task<int> Entries(string externalId)
    {
        using var scope = _host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .CountAsync(w => w.ExternalId == externalId);
    }
}
