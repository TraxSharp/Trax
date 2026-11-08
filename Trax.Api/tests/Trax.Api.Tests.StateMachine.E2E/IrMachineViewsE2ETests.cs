using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Views of machines served from features are served by Trax under the read rule the features derive (ADR
/// <c>Trax.Docs/adr/0058</c>): a reader sees their own instances and the system instance at its key, anyone else is
/// refused <c>FORBIDDEN</c>, and a system instance is started by the host. A view is never served to any signed-in
/// caller, and never reads the newest row of whichever user started one.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IrMachineViewsE2ETests
{
    private const string Database = "trax_sm_machine_views";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        var (machines, host) = Machines(receipts: true);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Instances = machines,
                Views = new MachineViewCatalog(host.Views),
                Services = s =>
                {
                    s.AddSingleton(host);
                    s.AddHostedService<SystemInstanceStarter>();
                },
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_reader_sees_their_own_instances_only()
    {
        var mine = await Start(E2EHost.AliceKey);
        await Post(
            E2EHost.AliceKey,
            $$"""mutation { machines { vOrder { place(id: "{{mine}}", requestId: "p-{{mine}}") { state } } } }"""
        );
        await Start(E2EHost.CarolKey);

        var rows = (
            await Post(E2EHost.AliceKey, "{ views { orders { vOrder { id status loaves } } } }")
        )
            .GetProperty("data")
            .GetProperty("views")
            .GetProperty("orders")
            .GetProperty("vOrder");

        rows.EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .Should()
            .Equal([mine], "Carol's order is hers");
        rows[0].GetProperty("status").GetString().Should().Be("Placed!", "a state shows its label");
        rows[0].GetProperty("loaves").GetInt64().Should().Be(0);
    }

    [Test]
    public async Task A_caller_who_is_not_a_reader_is_refused()
    {
        var answer = await Post(E2EHost.BobKey, "{ views { orders { vOrder { id } } } }");

        answer
            .GetProperty("errors")[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("FORBIDDEN");
    }

    [Test]
    public async Task The_system_instance_is_started_by_the_host_and_read_at_its_key()
    {
        var sign = (await Post(E2EHost.BobKey, "{ views { shopSign { vShop { open } } } }"))
            .GetProperty("data")
            .GetProperty("views")
            .GetProperty("shopSign")
            .GetProperty("vShop");

        sign.GetProperty("open").GetString().Should().Be("Closed for now");
    }

    [Test]
    public async Task A_reader_the_machines_read_rule_refuses_sees_none_of_its_instances()
    {
        // Bob may start a receipt and read the view, but a receipt is read only by Payers, which he is not.
        await Start(E2EHost.BobKey, "vReceipt");
        var order = await Start(E2EHost.BobKey);

        var view = (
            await Post(E2EHost.BobKey, "{ views { myThings { vReceipt { id } vOrder { id } } } }")
        )
            .GetProperty("data")
            .GetProperty("views")
            .GetProperty("myThings");

        view.GetProperty("vReceipt")
            .GetArrayLength()
            .Should()
            .Be(0, "the receipt's read rule refuses Bob, as its get does");
        view.GetProperty("vOrder")
            .EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .Should()
            .Equal([order], "the order's read rule admits him");
    }

    private async Task<string> Start(string key, string machine = "vOrder") =>
        (
            await Post(
                key,
                $$"""mutation { machines { {{machine}} { start(requestId: "s-{{Guid.NewGuid():N}}") { id } } } }"""
            )
        )
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty(machine)
            .GetProperty("start")
            .GetProperty("id")
            .GetString()!;

    private async Task<JsonElement> Post(string key, string query)
    {
        using var doc = await _host.PostAsync(query, apiKey: key);
        return doc.RootElement.Clone();
    }

    // An order Payers keep, with a count of loaves, and a shop the host opens; the orders view, for Payers, and the shop
    // sign, for anyone signed in. With receipts, a receipt only Payers read and a view of receipts and orders for anyone
    // signed in.
    internal static (
        IReadOnlyList<Trax.StateMachine.Persistence.IMachine> Machines,
        HostFile Host
    ) Machines(bool receipts = false)
    {
        var loaves = new JsonObject
        {
            ["name"] = "loaves",
            ["type"] = "integer",
            ["nullable"] = false,
            ["constraints"] = new JsonArray(),
        };
        var order = new JsonObject
        {
            ["id"] = "v-order",
            ["version"] = 1,
            ["initialState"] = "Draft",
            ["initialContext"] = new JsonObject { ["loaves"] = 0 },
            ["states"] = new JsonArray("Draft", "Placed"),
            ["triggers"] = new JsonArray("Place"),
            ["committedStates"] = new JsonArray(),
            ["context"] = new JsonObject
            {
                ["Draft"] = new JsonObject { ["fields"] = new JsonArray(loaves.DeepClone()) },
                ["Placed"] = new JsonObject { ["fields"] = new JsonArray(loaves.DeepClone()) },
            },
            ["inputs"] = new JsonObject(),
            ["transitions"] = new JsonArray(
                new JsonObject
                {
                    ["from"] = "Draft",
                    ["trigger"] = "Place",
                    ["to"] = "Placed",
                }
            ),
        };
        var shop = new JsonObject
        {
            ["id"] = "v-shop",
            ["version"] = 1,
            ["initialState"] = "Closed",
            ["initialContext"] = new JsonObject(),
            ["states"] = new JsonArray("Closed", "Opened"),
            ["triggers"] = new JsonArray("Open"),
            ["committedStates"] = new JsonArray(),
            ["context"] = new JsonObject
            {
                ["Closed"] = new JsonObject { ["fields"] = new JsonArray() },
                ["Opened"] = new JsonObject { ["fields"] = new JsonArray() },
            },
            ["inputs"] = new JsonObject(),
            ["transitions"] = new JsonArray(
                new JsonObject
                {
                    ["from"] = "Closed",
                    ["trigger"] = "Open",
                    ["to"] = "Opened",
                }
            ),
        };
        var receipt = new JsonObject
        {
            ["id"] = "v-receipt",
            ["version"] = 1,
            ["initialState"] = "Issued",
            ["initialContext"] = new JsonObject(),
            ["states"] = new JsonArray("Issued", "Void"),
            ["triggers"] = new JsonArray("Cancel"),
            ["committedStates"] = new JsonArray(),
            ["context"] = new JsonObject
            {
                ["Issued"] = new JsonObject { ["fields"] = new JsonArray() },
                ["Void"] = new JsonObject { ["fields"] = new JsonArray() },
            },
            ["inputs"] = new JsonObject(),
            ["transitions"] = new JsonArray(
                new JsonObject
                {
                    ["from"] = "Issued",
                    ["trigger"] = "Cancel",
                    ["to"] = "Void",
                }
            ),
        };
        var payers = new JsonObject
        {
            ["roles"] = new JsonArray(new JsonArray(WizardMachine.PayerRole)),
        };
        var file = new JsonObject
        {
            ["version"] = 1,
            ["machines"] = new JsonObject
            {
                ["v-order"] = new JsonObject
                {
                    ["schemaHash"] = Hash(order),
                    ["owner"] = "user",
                    ["start"] = new JsonObject { ["anyAuthenticated"] = true },
                    ["read"] = new JsonObject { ["anyAuthenticated"] = true },
                    ["triggers"] = new JsonObject
                    {
                        ["Place"] = new JsonObject { ["anyAuthenticated"] = true },
                    },
                },
                ["v-receipt"] = new JsonObject
                {
                    ["schemaHash"] = Hash(receipt),
                    ["owner"] = "user",
                    ["start"] = new JsonObject { ["anyAuthenticated"] = true },
                    ["read"] = payers.DeepClone(),
                    ["triggers"] = new JsonObject
                    {
                        ["Cancel"] = new JsonObject { ["anyAuthenticated"] = true },
                    },
                },
                ["v-shop"] = new JsonObject
                {
                    ["schemaHash"] = Hash(shop),
                    ["owner"] = new JsonObject { ["system"] = "v-shop/main" },
                    ["triggers"] = new JsonObject
                    {
                        ["Open"] = new JsonObject { ["server"] = true },
                    },
                },
            },
            ["views"] = new JsonObject
            {
                ["orders"] = new JsonObject
                {
                    ["reads"] = new JsonArray(new JsonObject { ["machine"] = "v-order" }),
                    ["fields"] = new JsonObject
                    {
                        ["status"] = new JsonObject
                        {
                            ["from"] = "v-order.state",
                            ["labels"] = new JsonObject
                            {
                                ["Draft"] = "Not sent",
                                ["Placed"] = "Placed!",
                            },
                        },
                        ["loaves"] = new JsonObject { ["from"] = "v-order.loaves" },
                    },
                    ["readers"] = payers,
                },
                // Read by anyone signed in, over a machine only Payers read.
                ["my things"] = new JsonObject
                {
                    ["reads"] = new JsonArray(
                        new JsonObject { ["machine"] = "v-receipt" },
                        new JsonObject { ["machine"] = "v-order" }
                    ),
                    ["fields"] = new JsonObject
                    {
                        ["receipt"] = new JsonObject { ["from"] = "v-receipt.state" },
                        ["order"] = new JsonObject { ["from"] = "v-order.state" },
                    },
                    ["readers"] = new JsonObject { ["anyAuthenticated"] = true },
                },
                ["shop sign"] = new JsonObject
                {
                    ["reads"] = new JsonArray(
                        new JsonObject { ["machine"] = "v-shop", ["key"] = "v-shop/main" }
                    ),
                    ["fields"] = new JsonObject
                    {
                        ["open"] = new JsonObject
                        {
                            ["from"] = "v-shop.state",
                            ["labels"] = new JsonObject
                            {
                                ["Closed"] = "Closed for now",
                                ["Opened"] = "Open",
                            },
                        },
                    },
                    ["readers"] = new JsonObject { ["anyAuthenticated"] = true },
                },
            },
        };
        var irs = new Dictionary<string, JsonObject>
        {
            ["v-order"] = order,
            ["v-receipt"] = receipt,
            ["v-shop"] = shop,
        };
        if (!receipts)
        {
            file["machines"]!.AsObject().Remove("v-receipt");
            file["views"]!.AsObject().Remove("my things");
            irs.Remove("v-receipt");
        }
        var host = HostFile.Read(file.ToJsonString());
        IrDirectory.Check("test", host, irs);
        var machines = IrMachines.Create(
            irs.Select(kv => new IrMachines.Source(kv.Value, host.Machines[kv.Key])).ToList(),
            []
        );
        return (machines, host);
    }

    private static string Hash(JsonObject ir) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
        );
}
