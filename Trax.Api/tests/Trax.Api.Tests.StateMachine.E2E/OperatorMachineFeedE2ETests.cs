using System.Net.WebSockets;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The operator's live view of state-machine instances over GraphQL: a subscription behind the operations gate that
/// shows each written instance's state, revision and whether it waits on a run, never its context or its owner's
/// key; and one change listener per host, which the dashboard's feed and every GraphQL stream share.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class OperatorMachineFeedE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_operator_feed";

    /// <summary>The <c>application_name</c> of the host's database sessions.</summary>
    private const string ApplicationName = "trax_sm_operator_feed_host";

    /// <summary>The <c>application_name</c> of a host's one listening session.</summary>
    private const string ListenerApplicationName = "trax_snapshot_listener";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                ExposeOperations = true,
                ApplicationName = ApplicationName,
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        await ShelfHosts.StopAsync(_host);
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task The_operator_subscription_is_gated_and_shows_state_revision_and_runs()
    {
        await using var operatorWs = await GraphQLWs.ConnectAsync(_host, E2EHost.OperatorKey);
        await operatorWs.SubscribeAsync("feed", MachineChangedOps.SubscribeOperator());

        // A row written by any path reaches the feed: here one written by hand.
        var system = Guid.NewGuid();
        await E2ESql.ExecuteAsync(
            Database,
            "INSERT INTO trax.snapshot_draft (id, user_key, machine, version, state, context, "
                + "concurrency_token, updated_at, created_at, owner_kind) "
                + "VALUES (@id, NULL, @machine, 1, 'Naming', '{}'::jsonb, gen_random_uuid(), now(), now(), "
                + "'system')",
            ("id", system),
            ("machine", WizardMachine.MachineId)
        );
        var first = await NextForAsync(operatorWs, system);
        first.GetProperty("state").GetString().Should().Be("Naming", "per " + Adr);

        var approving = await _host.WizardInApprovingAsync(E2EHost.AliceKey);
        var building = await _host.AdvanceWizardAsync(E2EHost.AliceKey, "approve", approving.Id);
        JsonElement seen;
        do seen = await NextForAsync(operatorWs, approving.Id);
        while (!MachineResult.SameName(seen.GetProperty("state").GetString(), "Building"));

        seen.GetProperty("revision").GetInt64().Should().Be(building.Revision!.Value);
        seen.GetProperty("hasLiveInvokedRun")
            .GetBoolean()
            .Should()
            .BeTrue("the operator sees that the instance waits on the run it queued");

        foreach (var caller in new[] { E2EHost.BobKey, null })
        {
            GraphQLWs ws;
            try
            {
                ws = await GraphQLWs.ConnectAsync(_host, caller);
            }
            catch (Exception ex) when (ex is InvalidOperationException or WebSocketException)
            {
                continue; // refused at the handshake
            }
            await using (ws)
            {
                await ws.SubscribeAsync("feed", MachineChangedOps.SubscribeOperator());
                var answer = await ws.NextAsync("feed");
                answer
                    .GetProperty("type")
                    .GetString()
                    .Should()
                    .Be(
                        "error",
                        $"the feed is behind the operations gate, so {caller ?? "an anonymous caller"} "
                            + "is refused: "
                            + answer.GetRawText()
                    );
            }
        }
    }

    [Test]
    public async Task A_users_draft_change_appears_to_an_operator_without_its_context()
    {
        const string Secret = "Secret-Atlas-7731";
        await using var operatorWs = await GraphQLWs.ConnectAsync(_host, E2EHost.OperatorKey);
        await operatorWs.SubscribeAsync("feed", MachineChangedOps.SubscribeOperator());
        var raw = new List<string>();
        var operatorSees = Task.Run(async () =>
        {
            // Every event the feed sends until the described draft shows up.
            while (true)
            {
                var data = await operatorWs.NextDataAsync("feed");
                raw.Add(data.GetRawText());
                if (
                    MachineResult.SameName(
                        data.GetProperty("onMachineInstanceChanged")
                            .GetProperty("state")
                            .GetString(),
                        nameof(WizardState.Paying)
                    )
                )
                    return;
            }
        });

        var started = await _host.StartWizardAsync(E2EHost.AliceKey, title: Secret);
        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput(title: Secret, note: "private")
        );
        await operatorSees.WaitAsync(GraphQLWs.DefaultTimeout);

        raw.Should().NotBeEmpty();
        raw.Should()
            .OnlyContain(
                text =>
                    !text.Contains(Secret, StringComparison.Ordinal)
                    && !text.Contains("private", StringComparison.Ordinal)
                    && !text.Contains("alice", StringComparison.OrdinalIgnoreCase),
                "an operator sees that a user's draft changed, never what it holds or whose it is, per "
                    + Adr
            );
    }

    [Test]
    public async Task Each_host_runs_one_listener_shared_by_the_dashboard_and_GraphQL()
    {
        // The dashboard's feed: the shared operations service, read the way the Blazor page reads it.
        using var scope = _host.Services.CreateScope();
        using var stop = new CancellationTokenSource();
        var dashboard = scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .WatchMachineInstancesAsync(WizardMachine.MachineId, stop.Token)
            .GetAsyncEnumerator(stop.Token);
        var dashboardReading = dashboard.MoveNextAsync().AsTask();

        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await using var userWs = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await userWs.SubscribeAsync("mine", MachineChangedOps.Subscribe(started.Id));
        await userWs.NextChangeAsync("mine");
        await using var operatorWs = await GraphQLWs.ConnectAsync(_host, E2EHost.OperatorKey);
        await operatorWs.SubscribeAsync("feed", MachineChangedOps.SubscribeOperator());
        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput()
        );
        await operatorWs.NextDataAsync("feed");
        (await dashboardReading.WaitAsync(GraphQLWs.DefaultTimeout))
            .Should()
            .BeTrue("the dashboard's feed heard the write too");

        // The listener's session names itself, whatever the host's own sessions are called.
        var listeners = await E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name "
                + "AND datname = current_database()",
            ("name", ListenerApplicationName)
        );
        listeners
            .Should()
            .Be(
                1,
                "one listening session serves every subscriber on a host, the dashboard's and GraphQL's, per "
                    + Adr
            );

        await stop.CancelAsync();
        try
        {
            await dashboard.DisposeAsync();
        }
        catch (OperationCanceledException) { }
    }

    [Test]
    public async Task After_a_listener_reconnect_the_operator_feed_sends_a_reload()
    {
        await using var operatorWs = await GraphQLWs.ConnectAsync(_host, E2EHost.OperatorKey);
        await operatorWs.SubscribeAsync(
            "feed",
            $"subscription {{ onMachineInstanceChanged(machine: {WizardOps.Literal(WizardMachine.MachineId)}) {{ {MachineChangedOps.OperatorFields} reload }} }}"
        );
        // The feed is live once a write reaches it.
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await NextForAsync(operatorWs, started.Id);

        // The listener's session is cut: whatever is written until it listens again is never announced.
        await E2ESql.ExecuteAsync(
            Database,
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = @name "
                + "AND datname = current_database()",
            ("name", ListenerApplicationName)
        );

        // After the listener reconnects, writes made meanwhile were never announced, so the operator is told to read
        // the instances again, per the ADR: the wait ends at the reload, and NextDataAsync fails the test when none
        // arrives within its timeout.
        JsonElement change;
        do change = (await operatorWs.NextDataAsync("feed")).GetProperty(
            "onMachineInstanceChanged"
        );
        while (!change.GetProperty("reload").GetBoolean());
    }

    // The next operator event about instance `id`, skipping events about others.
    private static async Task<JsonElement> NextForAsync(GraphQLWs ws, Guid id)
    {
        while (true)
        {
            var change = (await ws.NextDataAsync("feed")).GetProperty("onMachineInstanceChanged");
            if (change.GetProperty("id").GetString() == id.ToString())
                return change;
        }
    }
}
