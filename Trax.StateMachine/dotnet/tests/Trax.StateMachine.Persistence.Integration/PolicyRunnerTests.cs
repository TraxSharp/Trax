using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.MachinePolicyFire;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A policy between machines is the server acting, as a timer is (ADR <c>Trax.Docs/adr/0058</c>): it fires a
/// server-only trigger on the instance its shape names, never another user's, records the fire as the policy's, and
/// fires once for each transition that sets it off, whenever and on whichever host. The proof of concept fired on every
/// user's instance in the state, as that user, from a cursor that lost what happened while it was down.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class PolicyRunnerTests(StoreProvider provider)
{
    private const string Linked = "p-order.Expired>p-receipt.Cancel";
    private const string Broadcast = "p-shop.Opened>p-order.Bake";

    private static readonly JsonObject Field = new()
    {
        ["name"] = "orderId",
        ["type"] = "string",
        ["nullable"] = true,
        ["format"] = "uuid",
        ["constraints"] = new JsonArray(),
    };

    private InstanceHost _host = null!;
    private MachinePolicies _policies = null!;

    [SetUp]
    public void SetUp()
    {
        var (machines, host) = Machines();
        _policies = new MachinePolicies(host.Policies);
        _host = Host(machines, cap: 3);
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private InstanceHost Host(
        IReadOnlyList<IMachine> machines,
        int cap,
        InstanceHost? sameDatabaseAs = null
    ) =>
        InstanceHost.Create(
            provider,
            sameDatabaseAs: sameDatabaseAs,
            services: s =>
            {
                s.AddIrMachines(machines);
                s.AddSingleton(_policies);
                s.AddSingleton(new PolicyRunnerOptions { BatchSize = 100, FanOutCap = cap });
                s.AddSingleton<MachinePolicyRunner>();
            }
        );

    private MachinePolicyRunner Runner(InstanceHost host) =>
        host.Services.GetRequiredService<MachinePolicyRunner>();

    private static string NewUser() => $"policy-{Guid.NewGuid():N}";

    [Test]
    public async Task A_linked_policy_fires_only_the_owners_instance_that_links_the_source()
    {
        var ada = NewUser();
        var bo = NewUser();
        var order = await Start(ada, "p-order");
        var adasReceipt = await Start(ada, "p-receipt");
        var adasOther = await Start(ada, "p-receipt");
        var bosReceipt = await Start(bo, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            adasReceipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );
        await Advance(
            ada,
            "p-receipt",
            adasOther,
            "Attach",
            new JsonObject { ["orderId"] = Guid.NewGuid().ToString() }
        );
        // Bo's receipt names Ada's order, which only a policy that ignores owners would act on.
        await Advance(
            bo,
            "p-receipt",
            bosReceipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );

        await Advance(ada, "p-order", order, "Expire");
        (await Owed())
            .Should()
            .ContainSingle()
            .Which.Policy.Should()
            .Be(Linked, "the transition queued its fire as it committed");
        await Runner(_host).PassOnce(CancellationToken.None);

        (await StateOf(adasReceipt)).Should().Be("Void");
        (await StateOf(adasOther)).Should().Be("Issued", "it links another order");
        (await StateOf(bosReceipt))
            .Should()
            .Be("Issued", "a policy never acts on another user's instance");
        (await Owed()).Should().BeEmpty("the fire was deleted with the write it made");
    }

    [Test]
    public async Task A_policys_fire_is_recorded_as_the_policys_never_the_owners()
    {
        var ada = NewUser();
        var order = await Start(ada, "p-order");
        var receipt = await Start(ada, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            receipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );
        await Advance(ada, "p-order", order, "Expire");

        await Runner(_host).PassOnce(CancellationToken.None);

        var row = (await _host.Rows(receipt)).Single().RowId;
        using var scope = _host.Scope();
        var last = await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotTransitions.AsNoTracking()
            .Where(t => t.DraftRowId == row)
            .OrderByDescending(t => t.Revision)
            .FirstAsync();
        last.Actor.Should().Be(SnapshotTransitionActor.Policy);
        last.Kind.Should().Be(SnapshotTransitionKind.Command);
        last.Trigger.Should().Be("Cancel");
    }

    [Test]
    public async Task A_fire_owed_while_no_runner_ran_is_fired_once_by_the_next_host()
    {
        var ada = NewUser();
        var order = await Start(ada, "p-order");
        var receipt = await Start(ada, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            receipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );
        await Advance(ada, "p-order", order, "Expire");
        (await Owed()).Should().ContainSingle();

        // Another host, as after a restart, over the same database.
        using var next = Host(Machines().Machines, cap: 3, sameDatabaseAs: _host);
        (await Owed(next)).Should().ContainSingle("the next host reads the same database");
        await Runner(next).PassOnce(CancellationToken.None);
        await Runner(_host).PassOnce(CancellationToken.None);

        (await StateOf(receipt)).Should().Be("Void");
        (await PolicyFires(receipt))
            .Should()
            .Be(1, "a fire happens once, whichever host claims it");
    }

    [Test]
    public async Task Two_hosts_passing_at_once_fire_a_fire_once()
    {
        var ada = NewUser();
        var order = await Start(ada, "p-order");
        var receipt = await Start(ada, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            receipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );
        await Advance(ada, "p-order", order, "Expire");
        using var other = Host(Machines().Machines, cap: 3, sameDatabaseAs: _host);

        // A pass that loses SQLite's write lock throws and is tried again, so each host passes until nothing is owed.
        await Task.WhenAll(Drain(_host), Drain(other));

        (await StateOf(receipt)).Should().Be("Void");
        (await PolicyFires(receipt))
            .Should()
            .Be(1, "a fire happens once, whichever host claims it");
    }

    [Test]
    public async Task Two_hosts_expanding_one_broadcast_do_not_wait_on_each_other()
    {
        if (provider != StoreProvider.Postgres)
            Assert.Ignore(
                "SQLite holds one write lock for the database; no two expansions overlap."
            );
        var user = NewUser();
        var order = await Start(user, "p-order");
        await Advance(user, "p-order", order, "Place");
        await Queue(Broadcast);
        var held = await Queue(Broadcast);

        // Another host holds the second row, as it would while expanding it.
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (
            var hold = new NpgsqlCommand(
                "SELECT id FROM trax.machine_policy_fire WHERE id = @id FOR UPDATE",
                connection,
                transaction
            )
        )
        {
            hold.Parameters.AddWithValue("id", held);
            await hold.ExecuteScalarAsync();
        }

        var pass = Runner(_host).PassOnce(CancellationToken.None);
        // negative-wait: a pass that waits on the held row never ends while it is held.
        var finished = await Task.WhenAny(pass, Task.Delay(TimeSpan.FromSeconds(10))) == pass;
        await transaction.RollbackAsync();
        await pass;

        finished.Should().BeTrue("a host coalesces only the rows it can claim");
        await Drain(_host);
        (await StateOf(order)).Should().Be("Baking");
        (await Owed()).Should().BeEmpty();
    }

    [Test]
    public async Task A_fire_whose_target_is_busy_is_kept_and_fired_on_a_later_pass()
    {
        var ada = NewUser();
        var order = await Start(ada, "p-order");
        var receipt = await Start(ada, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            receipt,
            "Attach",
            new JsonObject { ["orderId"] = order.ToString() }
        );
        await Advance(ada, "p-order", order, "Expire");
        await HoldEffect(receipt, Guid.NewGuid());

        await Runner(_host).PassOnce(CancellationToken.None);

        (await StateOf(receipt)).Should().Be("Issued", "an effect in flight holds the receipt");
        (await Owed()).Should().ContainSingle("the fire is still owed while its target is held");

        await HoldEffect(receipt, null);
        await Runner(_host).PassOnce(CancellationToken.None);

        (await StateOf(receipt)).Should().Be("Void");
        (await Owed()).Should().BeEmpty();
    }

    [Test]
    public async Task A_broadcast_fire_whose_target_is_busy_is_kept_and_fired_on_a_later_pass()
    {
        var user = NewUser();
        var order = await Start(user, "p-order");
        await Advance(user, "p-order", order, "Place");
        await Queue(Broadcast);
        await HoldEffect(order, Guid.NewGuid());

        await Runner(_host).PassOnce(CancellationToken.None);
        await Runner(_host).PassOnce(CancellationToken.None);

        var row = (await _host.Rows(order)).Single().RowId;
        (await StateOf(order)).Should().Be("Placed");
        (await Owed())
            .Should()
            .Contain(
                f => f.TargetRowId == row,
                "the expanded fire is still owed while its target is held"
            );

        await HoldEffect(order, null);
        await Runner(_host).PassOnce(CancellationToken.None);

        (await StateOf(order)).Should().Be("Baking");
        (await Owed()).Should().BeEmpty();
    }

    [Test]
    public async Task A_creation_and_a_save_set_no_policy_off()
    {
        var ada = NewUser();
        await Start(ada, "p-order");
        await Start(ada, "p-receipt");

        (await Owed()).Should().BeEmpty();
    }

    [Test]
    public async Task A_broadcast_is_capped_and_coalesced()
    {
        var users = Enumerable.Range(0, 5).Select(_ => NewUser()).ToList();
        var orders = new List<Guid>();
        foreach (var user in users)
        {
            var order = await Start(user, "p-order");
            await Advance(user, "p-order", order, "Place");
            orders.Add(order);
        }
        // Two transitions of the shop owe the same broadcast; it reaches the same instances once.
        await Queue(Broadcast);
        await Queue(Broadcast);

        await Runner(_host).PassOnce(CancellationToken.None);
        await Runner(_host).PassOnce(CancellationToken.None);

        var baking = 0;
        foreach (var order in orders)
            if (await StateOf(order) == "Baking")
                baking++;
        baking
            .Should()
            .Be(3, "the cap is 3 instances per broadcast, and the second transition was coalesced");
        (await Owed()).Should().BeEmpty();
    }

    [Test]
    public async Task A_client_cannot_fire_a_server_only_trigger()
    {
        var ada = NewUser();
        var receipt = await Start(ada, "p-receipt");
        await Advance(
            ada,
            "p-receipt",
            receipt,
            "Attach",
            new JsonObject { ["orderId"] = Guid.NewGuid().ToString() }
        );

        using var scope = _host.Scope();
        (
            await _host
                .Service(scope, "p-receipt")
                .Advance(DeskRoles.Client(ada), receipt, "Cancel", requestId: "c1")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.Forbidden);
    }

    private async Task<Guid> Start(string user, string machine)
    {
        using var scope = _host.Scope();
        return (
            await _host
                .Service(scope, machine)
                .Start(DeskRoles.Client(user), new JsonObject(), $"s-{Guid.NewGuid():N}")
        )
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject.Id;
    }

    private async Task Advance(
        string user,
        string machine,
        Guid id,
        string trigger,
        JsonObject? input = null
    )
    {
        using var scope = _host.Scope();
        (
            await _host
                .Service(scope, machine)
                .Advance(DeskRoles.Client(user), id, trigger, input, $"a-{Guid.NewGuid():N}")
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }

    private async Task<string> StateOf(Guid id) => (await _host.Rows(id)).Single().State;

    private async Task<List<MachinePolicyFire>> Owed(InstanceHost? host = null)
    {
        using var scope = (host ?? _host).Scope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.MachinePolicyFires.AsNoTracking().ToListAsync();
    }

    // Marks the instance with an effect in flight, as the effect runner does, or clears it.
    private async Task HoldEffect(Guid id, Guid? token)
    {
        using var scope = _host.Scope();
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.Id == id)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.EffectToken, token));
    }

    private async Task<long> Queue(string policy)
    {
        using var scope = _host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var fire = new MachinePolicyFire
        {
            Policy = policy,
            SourceRowId = -1,
            SourceInstanceId = Guid.NewGuid(),
            SourceRevision = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.MachinePolicyFires.Add(fire);
        await context.SaveChanges(CancellationToken.None);
        return fire.Id;
    }

    // Passes until nothing is owed, as the runner's loop does, within a bound.
    private async Task Drain(InstanceHost host)
    {
        for (var i = 0; i < 20 && (await Owed(host)).Count > 0; i++)
            await Runner(host).PassOnce(CancellationToken.None);
    }

    private async Task<int> PolicyFires(Guid id)
    {
        var row = (await _host.Rows(id)).Single().RowId;
        using var scope = _host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotTransitions.CountAsync(t =>
                t.DraftRowId == row && t.Actor == SnapshotTransitionActor.Policy
            );
    }

    // An order a customer may expire, place, and have baked by the shop; a receipt that names an order and is voided
    // when that order expires; and the shop, whose opening starts every placed order baking.
    private static (IReadOnlyList<IMachine> Machines, HostFile Host) Machines()
    {
        var order = Ir(
            "p-order",
            "Draft",
            ["Draft", "Expired", "Placed", "Baking"],
            ["Bake", "Expire", "Place"],
            [
                ("Draft", "Expire", "Expired"),
                ("Draft", "Place", "Placed"),
                ("Placed", "Bake", "Baking"),
            ]
        );
        var receipt = Ir(
            "p-receipt",
            "Open",
            ["Open", "Issued", "Void"],
            ["Attach", "Cancel"],
            [("Issued", "Cancel", "Void"), ("Open", "Attach", "Issued")],
            withField: true
        );
        receipt["inputs"] = new JsonObject
        {
            ["Attach"] = new JsonObject { ["fields"] = new JsonArray(Field.DeepClone()) },
        };
        receipt["transitions"]!
            .AsArray()
            .Single(t => t!["trigger"]!.GetValue<string>() == "Attach")!["reduce"] = new JsonObject
        {
            ["reduce"] = "set",
            ["steps"] = new JsonArray(
                new JsonObject
                {
                    ["field"] = "orderId",
                    ["value"] = new JsonObject { ["input"] = "orderId" },
                }
            ),
        };
        var shop = Ir(
            "p-shop",
            "Closed",
            ["Closed", "Opened"],
            ["Open"],
            [("Closed", "Open", "Opened")]
        );

        var any = new JsonObject { ["anyAuthenticated"] = true };
        var server = new JsonObject { ["server"] = true };
        var host = new JsonObject
        {
            ["version"] = 1,
            ["machines"] = new JsonObject
            {
                ["p-order"] = new JsonObject
                {
                    ["schemaHash"] = Hash(order),
                    ["owner"] = "user",
                    ["start"] = any.DeepClone(),
                    ["read"] = any.DeepClone(),
                    ["triggers"] = new JsonObject
                    {
                        ["Expire"] = any.DeepClone(),
                        ["Place"] = any.DeepClone(),
                        ["Bake"] = server.DeepClone(),
                    },
                },
                ["p-receipt"] = new JsonObject
                {
                    ["schemaHash"] = Hash(receipt),
                    ["owner"] = "user",
                    ["start"] = any.DeepClone(),
                    ["read"] = any.DeepClone(),
                    ["triggers"] = new JsonObject
                    {
                        ["Attach"] = any.DeepClone(),
                        ["Cancel"] = server.DeepClone(),
                    },
                },
                ["p-shop"] = new JsonObject
                {
                    ["schemaHash"] = Hash(shop),
                    ["owner"] = new JsonObject { ["system"] = "p-shop/main" },
                    ["triggers"] = new JsonObject { ["Open"] = server.DeepClone() },
                },
            },
            ["policies"] = new JsonArray(
                new JsonObject
                {
                    ["shape"] = "linked",
                    ["when"] = new JsonObject { ["machine"] = "p-order", ["enters"] = "Expired" },
                    ["then"] = new JsonObject
                    {
                        ["machine"] = "p-receipt",
                        ["link"] = "orderId",
                        ["trigger"] = "Cancel",
                    },
                },
                new JsonObject
                {
                    ["shape"] = "broadcast",
                    ["when"] = new JsonObject { ["machine"] = "p-shop", ["enters"] = "Opened" },
                    ["then"] = new JsonObject
                    {
                        ["machine"] = "p-order",
                        ["inState"] = "Placed",
                        ["trigger"] = "Bake",
                    },
                }
            ),
        };
        var file = HostFile.Read(host.ToJsonString());
        var irs = new Dictionary<string, JsonObject>
        {
            ["p-order"] = order,
            ["p-receipt"] = receipt,
            ["p-shop"] = shop,
        };
        IrDirectory.Check("test", file, irs);
        var machines = IrMachines.Create(
            irs.Select(kv => new IrMachines.Source(kv.Value, file.Machines[kv.Key])).ToList(),
            []
        );
        return (machines, file);
    }

    private static JsonObject Ir(
        string id,
        string initial,
        string[] states,
        string[] triggers,
        (string From, string Trigger, string To)[] edges,
        bool withField = false
    )
    {
        var context = new JsonObject();
        foreach (var state in states.Order(StringComparer.Ordinal))
            context[state] = new JsonObject
            {
                ["fields"] = withField ? new JsonArray(Field.DeepClone()) : new JsonArray(),
            };
        var transitions = new JsonArray();
        foreach (
            var (from, trigger, to) in edges
                .OrderBy(e => e.From, StringComparer.Ordinal)
                .ThenBy(e => e.Trigger, StringComparer.Ordinal)
        )
            transitions.Add(
                new JsonObject
                {
                    ["from"] = from,
                    ["trigger"] = trigger,
                    ["to"] = to,
                }
            );
        return new JsonObject
        {
            ["id"] = id,
            ["version"] = 1,
            ["initialState"] = initial,
            ["initialContext"] = withField
                ? new JsonObject { ["orderId"] = null }
                : new JsonObject(),
            ["states"] = new JsonArray(
                states.Order(StringComparer.Ordinal).Select(s => (JsonNode?)s).ToArray()
            ),
            ["triggers"] = new JsonArray(
                triggers.Order(StringComparer.Ordinal).Select(s => (JsonNode?)s).ToArray()
            ),
            ["committedStates"] = new JsonArray(),
            ["context"] = context,
            ["inputs"] = new JsonObject(),
            ["transitions"] = transitions,
        };
    }

    private static string Hash(JsonObject ir) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
        );
}
