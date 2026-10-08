using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Instances the system owns: created only by <see cref="IMachineInstances.Start{TMachine}"/>, keyed by an id
/// derived from the key, and out of reach of every user path (load, save, advance, send), even a user holding
/// the same id. See <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class SystemOwnedInstanceTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static MachineKey NewKey() => MachineKey.Of("source", Guid.NewGuid().ToString());

    private static string UnlockedJson =>
        """{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}""";

    [Test]
    public async Task Start_twice_with_one_key_returns_one_instance_and_queues_nothing_new()
    {
        var key = NewKey();
        var queued = await _host.WorkQueueCount();

        var first = await _host.Start<TurnstileMachine>(key);
        var second = await _host.Start<TurnstileMachine>(key);

        first.Created.Should().BeTrue();
        second
            .Should()
            .Be(first with { Created = false }, $"one key names one instance. See {Adr}");
        first.Id.Should().Be(MachineInstanceId.For("turnstile", key));
        first.State.Should().Be("Locked");

        var rows = await _host.Rows(first.Id);
        rows.Should().ContainSingle();
        rows[0].OwnerKind.Should().Be(SnapshotOwnerKind.System);
        rows[0].UserKey.Should().BeNull();
        rows[0].InvokeToken.Should().BeNull();
        (await _host.WorkQueueCount()).Should().Be(queued, "Start queues nothing of its own");
    }

    [Test]
    public async Task Concurrent_starts_with_one_key_create_one_row()
    {
        var key = NewKey();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => _host.Start<TurnstileMachine>(key)))
        );

        results.Select(r => r.Id).Distinct().Should().ContainSingle();
        results.Count(r => r.Created).Should().Be(1, "exactly one Start creates the row");
        (await _host.Rows(results[0].Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Start_uses_the_given_context_and_refuses_an_invalid_one()
    {
        var created = await _host.Start<OrderMachine>(
            NewKey(),
            new JsonObject { ["items"] = new JsonArray(1, 2), ["receipt"] = null }
        );
        JsonNode.Parse((await _host.Rows(created.Id)).Single().Context)!["items"]!
            .ToJsonString()
            .Should()
            .Be("[1,2]");

        var invalid = () =>
            _host.Start<TurnstileMachine>(NewKey(), new JsonObject { ["stray"] = 1 });
        await invalid
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*Locked carries no context*");
    }

    [Test]
    public async Task A_system_instance_refuses_advance_and_save_from_any_user()
    {
        var instance = await _host.Start<TurnstileMachine>(NewKey());
        var before = (await _host.Rows(instance.Id)).Single();

        using (var scope = _host.Scope())
        {
            var turnstile = _host.Service(scope, "turnstile");

            (
                await turnstile.Advance(
                    "u1",
                    instance.Id,
                    "Coin",
                    new JsonObject { ["coin"] = "quarter" }
                )
            )
                .Should()
                .BeOfType<AdvanceOutcome.NotFound>(
                    $"a user's advance never reaches a system row, and does not learn it exists. See {Adr}"
                );

            // A save under the id writes the user's own draft, never the system row.
            (await turnstile.Autosave("u1", instance.Id, UnlockedJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        }

        var after = await _host.Rows(instance.Id);
        var system = after.Single(x => x.OwnerKind == SnapshotOwnerKind.System);
        system.State.Should().Be("Locked");
        system
            .ConcurrencyToken.Should()
            .Be(before.ConcurrencyToken, "the system row was never written");
        after.Single(x => x.OwnerKind == SnapshotOwnerKind.User).UserKey.Should().Be("u1");
    }

    [Test]
    public async Task A_system_instance_refuses_send_from_any_user()
    {
        var instance = await _host.Start<OrderMachine>(
            NewKey(),
            new JsonObject { ["items"] = new JsonArray(1), ["receipt"] = null }
        );

        // Move the system row to Review, the effect's from-state, the way only server code can.
        using (var scope = _host.Scope())
        {
            var store = _host.Instances(scope);
            var stored = await store.Get(DraftOwner.System, "order", instance.Id);
            (
                await store.Update(
                    DraftOwner.System,
                    instance.Id,
                    new Snapshot
                    {
                        Machine = "order",
                        Version = 1,
                        State = "Review",
                        Context = new JsonObject
                        {
                            ["items"] = new JsonArray(1),
                            ["receipt"] = null,
                        },
                    },
                    stored!.Token,
                    request: null
                )
            ).Should().BeTrue();
        }

        using (var scope = _host.Scope())
        {
            var runner = scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .EffectRunner("order")!;
            (await runner.Run("u1", instance.Id, "req-1"))
                .Should()
                .BeOfType<AdvanceOutcome.NotFound>();
            ((CountingEffect)scope.ServiceProvider.GetRequiredService<IOrderCharge>())
                .Calls.Should()
                .Be(0, "no effect runs for a draft the user does not own");
        }

        (await _host.Rows(instance.Id)).Single().State.Should().Be("Review");
    }

    [Test]
    public async Task A_system_instance_never_appears_in_a_users_load()
    {
        var instance = await _host.Start<TurnstileMachine>(NewKey());

        using var scope = _host.Scope();
        (await _host.Service(scope, "turnstile").Load("u1", instance.Id))
            .Should()
            .BeOfType<LoadResult.NotFound>($"a user's load never returns a system row. See {Adr}");

        var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        (await store.Get("u1", instance.Id)).Should().BeNull();
        (await store.Get("u1", "turnstile", instance.Id)).Should().BeNull();
    }

    [Test]
    public async Task A_user_holding_the_same_id_does_not_reach_the_system_row()
    {
        var instance = await _host.Start<TurnstileMachine>(NewKey());

        using (var scope = _host.Scope())
        {
            var turnstile = _host.Service(scope, "turnstile");
            await turnstile.Autosave("u1", instance.Id, UnlockedJson);

            // The user's load, advance and delete act on the user's own row only.
            (await turnstile.Load("u1", instance.Id))
                .Should()
                .BeOfType<LoadResult.Loaded>()
                .Which.Snapshot.State.Should()
                .Be("Unlocked");
            (await turnstile.Advance("u1", instance.Id, "Push"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Locked");

            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            await store.Delete("u1", "turnstile", instance.Id);
            await store.Delete("u1", instance.Id);
        }

        var rows = await _host.Rows(instance.Id);
        rows.Should()
            .ContainSingle($"the user's delete leaves the system row. See {Adr}")
            .Which.OwnerKind.Should()
            .Be(SnapshotOwnerKind.System);

        using var check = _host.Scope();
        (await _host.Instances(check).Get(DraftOwner.System, "turnstile", instance.Id))
            .Should()
            .NotBeNull();
    }
}
