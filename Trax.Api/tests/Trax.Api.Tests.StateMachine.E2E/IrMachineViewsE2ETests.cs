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
/// refused <c>FORBIDDEN</c>, and a system instance is started by the host. The proof of concept served a view to any
/// signed-in caller and read the newest row of whichever user had started one.
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
        var (machines, host) = Machines();
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

    private async Task<string> Start(string key) =>
        (
            await Post(
                key,
                $$"""mutation { machines { vOrder { start(requestId: "s-{{Guid.NewGuid():N}}") { id } } } }"""
            )
        )
            .GetProperty("data")
            .GetProperty("machines")
            .GetProperty("vOrder")
            .GetProperty("start")
            .GetProperty("id")
            .GetString()!;

    private async Task<JsonElement> Post(string key, string query)
    {
        using var doc = await _host.PostAsync(query, apiKey: key);
        return doc.RootElement.Clone();
    }

    // An order Payers keep, with a count of loaves, and a shop the host opens; the orders view, for Payers, and the shop
    // sign, for anyone signed in.
    internal static (
        IReadOnlyList<Trax.StateMachine.Persistence.IMachine> Machines,
        HostFile Host
    ) Machines()
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
        var payers = new JsonObject
        {
            ["roles"] = new JsonArray(new JsonArray(WizardMachine.PayerRole)),
        };
        var host = HostFile.Read(
            new JsonObject
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
            }.ToJsonString()
        );
        var irs = new Dictionary<string, JsonObject> { ["v-order"] = order, ["v-shop"] = shop };
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
