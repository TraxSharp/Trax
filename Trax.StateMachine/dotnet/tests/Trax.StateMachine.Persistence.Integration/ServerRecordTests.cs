using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A row records whether the server put it in its state (an effect, an invoked train's outcome, a gated event), and
/// that record, not the kind of state, decides what discard refuses, what the draft sweep keeps and what the draft cap
/// counts. A client event clears it; a timer carries it over.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class ServerRecordTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Machine = RecordDeskMachine.Id;

    private InstanceHost? _host;
    private Actor.Client? _owner;
    private Actor.Client? _operator;
    private string? _user;

    [SetUp]
    public void SetUp()
    {
        _host = InstanceHost.Create(
            provider,
            options: o =>
            {
                o.MaxDraftLifetime = TimeSpan.FromDays(1);
                o.MaxDraftsPerUser = 2;
            }
        );
        var user = $"record-{Guid.NewGuid():N}";
        _user = user;
        _owner = DeskRoles.Client(user);
        // The same user, holding the role the gated event requires.
        _operator = DeskRoles.Client(user, DeskRoles.Operator);
    }

    [TearDown]
    public void TearDown() => _host?.Dispose();

    private Task<AdvanceOutcome> Fire(Actor.Client actor, Guid id, RecordDeskTrigger trigger) =>
        Drive.Event(_host.NotNull(), actor, Machine, id, trigger.ToString());

    // A started instance an operator rejected into Revising.
    private async Task<Guid> RevisingByOperator()
    {
        var id = await Drive
            .Start(_host.NotNull(), _owner.NotNull(), Machine)
            .ConfigureAwait(false);
        (await Fire(_owner.NotNull(), id, RecordDeskTrigger.Submit).ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await Fire(_operator.NotNull(), id, RecordDeskTrigger.Reject).ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return id;
    }

    // A started instance its owner moved into Revising.
    private async Task<Guid> RevisingByOwner()
    {
        var id = await Drive
            .Start(_host.NotNull(), _owner.NotNull(), Machine)
            .ConfigureAwait(false);
        (await Fire(_owner.NotNull(), id, RecordDeskTrigger.Revise).ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return id;
    }

    private async Task MakeOld(Guid id) =>
        await _host
            .NotNull()
            .Execute(
                $"UPDATE {_host.NotNull().DraftTable} SET created_at = @p0, updated_at = @p0 WHERE row_id = @p1",
                provider == StoreProvider.Postgres
                    ? DateTimeOffset.UtcNow.AddDays(-30)
                    : DateTimeOffset
                        .UtcNow.AddDays(-30)
                        .ToString(
                            "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz",
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                await _host.NotNull().RowId(id).ConfigureAwait(false)
            )
            .ConfigureAwait(false);

    private async Task<string> State(Guid id) =>
        (await _host.NotNull().Rows(id).ConfigureAwait(false)).Single().State;

    [Test]
    public async Task A_row_a_gated_event_moved_is_kept_and_refuses_discard()
    {
        var id = await RevisingByOperator();

        var discard = await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, id);
        await MakeOld(id);
        await Drive.SweepDrafts(_host.NotNull());

        discard
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(RefusalCode.DiscardNotAllowed, $"an operator put it there ({Adr})");
        (await _host.NotNull().Rows(id))
            .Should()
            .ContainSingle("the sweep keeps the record of what the server did");
    }

    [Test]
    public async Task A_row_a_client_event_moved_into_the_same_state_is_a_draft()
    {
        var id = await RevisingByOwner();

        var discard = await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, id);

        discard
            .Should()
            .BeOfType<DiscardOutcome.Discarded>($"its owner put it there, not the server ({Adr})");
        (await _host.NotNull().Rows(id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_client_event_out_of_a_kept_row_makes_it_a_draft_again()
    {
        var id = await RevisingByOperator();
        (await Fire(_owner.NotNull(), id, RecordDeskTrigger.Note))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        var discard = await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, id);

        discard
            .Should()
            .BeOfType<DiscardOutcome.Discarded>(
                $"the owner's own event moved it last, so it is a draft again ({Adr})"
            );
    }

    // Host code writing the row back to Draft through the store, as a reset would: it states no record of its own.
    private static Snapshot DraftSnapshot() =>
        new()
        {
            Machine = Machine,
            Version = 1,
            State = nameof(RecordDeskState.Draft),
            Context = new JsonObject(),
        };

    [Test]
    public async Task A_host_upsert_out_of_a_kept_row_leaves_it_judged_by_its_state()
    {
        var id = await RevisingByOperator();
        var upsertScope = _host.NotNull().Services.CreateAsyncScope();
        await using (upsertScope.ConfigureAwait(false))
            (
                await upsertScope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(_user.NotNull(), id, DraftSnapshot())
            )
                .Should()
                .BeTrue();

        var discard = await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, id);

        discard
            .Should()
            .BeOfType<DiscardOutcome.Discarded>(
                $"host code moved it to Draft and stated no record, so its state decides ({Adr})"
            );
    }

    [Test]
    public async Task A_host_update_out_of_a_kept_row_leaves_it_judged_by_its_state()
    {
        var id = await RevisingByOperator();
        var updateScope = _host.NotNull().Services.CreateAsyncScope();
        await using (updateScope.ConfigureAwait(false))
        {
            var store = updateScope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var stored = await store.Get(_user.NotNull(), Machine, id);
            (await store.Update(_user.NotNull(), id, DraftSnapshot(), stored.NotNull().Token))
                .Should()
                .BeTrue();
        }

        var discard = await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, id);

        discard
            .Should()
            .BeOfType<DiscardOutcome.Discarded>(
                $"host code moved it to Draft and stated no record, so its state decides ({Adr})"
            );
    }

    [Test]
    public async Task A_timer_carries_the_record_over()
    {
        var byOperator = await RevisingByOperator();
        var byOwner = await RevisingByOwner();
        var now = await RawSql.Now(_host.NotNull());
        (await RawSql.SetDueAt(_host.NotNull(), byOperator, now.AddMinutes(-1))).Should().Be(1);
        (await RawSql.SetDueAt(_host.NotNull(), byOwner, now.AddMinutes(-1))).Should().Be(1);

        await Drive.SweepTimers(_host.NotNull());

        (await State(byOperator)).Should().Be(nameof(RecordDeskState.Archived));
        (await State(byOwner)).Should().Be(nameof(RecordDeskState.Archived));
        (await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, byOperator))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>(
                $"the timer moved what the operator decided ({Adr})"
            );
        (await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, byOwner))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>("the timer moved what the owner decided");
    }

    [Test]
    public async Task Kept_rows_do_not_count_toward_the_draft_cap()
    {
        await RevisingByOperator();
        await RevisingByOperator();

        var third = await Drive.TryStart(_host.NotNull(), _owner.NotNull(), Machine);

        third
            .Should()
            .BeOfType<StartOutcome.Started>(
                $"rows the server put in their state are records, not drafts ({Adr})"
            );
    }

    [Test]
    public async Task The_upgrade_marks_rows_in_kept_states_as_kept()
    {
        var kept = await RevisingByOwner();
        var draft = await Drive.Start(_host.NotNull(), _owner.NotNull(), Machine);
        (await RawSql.SetServerRecord(_host.NotNull(), kept, value: null)).Should().Be(1);
        (await RawSql.SetServerRecord(_host.NotNull(), draft, value: null)).Should().Be(1);

        (await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, kept))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>(
                $"a row from before the record is judged by its state, as before ({Adr})"
            );
        (await Drive.Discard(_host.NotNull(), _owner.NotNull(), Machine, draft))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>();
    }
}
