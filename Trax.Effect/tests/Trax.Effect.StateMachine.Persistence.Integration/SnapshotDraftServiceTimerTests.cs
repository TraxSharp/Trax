using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A timer trigger is fired only by the server when it is due. No advance fires it, whoever asks: a client on the
/// typed path, the generic mutation, a host train forwarding a client, or trusted host code through
/// <see cref="IMachineInstances"/>. Each is refused as <c>timer-bound</c> and nothing is written.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class SnapshotDraftServiceTimerTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Because = $"only the server fires a timer, when it is due. See {Adr}";

    private const string User = "timer-user";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() =>
        _host = InstanceHost.Create(
            provider,
            options: o => o.ExposeGenericStateMachineMutations(g => g.Policy = "operators")
        );

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Advance_with_the_timer_trigger_is_refused_timer_bound()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, ReminderMachine.Json("Idle"));
        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, ReminderMachine.Id);
            (await service.Advance(new Actor.Client(User, "Editor"), id, "Open"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        }
        var before = (await _host.Rows(id)).Single();

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, ReminderMachine.Id);

            (await service.Advance(User, id, ReminderMachine.ReviewTimer))
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(RejectionReasons.TimerBound, Because);

            var generic = await new AdvanceSnapshotJunction(
                scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>(),
                new FakePrincipal(User)
            ).Run(
                new AdvanceSnapshotInput
                {
                    Machine = ReminderMachine.Id,
                    Id = id,
                    Trigger = ReminderMachine.ReviewTimer,
                }
            );
            generic.Problem!.Code.Should().Be(RejectionReasons.TimerBound, Because);

            // The typed path, and a host train forwarding its caller, both act as that client.
            (
                await service.Advance(
                    new Actor.Client(User, "Editor"),
                    id,
                    ReminderMachine.ReviewTimer
                )
            )
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(RejectionReasons.TimerBound, Because);
        }

        var after = (await _host.Rows(id)).Single();
        after.State.Should().Be("Review");
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
    }

    [Test]
    public async Task IMachineInstances_Advance_with_the_timer_trigger_is_refused()
    {
        var key = MachineKey.Of("timers", Guid.NewGuid().ToString());
        var instance = await _host.Start<SystemReminderMachine>(key);
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemReminderMachine>(key, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        var before = (await _host.Rows(instance.Id)).Single();

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemReminderMachine>(key, ReminderMachine.ReviewTimer)
            )
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(
                    RejectionReasons.TimerBound,
                    $"trusted host code cannot fire a timer either. See {Adr}"
                );

        var after = (await _host.Rows(instance.Id)).Single();
        after.State.Should().Be("Review");
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
    }
}
