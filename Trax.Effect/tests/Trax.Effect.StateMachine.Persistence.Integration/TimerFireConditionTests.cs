using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A timer's fire is a conditional write on the row's state and revision as the sweep read them, whatever lock the
/// sweep took first: a fire over a row written since matches nothing. These tests call the store's fire directly, so
/// they hold without the sweep's lock.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class TimerFireConditionTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "fire-condition-user";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static readonly Snapshot Expired = new()
    {
        Machine = ReminderMachine.Id,
        Version = 1,
        State = nameof(ReminderState.Expired),
        Context = new JsonObject(),
    };

    private async Task<Guid> InReview()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, ReminderMachine.Json("Idle"));
        using var scope = _host.Scope();
        (
            await _host
                .Service(scope, ReminderMachine.Id)
                .Advance(new Actor.Client(User, "Editor"), id, "Open")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return id;
    }

    // The row as the sweep reads it once it is due, and a fire written through the store on a context of its own.
    private async Task<DueRow> ReadDue(Guid id)
    {
        (await RawSql.SetDueAt(_host, id, (await RawSql.Now(_host)).AddMinutes(-1))).Should().Be(1);
        return await WithStore(async store =>
            (
                await store.ReadDue(
                    [ReminderMachine.Id],
                    Array.Empty<long>(),
                    1000,
                    CancellationToken.None
                )
            ).Single(r => r.Id == id)
        );
    }

    private async Task<T> WithStore<T>(Func<EfSnapshotStore, Task<T>> use)
    {
        using var scope = _host.Scope();
        var services = scope.ServiceProvider;
        await using var context = await services
            .GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await use(
            services
                .GetRequiredService<InvokeOutbox>()
                .StoreOver(context, services.GetService<ISqlDialect>())
        );
    }

    private Task<bool> Fire(DueRow due) =>
        WithStore(store => store.FireTimer(due, Expired, null, CancellationToken.None));

    [Test]
    public async Task A_fire_over_a_row_written_since_it_was_read_matches_nothing()
    {
        var id = await InReview();
        var read = await ReadDue(id);

        // A client's Extend writes the row again in the same state, at a new revision.
        using (var scope = _host.Scope())
            (await _host.Service(scope, ReminderMachine.Id).Advance(User, id, "Extend"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        var extended = (await RawSql.Timer(_host, id))!;
        extended.State.Should().Be(nameof(ReminderState.Review));

        (await Fire(read))
            .Should()
            .BeFalse($"a fire writes only over the revision it read. See {Adr}");
        var row = (await RawSql.Timer(_host, id))!;
        row.State.Should().Be(nameof(ReminderState.Review));
        row.Revision.Should().Be(extended.Revision);
        row.DueAt.Should().Be(extended.DueAt, "the client's write, and its timer, stand");
    }

    [Test]
    public async Task A_fire_over_a_row_in_another_state_matches_nothing()
    {
        var id = await InReview();
        var read = await ReadDue(id);

        (await Fire(read with { State = nameof(ReminderState.Idle) }))
            .Should()
            .BeFalse($"a fire writes only over the state it read. See {Adr}");
        (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(ReminderState.Review));

        (await Fire(read)).Should().BeTrue("the row as it was read is fired");
        (await RawSql.Timer(_host, id))!.State.Should().Be(nameof(ReminderState.Expired));
    }
}
