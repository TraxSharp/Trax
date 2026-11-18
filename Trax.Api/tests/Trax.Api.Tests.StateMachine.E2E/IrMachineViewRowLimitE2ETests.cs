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
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A view returns at most <see cref="StateMachineOptions.ViewRowLimit"/> instances of each user machine: the caller's
/// newest, oldest first.
/// </summary>
[TestFixture]
[NonParallelizable]
public class IrMachineViewRowLimitE2ETests
{
    private const string Database = "trax_sm_machine_view_limit";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        var (machines, host) = IrMachineViewsE2ETests.Machines();
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Instances = machines,
                Views = new MachineViewCatalog(host.Views),
                Machines = o => o.ViewRowLimit = 2,
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
    public async Task A_reader_holding_more_than_the_limit_gets_the_newest()
    {
        var started = new List<string>();
        for (var i = 0; i < 4; i++)
            started.Add(await Start());

        using var doc = await _host.PostAsync(
            "{ views { orders { vOrder { id } } } }",
            apiKey: E2EHost.AliceKey
        );
        doc.RootElement.GetProperty("data")
            .GetProperty("views")
            .GetProperty("orders")
            .GetProperty("vOrder")
            .EnumerateArray()
            .Select(r => r.GetProperty("id").GetString())
            .Should()
            .Equal(started[2..], "the limit is 2: the newest two, oldest first");
    }

    private async Task<string> Start()
    {
        using var doc = await _host
            .PostAsync(
                $$"""mutation { machines { vOrder { start(requestId: "s-{{Guid.NewGuid():N}}") { id } } } }""",
                apiKey: E2EHost.AliceKey
            )
            .ConfigureAwait(false);
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("vOrder")
            .GetProperty("start")
            .GetProperty("id")
            .GetString()!;
    }
}
