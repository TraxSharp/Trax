using System.Text.Json.Nodes;
using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Starts, events, sends, timers, discards, draft sweeps and a claimant that stops, in random order from a logged
/// seed, against <see cref="PendingPayMachine"/>, with the invariants of central ADR 0048's <i>Effects and server
/// records</i> checked after every step: an effect that ran is recorded on its row or its row still carries its mark;
/// no row holds two outcomes for one effect; the rows a user holds that the server did not put in their state number
/// at most the cap; a row with a pending effect is in the effect's state; and every mark is finished once its lease
/// has passed and the reconciler has swept. A failure names its seed, which replays the same steps.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class MachineInterleavingModelTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Machine = PendingPayMachine.Id;
    private const int Cap = 3;
    private const int Sequences = 12;
    private const int Steps = 30;

    private enum Step
    {
        Start,
        Go,
        Edit,
        Cancel,
        Restart,
        Send,
        SendHeld,
        SendFailing,
        SendTimingOut,
        Release,
        Discard,
        Timer,
        PassLease,
        SweepDrafts,
    }

    [Test]
    public async Task Seeded_interleavings_keep_every_invariant()
    {
        for (var seed = 1; seed <= Sequences; seed++)
            await Run(seed);
    }

    private async Task Run(int seed)
    {
        var random = new Random(seed);
        var charge = new ControlledCharge();
        using var host = InstanceHost.Create(
            provider,
            options: o =>
            {
                o.MaxDraftsPerUser = Cap;
                o.MaxDraftLifetime = TimeSpan.FromDays(1);
            },
            services: s => s.AddSingleton<IOrderCharge>(charge)
        );
        var owner = DeskRoles.Client($"model-{seed}-{Guid.NewGuid():N}");
        var ids = new List<Guid>();
        Task<AdvanceOutcome>? held = null;
        var log = new List<string>();

        for (var n = 0; n < Steps; n++)
        {
            var step = (Step)random.Next(Enum.GetValues<Step>().Length);
            var id = ids.Count == 0 ? (Guid?)null : ids[random.Next(ids.Count)];
            log.Add($"{step} {id}");
            charge.Clear();
            switch (step)
            {
                case Step.Start:
                    if (await Drive.TryStart(host, owner, Machine) is StartOutcome.Started started)
                        ids.Add(started.Id);
                    break;
                case Step.Go or Step.Cancel or Step.Restart when id is { } target:
                    await Drive.Event(host, owner, Machine, target, step.ToString());
                    break;
                case Step.Edit when id is { } target:
                    await Drive.Event(
                        host,
                        owner,
                        Machine,
                        target,
                        nameof(PendingPayTrigger.Edit),
                        new JsonObject { ["note"] = $"n{n}" }
                    );
                    break;
                case Step.Send when id is { } target:
                    await Swallow(Drive.Send(host, owner, Machine, target));
                    break;
                case Step.SendHeld when id is { } target && held is null:
                    charge.Script(ChargeRun.Hold);
                    held = Drive.Send(host, owner, Machine, target);
                    // The send either holds the effect or is refused before it: wait for whichever comes first.
                    await Task.WhenAny(charge.Entered, held);
                    break;
                case Step.SendFailing when id is { } target:
                    charge.Script(ChargeRun.Fail);
                    await Swallow(Drive.Send(host, owner, Machine, target));
                    break;
                case Step.SendTimingOut when id is { } target:
                    charge.Script(ChargeRun.TimeOut);
                    await Swallow(Drive.Send(host, owner, Machine, target));
                    break;
                case Step.Release when held is not null:
                    charge.Release();
                    await Swallow(held);
                    held = null;
                    break;
                case Step.Discard when id is { } target:
                    await Drive.Discard(host, owner, Machine, target);
                    break;
                case Step.Timer when id is { } target:
                    await RawSql.Execute(
                        host,
                        $"UPDATE {RawSql.Table(host)} SET due_at = created_at WHERE due_at IS NOT NULL"
                    );
                    await Drive.SweepTimers(host);
                    break;
                case Step.PassLease:
                    if (held is null)
                    {
                        await PassLeases(host);
                        await Drive.SweepTimers(host);
                    }
                    break;
                case Step.SweepDrafts:
                    await Drive.SweepDrafts(host);
                    break;
            }

            await Check(host, owner, charge, seed, log);
        }

        if (held is not null)
        {
            charge.Release();
            await Swallow(held);
        }
        await PassLeases(host);
        await Drive.SweepTimers(host);
        await Check(host, owner, charge, seed, log);
        var marked = (await host.UserRows(owner.UserKey, Machine)).Select(r => r.Id).ToList();
        foreach (var id in marked)
            (await RawSql.PendingEffect(host, id))
                .Token.Should()
                .BeNull($"seed {seed}: every mark is finished once its lease has passed ({Adr})");
    }

    private static async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            // The scripted charge failed or timed out; the invariants say what that must leave.
        }
    }

    private static Task PassLeases(InstanceHost host) =>
        RawSql.Execute(
            host,
            $"UPDATE {RawSql.Table(host)} SET effect_lease_until = created_at WHERE effect_token IS NOT NULL"
        );

    private async Task Check(
        InstanceHost host,
        Actor.Client owner,
        ControlledCharge charge,
        int seed,
        List<string> log
    )
    {
        var rows = await host.UserRows(owner.UserKey, Machine);
        var because = $"seed {seed}, after: {string.Join(", ", log)} ({Adr})";
        using var scope = new AssertionScope();

        var drafts = 0;
        foreach (var row in rows)
        {
            var (token, _) = await RawSql.PendingEffect(host, row.Id);
            var context = JsonNode.Parse(row.Context)!;
            var receipt = context["receipt"]?.GetValue<string>() ?? "";
            var ran = charge
                .Receipts.Where(r => r.Key.Contains(row.Id.ToString(), StringComparison.Ordinal))
                .ToList();

            if (token is not null)
                row.State.Should()
                    .Be(
                        nameof(PendingPayState.Review),
                        $"a marked row is in the effect's state; {because}"
                    );
            if (token is null && ran.Count > 0)
            {
                row.State.Should()
                    .Be(nameof(PendingPayState.Paid), $"an effect that ran is recorded; {because}");
                ran.Select(r => r.Receipt)
                    .Should()
                    .Contain(receipt, $"the row holds a receipt the effect returned; {because}");
            }
            if (row.State is nameof(PendingPayState.Expired) or nameof(PendingPayState.Cancelled))
                ran.Should().BeEmpty($"no row holds two outcomes for one effect; {because}");
            if (await RawSql.ServerRecord(host, row.Id) != true)
                drafts++;
        }
        drafts
            .Should()
            .BeLessThanOrEqualTo(Cap, $"the cap bounds what a client alone holds; {because}");

        // An effect that returned a receipt for an instance no longer stored was erased.
        // The reconciler may finish another test's abandoned effect with this charge: only this user's count here.
        foreach (
            var (key, _) in charge.Receipts.Where(r =>
                r.Key.Contains($":{owner.UserKey}:", StringComparison.Ordinal)
            )
        )
            rows.Should()
                .Contain(
                    r => key.Contains(r.Id.ToString(), StringComparison.Ordinal),
                    $"an effect that ran keeps its row ({key}); {because}"
                );
    }
}
