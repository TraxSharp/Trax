using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A timed state is the server's to leave: the generic save is refused outright for a machine with a timer, even
/// with the generic mutations opted in, and the draft service's autosave can neither leave a timed state nor
/// rewrite it, so a client can never move a deadline or skip it.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class GenericSaveTimerTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "save-user";

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
    public async Task The_generic_save_is_refused_for_a_machine_with_a_timer()
    {
        var id = Guid.NewGuid();

        SaveSnapshotOutput output;
        using (var scope = _host.Scope())
            output = await new SaveSnapshotJunction(
                scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>(),
                new FakePrincipal(User)
            ).Run(
                new SaveSnapshotInput
                {
                    Machine = PlainTimerMachine.Id,
                    Id = id,
                    Snapshot = PlainTimerMachine.Json("Idle"),
                }
            );

        output.Snapshot.Should().BeNull();
        output
            .Problem.Should()
            .NotBeNull()
            .And.BeEquivalentTo(
                new { Code = RefusalCode.SaveNotAllowed },
                $"a machine with a timer is reached through its declared events only. See {Adr}"
            );
        (await _host.Rows(id)).Should().BeEmpty("nothing is written");
    }

    [Test]
    public async Task Autosave_cannot_leave_or_rewrite_a_timed_state()
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
        var before = await RawSql.Timer(_host, id);

        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, ReminderMachine.Id);
            (await service.Autosave(User, id, ReminderMachine.Json("Idle")))
                .Should()
                .BeOfType<AutosaveResult.Rejected>(
                    $"a save cannot leave a timed state; only an event or the timer does. See {Adr}"
                );
            (await service.Autosave(User, id, ReminderMachine.Json("Done")))
                .Should()
                .BeOfType<AutosaveResult.Rejected>("nor skip past it to where an event leads");
        }

        (await RawSql.Timer(_host, id))
            .Should()
            .Be(before, "due_at and the revision are unchanged");
    }
}
