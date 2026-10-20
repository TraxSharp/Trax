using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Instances the system owns: created only by <see cref="IMachineInstances.Start{TMachine}"/> for a machine that
/// declares <c>SystemOwned()</c>, keyed by an id derived from the key, and out of reach of every user path (load,
/// save, advance, send), even a user holding the same id. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class SystemOwnedInstanceTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private const string Machine = "system-turnstile";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static MachineKey NewKey() => MachineKey.Of("source", Guid.NewGuid().ToString());

    private static Snapshot Unlocked =>
        new()
        {
            Machine = Machine,
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = "quarter" },
        };

    [Test]
    public async Task Start_twice_with_one_key_returns_one_instance_and_queues_nothing_new()
    {
        var key = NewKey();
        var queued = await _host.WorkQueueCount();

        var first = await _host.Start<SystemTurnstileMachine>(key);
        var second = await _host.Start<SystemTurnstileMachine>(key);

        first.Created.Should().BeTrue();
        second
            .Should()
            .Be(first with { Created = false }, $"one key names one instance. See {Adr}");
        first.Id.Should().Be(MachineInstanceId.For(Machine, key));
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
            Enumerable
                .Range(0, 8)
                .Select(_ => Task.Run(() => _host.Start<SystemTurnstileMachine>(key)))
        );

        results.Select(r => r.Id).Distinct().Should().ContainSingle();
        results.Count(r => r.Created).Should().Be(1, "exactly one Start creates the row");
        (await _host.Rows(results[0].Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Start_uses_the_given_context_and_refuses_an_invalid_one()
    {
        var created = await _host.Start<SystemOrderMachine>(
            NewKey(),
            new JsonObject { ["items"] = new JsonArray(1, 2), ["receipt"] = null }
        );
        JsonNode.Parse((await _host.Rows(created.Id)).Single().Context)!["items"]!
            .ToJsonString()
            .Should()
            .Be("[1,2]");

        var invalid = () =>
            _host.Start<SystemTurnstileMachine>(NewKey(), new JsonObject { ["stray"] = 1 });
        await invalid
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*Locked carries no context*");
    }

    [Test]
    public async Task Start_refuses_a_user_owned_machine()
    {
        var start = () => _host.Start<TurnstileMachine>(NewKey());

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>(
                $"only a machine that declares SystemOwned() has system instances. See {Adr}"
            )
            .WithMessage("*user-owned*SystemOwned()*");
    }

    [Test]
    public async Task A_system_instance_refuses_advance_and_save_from_any_user()
    {
        var instance = await _host.Start<SystemTurnstileMachine>(NewKey());
        var before = (await _host.Rows(instance.Id)).Single();

        using (var scope = _host.Scope())
        {
            var registry = scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>();
            registry
                .Service(Machine)
                .Should()
                .BeNull(
                    $"no user's draft operation reaches a system-owned machine, which the mutations answer as "
                        + $"unknown-machine. See {Adr}"
                );

            // Below the draft service, a user's own writes under the id never reach the system row.
            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            (await store.Update("u1", instance.Id, Unlocked, before.ConcurrencyToken))
                .Should()
                .BeFalse("a user's update never matches a system row");
            (await store.Upsert("u1", instance.Id, Unlocked)).Should().BeTrue();
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
        var instance = await _host.Start<SystemOrderMachine>(
            NewKey(),
            new JsonObject { ["items"] = new JsonArray(1), ["receipt"] = null }
        );

        using (var scope = _host.Scope())
        {
            scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .EffectRunner("system-order")
                .Should()
                .BeNull("no user can send a system-owned machine's effect");
            ((CountingEffect)scope.ServiceProvider.GetRequiredService<IOrderCharge>())
                .Calls.Should()
                .Be(0);
        }

        (await _host.Rows(instance.Id)).Single().State.Should().Be("Draft");
    }

    [Test]
    public async Task A_system_instance_never_appears_in_a_users_load()
    {
        var instance = await _host.Start<SystemTurnstileMachine>(NewKey());

        using var scope = _host.Scope();
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(Machine)
            .Should()
            .BeNull($"a user's load never reaches a system-owned machine. See {Adr}");

        var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        (await store.Get("u1", instance.Id)).Should().BeNull();
        (await store.Get("u1", Machine, instance.Id)).Should().BeNull();
    }

    [Test]
    public async Task A_user_holding_the_same_id_does_not_reach_the_system_row()
    {
        var instance = await _host.Start<SystemTurnstileMachine>(NewKey());

        using (var scope = _host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            (await store.Insert("u1", instance.Id, Unlocked)).Should().BeTrue();

            // The user's read, write and delete act on the user's own row only.
            var own = await store.Get("u1", Machine, instance.Id);
            own.Should().NotBeNull();
            JsonNode.Parse(own!.Json)!["state"]!.GetValue<string>().Should().Be("Unlocked");
            (
                await store.Update(
                    "u1",
                    instance.Id,
                    Unlocked with
                    {
                        State = "Locked",
                        Context = new JsonObject(),
                    },
                    own.Token
                )
            )
                .Should()
                .BeTrue();

            await store.Delete("u1", Machine, instance.Id);
            await store.Delete("u1", instance.Id);
        }

        var rows = await _host.Rows(instance.Id);
        rows.Should()
            .ContainSingle($"the user's delete leaves the system row. See {Adr}")
            .Which.OwnerKind.Should()
            .Be(SnapshotOwnerKind.System);

        using var check = _host.Scope();
        (await _host.Instances(check).Get(DraftOwner.System, Machine, instance.Id))
            .Should()
            .NotBeNull();
    }
}
