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
    public async Task A_machines_limit_counts_the_users_live_runs_in_every_machine()
    {
        var user = NewUser();
        await _host.EnterRunning(user, Guid.NewGuid(), GoodMachine.MachineId);
        var first = Guid.NewGuid();
        (await _host.EnterRunning(user, first, CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        (await _host.EnterRunning(user, Guid.NewGuid(), CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(
                "invoke-limit-reached",
                "the run in the other machine counts toward this machine's limit of 2, so N machines "
                    + $"do not give N limits. See {Adr}"
            );

        // A queued run is cancelled outright when its state is left, so it stops counting at once.
        using (var scope = _host.Scope())
            await _host.Service(scope, CappedMachine.MachineId).Advance(user, first, "Stop");

        (await _host.EnterRunning(user, Guid.NewGuid(), CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>("a cancelled queued run is no longer live");
    }

    [Test]
    public async Task Leaving_and_entering_again_cannot_pile_up_runs_that_are_still_executing()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        (await _host.EnterRunning(user, id, CappedMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        // Each lap: the run is dispatched, the user leaves the state (which only flags the run, which goes on to its
        // next junction), and enters it again.
        var laps = 0;
        AdvanceOutcome entered;
        do
        {
            await _host.Dispatch(id);
            using (var scope = _host.Scope())
                (await _host.Service(scope, CappedMachine.MachineId).Advance(user, id, "Stop"))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
            using (var scope = _host.Scope())
                entered = await _host
                    .Service(scope, CappedMachine.MachineId)
                    .Advance(user, id, "Go");
            laps++;
        } while (entered is AdvanceOutcome.Advanced && laps < 10);

        entered
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>(
                $"a run flagged for cancel is live until it ends, and counts. See {Adr}"
            )
            .Which.Reason.Should()
            .Be("invoke-limit-reached");
        laps.Should().Be(2, "the machine's limit is 2");

        // Once the flagged runs end, their slots are free again.
        await _host.End(id, TrainState.Cancelled);
        using (var scope = _host.Scope())
            (await _host.Service(scope, CappedMachine.MachineId).Advance(user, id, "Go"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
    }

    [Test]
    public async Task Concurrent_entries_by_one_user_cannot_pass_the_limit_together()
    {
        var user = NewUser();
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        TestPrincipal.Become(user);
        foreach (var id in ids)
            using (var scope = _host.Scope())
                await _host
                    .Service(scope, CappedMachine.MachineId)
                    .Autosave(
                        user,
                        id,
                        StageMachine<IGoodTrain, GoodInput, JobOutput>.Json(
                            CappedMachine.MachineId,
                            "Idle"
                        )
                    );

        var outcomes = await Task.WhenAll(
            ids.Select(id =>
                Task.Run(async () =>
                {
                    using var scope = _host.Scope();
                    return await _host
                        .Service(scope, CappedMachine.MachineId)
                        .Advance(user, id, "Go");
                })
            )
        );

        outcomes
            .OfType<AdvanceOutcome.Advanced>()
            .Should()
            .HaveCount(2, $"the count is taken under a lock per user. See {Adr}");
        outcomes
            .OfType<AdvanceOutcome.Rejected>()
            .Should()
            .HaveCount(4)
            .And.OnlyContain(r => r.Reason == "invoke-limit-reached");
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
    public async Task A_user_owned_machines_train_is_authorized_against_the_entering_user_at_entry()
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

    [Test]
    public async Task A_launch_whose_input_is_not_the_trains_input_type_is_refused_and_queues_nothing()
    {
        // A machine's input mapping is typed by its declaration, so this cannot come from one; if a launch carries
        // some other object, the launcher refuses it rather than store an input the train cannot read.
        var externalId = Guid.NewGuid().ToString("N");

        var launch = async () =>
        {
            using var scope = _host.Scope();
            await scope
                .ServiceProvider.GetRequiredService<IInvokedTrainLauncher>()
                .Launch(
                    new InvokedTrainLaunch(
                        typeof(IGoodTrain),
                        "not a GoodInput",
                        externalId,
                        new InvokedBy(
                            SystemChainMachine.MachineId,
                            Guid.NewGuid(),
                            SnapshotOwnerKind.System
                        ),
                        "Embedding"
                    ),
                    scope.ServiceProvider.GetRequiredService<IDataContext>()
                );
        };

        var refused = await launch.Should().ThrowAsync<InvalidOperationException>();
        refused
            .Which.Message.Should()
            .Contain("The input built for")
            .And.Contain(nameof(GoodInput))
            .And.Contain("String");
        (await Entries(externalId)).Should().Be(0);
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
                    new InvokedBy(machine, Guid.NewGuid(), owner),
                    "Embedding"
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
