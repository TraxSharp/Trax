using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A push's watch rule is asked again at every connection re-check interval, for the same caller and the same row,
/// over a real socket: a stream whose caller no longer satisfies the rule's policy ends with <c>NOT_FOUND</c>, the
/// answer a refused subscribe gets, and the socket stays open; one whose caller still does goes on.
/// On a host gated with <c>RequireAuthorization(...)</c> the endpoint policy is asked first: a stream whose caller no
/// longer satisfies it ends the same way.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>
/// and <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[Property("adr", "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md")]
[TestFixture]
[NonParallelizable]
public class MachineChangedRecheckE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string SubscriptionsAdr =
        "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md";

    private const string Database = "trax_sm_machine_changed_recheck";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private IHost _host = null!;
    private volatile bool _watchers = true;
    private volatile bool _active = true;

    private const string ActivePolicy = "ActiveAccount";

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(BadgeMachine)],
                // The endpoint is gated, so the assembly's public diagnostic trains are left out.
                Trains = t => !t.HasAllowAnonymousAttribute,
                GraphQL = g =>
                    g.RecheckConnectionCredentialsEvery(Interval)
                        .RequireAuthorization(ActivePolicy),
                Services = s =>
                    s.AddAuthorization(o =>
                    {
                        o.AddPolicy(
                            BadgeMachine.WatchPolicy,
                            p => p.RequireAuthenticatedUser().RequireAssertion(_ => _watchers)
                        );
                        // The endpoint policy, reading state outside the principal.
                        o.AddPolicy(
                            ActivePolicy,
                            p => p.RequireAuthenticatedUser().RequireAssertion(_ => _active)
                        );
                    }),
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        await ShelfHosts.StopAsync(_host);
        await E2EHost.DropDatabaseAsync(Database);
    }

    [SetUp]
    public void Admit()
    {
        _watchers = true;
        _active = true;
    }

    [Test]
    public async Task A_stream_whose_caller_no_longer_satisfies_the_watch_rule_ends_not_found()
    {
        var id = await IssueAsync("Issued");
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(id, BadgeMachine.MachineId));
        (await ws.NextChangeAsync("1")).State.Should().NotBeNull("the owner may watch");

        _watchers = false;

        var last = (await ws.ReadUntilEndAsync("1", TimeSpan.FromSeconds(15)))[^1];
        last.ProblemCode.Should()
            .Be(
                RefusalCode.NotFound.ToWire(),
                "the watch rule is asked again at the re-check, per " + Adr
            );
        last.State.Should().BeNull("the refusal carries no snapshot");
        (await ws.NextAsync("1")).GetProperty("type").GetString().Should().Be("complete");

        _watchers = true;
        await ws.SubscribeAsync("2", MachineChangedOps.Subscribe(id, BadgeMachine.MachineId));
        (await ws.NextChangeAsync("2"))
            .State.Should()
            .NotBeNull("the socket stays open and a caller admitted again may subscribe again");
    }

    [Test]
    public async Task A_stream_whose_caller_still_satisfies_the_watch_rule_goes_on()
    {
        var id = await IssueAsync("Issued");
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(id, BadgeMachine.MachineId));
        await ws.NextChangeAsync("1");

        // allowed-delay: lets three re-checks pass on the real clock the socket runs on; the
        // write after them is what the test waits for.
        await Task.Delay(Interval * 3);
        await IssueAsync("Returned", id);

        var next = await ws.NextChangeAsync("1");
        next.ProblemCode.Should().BeNull("a caller the rule still admits keeps receiving");
        next.StateIs(nameof(BadgeState.Returned)).Should().BeTrue();
    }

    [Test]
    public async Task A_stream_whose_caller_no_longer_satisfies_the_endpoint_policy_ends_not_found()
    {
        var id = await IssueAsync("Issued");
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", MachineChangedOps.Subscribe(id, BadgeMachine.MachineId));
        (await ws.NextChangeAsync("1")).State.Should().NotBeNull("the owner may watch");

        _active = false;

        var last = (await ws.ReadUntilEndAsync("1", TimeSpan.FromSeconds(15)))[^1];
        last.ProblemCode.Should()
            .Be(
                RefusalCode.NotFound.ToWire(),
                "the endpoint policy is asked again at the re-check beside the watch rule, per "
                    + SubscriptionsAdr
            );
        last.State.Should().BeNull("the refusal carries no snapshot");
        (await ws.NextAsync("1")).GetProperty("type").GetString().Should().Be("complete");
    }

    private async Task<Guid> IssueAsync(string state, Guid? id = null)
    {
        var instance = id ?? Guid.NewGuid();
        await _host.SeedDraftAsync(
            "TraxApiKey:alice",
            instance,
            new JsonObject
            {
                ["machine"] = BadgeMachine.MachineId,
                ["version"] = 1,
                ["state"] = state,
                ["context"] = new JsonObject(),
            }.ToJsonString()
        );
        return instance;
    }
}
