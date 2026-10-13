using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// An effect is pending on its instance's row: sending an effect-bound event marks the row, every other write to it
/// is refused as <c>effect-in-progress</c> or waits while the mark is there, and the effect's receipt, the move to its
/// target and the end of the mark are one write. Past its lease the mark is taken over, by the next send or by the
/// reconciler, and the effect run again under the same key. Pins docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md
/// as well: nothing but the effect runner writes a draft between its effect and its record.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
[Property("adr", "docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md")]
public class EffectPendingTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string OnlyTheRunner =
        "docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md";

    private const string Machine = PendingPayMachine.Id;

    private InstanceHost _host = null!;
    private ControlledCharge _charge = null!;
    private Actor.Client _owner = null!;

    [SetUp]
    public void SetUp()
    {
        _charge = new ControlledCharge();
        _host = InstanceHost.Create(
            provider,
            options: o => o.MaxDraftLifetime = TimeSpan.FromDays(1),
            services: s => s.AddSingleton<IOrderCharge>(_charge)
        );
        _owner = DeskRoles.Client($"pending-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    // A started instance moved into Review, where its effect runs from.
    private async Task<Guid> InReview()
    {
        var id = await Drive.Start(_host, _owner, Machine);
        (await Drive.Event(_host, _owner, Machine, id, nameof(PendingPayTrigger.Go)))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return id;
    }

    private async Task<Models.SnapshotDraft.SnapshotDraft> Row(Guid id) =>
        (await _host.Rows(id)).Single();

    private static void ShouldBeEffectInProgress(AdvanceOutcome outcome, string because) =>
        outcome
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>(because)
            .Which.Reason.Should()
            .Be("effect-in-progress", $"{because} ({Adr}; {OnlyTheRunner})");

    [Test]
    public async Task An_event_out_of_the_effects_state_while_it_is_pending_is_refused_effect_in_progress()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.Hold);
        var send = Drive.Send(_host, _owner, Machine, id);
        await _charge.Entered;

        var cancel = await Drive.Event(
            _host,
            _owner,
            Machine,
            id,
            nameof(PendingPayTrigger.Cancel)
        );
        var edit = await Drive.Event(
            _host,
            _owner,
            Machine,
            id,
            nameof(PendingPayTrigger.Edit),
            new JsonObject { ["note"] = "n" }
        );
        _charge.Release();

        ShouldBeEffectInProgress(cancel, "the row holds a pending effect");
        ShouldBeEffectInProgress(edit, "the row holds a pending effect");
        (await send)
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be(nameof(PendingPayState.Paid));
        var row = await Row(id);
        row.State.Should().Be(nameof(PendingPayState.Paid));
        JsonNode.Parse(row.Context)!["receipt"]!.GetValue<string>().Should().Be("receipt-1");
        _charge.Calls.Should().Be(1);
    }

    [Test]
    public async Task A_reset_and_the_generic_save_are_refused_while_an_effect_is_pending()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.Hold);
        var send = Drive.Send(_host, _owner, Machine, id);
        await _charge.Entered;

        var reset = await Drive.Event(
            _host,
            _owner,
            Machine,
            id,
            nameof(PendingPayTrigger.Restart)
        );
        AutosaveResult save;
        using (var scope = _host.Scope())
            save = await _host
                .Service(scope, Machine)
                .Autosave(
                    _owner.UserKey,
                    id,
                    new JsonObject
                    {
                        ["machine"] = Machine,
                        ["version"] = 1,
                        ["state"] = nameof(PendingPayState.Draft),
                        ["context"] = new JsonObject { ["note"] = "", ["receipt"] = "" },
                    }.ToJsonString()
                );
        var stateWhilePending = (await Row(id)).State;
        _charge.Release();

        ShouldBeEffectInProgress(reset, "a reset is a write like any other");
        save.Should().BeOfType<AutosaveResult.Rejected>("the generic save writes nothing here");
        stateWhilePending.Should().Be(nameof(PendingPayState.Review));
        (await send).Should().BeOfType<AdvanceOutcome.Advanced>();
        (await Row(id)).State.Should().Be(nameof(PendingPayState.Paid));
    }

    [Test]
    public async Task Discard_and_the_draft_sweep_leave_a_row_with_a_pending_effect()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.Hold);
        var send = Drive.Send(_host, _owner, Machine, id);
        await _charge.Entered;

        var discard = await Drive.Discard(_host, _owner, Machine, id);
        await _host.Execute(
            $"UPDATE {_host.DraftTable} SET created_at = @p0 WHERE row_id = @p1",
            provider == StoreProvider.Postgres
                ? DateTimeOffset.UtcNow.AddDays(-30)
                : DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz"),
            await _host.RowId(id)
        );
        await Drive.SweepDrafts(_host);
        var rowsWhilePending = (await _host.Rows(id)).Count;
        _charge.Release();

        discard
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(DraftRefusalCodes.DiscardNotAllowed, $"an effect is pending on the row ({Adr})");
        rowsWhilePending.Should().Be(1, "the sweep passes over a row with a pending effect");
        (await send).Should().BeOfType<AdvanceOutcome.Advanced>();
        (await Row(id)).State.Should().Be(nameof(PendingPayState.Paid));
    }

    [Test]
    public async Task A_due_timer_waits_for_a_pending_effect_and_fires_after_it_fails()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.HoldThenFail);
        var send = Drive.Send(_host, _owner, Machine, id);
        await _charge.Entered;
        (await RawSql.SetDueAt(_host, id, (await RawSql.Now(_host)).AddMinutes(-1))).Should().Be(1);

        await Drive.SweepTimers(_host);
        var stateWhilePending = (await Row(id)).State;
        _charge.Release();
        await FluentActions.Awaiting(() => send).Should().ThrowAsync<InvalidOperationException>();
        await Drive.SweepTimers(_host);

        stateWhilePending.Should().Be(nameof(PendingPayState.Review), "the timer waits");
        (await Row(id))
            .State.Should()
            .Be(nameof(PendingPayState.Expired), $"the effect failed, so the timer fires ({Adr})");
    }

    [Test]
    public async Task The_receipt_the_move_and_the_end_of_the_mark_are_one_write()
    {
        var id = await InReview();

        (await Drive.Send(_host, _owner, Machine, id)).Should().BeOfType<AdvanceOutcome.Advanced>();

        var row = await Row(id);
        row.State.Should().Be(nameof(PendingPayState.Paid));
        JsonNode.Parse(row.Context)!["receipt"]!.GetValue<string>().Should().Be("receipt-1");
        (await RawSql.PendingEffect(_host, id))
            .Token.Should()
            .BeNull("the mark ended with the move");
        using var scope = _host.Scope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .EffectClaims.AsNoTracking()
                .CountAsync(c => c.EffectKey.Contains(id.ToString()))
        )
            .Should()
            .Be(0, $"nothing about the effect is held outside its row ({Adr})");
    }

    [Test]
    public async Task A_failed_effect_leaves_the_row_as_it_was_and_takes_events_again()
    {
        var id = await InReview();
        var before = await Row(id);
        _charge.Script(ChargeRun.Fail);

        await FluentActions
            .Awaiting(() => Drive.Send(_host, _owner, Machine, id))
            .Should()
            .ThrowAsync<InvalidOperationException>();

        var after = await Row(id);
        after.State.Should().Be(before.State);
        after.Context.Should().Be(before.Context);
        (await RawSql.PendingEffect(_host, id)).Token.Should().BeNull();
        (await Drive.Event(_host, _owner, Machine, id, nameof(PendingPayTrigger.Cancel)))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }

    [Test]
    public async Task A_mark_past_its_lease_is_finished_by_the_next_send_under_the_same_key()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.TimeOut);
        await FluentActions
            .Awaiting(() => Drive.Send(_host, _owner, Machine, id))
            .Should()
            .ThrowAsync<OperationCanceledException>();
        (await RawSql.PendingEffect(_host, id))
            .Token.Should()
            .NotBeNull("an effect whose outcome is unknown stays pending");
        (await RawSql.SetEffectLease(_host, id, (await RawSql.Now(_host)).AddHours(-1)))
            .Should()
            .Be(1);

        (await Drive.Send(_host, _owner, Machine, id)).Should().BeOfType<AdvanceOutcome.Advanced>();

        (await Row(id)).State.Should().Be(nameof(PendingPayState.Paid));
        _charge.Calls.Should().Be(2);
        _charge
            .Keys.Distinct()
            .Should()
            .ContainSingle($"a run again keeps the effect's key ({Adr})");
        (await RawSql.PendingEffect(_host, id)).Token.Should().BeNull();
    }

    [Test]
    public async Task A_mark_past_its_lease_is_finished_by_the_reconciler_under_the_same_key()
    {
        var id = await InReview();
        _charge.Script(ChargeRun.TimeOut);
        await FluentActions
            .Awaiting(() => Drive.Send(_host, _owner, Machine, id, "s-first"))
            .Should()
            .ThrowAsync<OperationCanceledException>();
        (await RawSql.SetEffectLease(_host, id, (await RawSql.Now(_host)).AddHours(-1)))
            .Should()
            .Be(1);

        await Drive.SweepTimers(_host);

        (await Row(id)).State.Should().Be(nameof(PendingPayState.Paid), $"({Adr})");
        _charge.KeysOf(id).Distinct().Should().ContainSingle();
        _charge.KeysOf(id).Should().HaveCount(2);
        (await RawSql.PendingEffect(_host, id)).Token.Should().BeNull();
        (await Drive.Send(_host, _owner, Machine, id, "s-first"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                "the client's retry replays what the reconciler recorded"
            )
            .Which.Snapshot.State.Should()
            .Be(nameof(PendingPayState.Paid));
        _charge.KeysOf(id).Should().HaveCount(2, "the retry replays without running the effect");
    }

    [Test]
    public async Task A_send_after_the_effect_was_recorded_and_the_draft_reset_runs_under_a_new_key()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(_owner.UserKey, id, OrderMachine.ReviewSnapshot(1, 2));

        (await Drive.Send(_host, _owner, "order", id)).Should().BeOfType<AdvanceOutcome.Advanced>();
        (await Drive.Event(_host, _owner, "order", id, nameof(OrderTrigger.Reset)))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        await _host.SeedDraft(_owner.UserKey, id, OrderMachine.ReviewSnapshot(3, 4));
        (await Drive.Send(_host, _owner, "order", id)).Should().BeOfType<AdvanceOutcome.Advanced>();

        _charge
            .KeysOf(id)
            .Should()
            .HaveCount(2)
            .And.OnlyHaveUniqueItems(
                $"each send is an intent of its own, with a key of its own ({Adr})"
            );
    }

    [Test]
    public async Task A_host_without_the_effect_leaves_an_abandoned_mark_and_meters_it()
    {
        using var host = InstanceHost.Create(provider, services: s => s.RemoveAll<IOrderCharge>());
        var owner = DeskRoles.Client($"pending-{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        await host.Seed(owner.UserKey, id, PendingPayMachine.At(PendingPayState.Review));
        (
            await RawSql.MarkEffect(
                host,
                id,
                nameof(PendingPayTrigger.Pay),
                "s-1",
                (await RawSql.Now(host)).AddHours(-1)
            )
        )
            .Should()
            .Be(1);
        var abandoned = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == StateMachineInstruments.MeterName
                && instrument.Name == "trax.machine.effect.abandoned"
            )
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>(
            (_, value, _, _) => Interlocked.Add(ref abandoned, value)
        );
        listener.Start();

        await host
            .Services.GetRequiredService<InvokeOutcomeReconciler>()
            .FireDueTimersOnce(CancellationToken.None);

        var row = (await host.Rows(id)).Single();
        row.State.Should().Be(nameof(PendingPayState.Review));
        (await RawSql.PendingEffect(host, id))
            .Token.Should()
            .NotBeNull("only a host with the effect finishes it");
        Interlocked.Read(ref abandoned).Should().BeGreaterThan(0, $"({Adr})");
    }

    [Test]
    public async Task A_completed_ledger_claim_from_before_the_upgrade_is_recorded_by_the_next_send()
    {
        var id = await InReview();
        string fingerprint;
        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, Machine);
            var loaded = (await service.Load(_owner.UserKey, id))
                .Should()
                .BeOfType<LoadResult.Loaded>()
                .Subject;
            fingerprint = SnapshotFingerprint.Of(service.Serialize(loaded.Snapshot));
        }
        var key = EffectClaimKey.ForUser($"{Machine}:pay", _owner.UserKey, id);
        using (var scope = _host.Scope())
        {
            var claims = scope.ServiceProvider.GetRequiredService<IEffectClaimStore>();
            var won = (await claims.TryClaim(key, TimeSpan.FromMinutes(5), fingerprint))
                .Should()
                .BeOfType<ClaimResult.Won>()
                .Subject;
            await claims.Complete(key, won.OwnerToken, "receipt-from-before");
        }

        (await Drive.Send(_host, _owner, Machine, id)).Should().BeOfType<AdvanceOutcome.Advanced>();

        var row = await Row(id);
        row.State.Should().Be(nameof(PendingPayState.Paid));
        JsonNode.Parse(row.Context)!["receipt"]!
            .GetValue<string>()
            .Should()
            .Be("receipt-from-before");
        _charge.Calls.Should().Be(0, "the effect already ran");
        using var check = _host.Scope();
        (
            await check
                .ServiceProvider.GetRequiredService<IDataContext>()
                .EffectClaims.AsNoTracking()
                .CountAsync(c => c.EffectKey == key)
        )
            .Should()
            .Be(0, $"the receipt is on the row now, and nothing else holds it ({Adr})");
    }
}
