using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A run its starter queues on one node and another node executes still reaches the starter: two hosts over one
/// database and one broadcast transport. Node A serves GraphQL and runs nothing; node B has the scheduler, dispatches
/// the queue and runs the train. Everything Alice sees after QUEUED crossed the transport.
/// </summary>
/// <remarks>
/// The transport is <see cref="LoopbackBus"/>: each message goes through JSON bytes, as RabbitMQ carries it, and
/// reaches every started receiver in the order it was published, its own publisher's included, which the receiver
/// service drops by instance id.
/// <para>Enforces <c>Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md</c>.</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
[Property("adr", Adr)]
public class RunProgressCrossNodeE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    private const string Database = "trax_api_run_progress_cross_node";
    private const string AliceKey = "cross-node-alice-key-0123456789abcdefghi";
    private const string BobKey = "cross-node-bob-key-0123456789abcdefghijk";

    private const string Fields = "runId state problem { code }";
    private const string EventFields =
        "sequence state failure step { name state } problem { code }";

    private readonly LoopbackBus _bus = new();
    private LoopbackBus.Endpoint? _apiEndpoint;
    private LoopbackBus.Endpoint? _workerEndpoint;
    private IHost? _api;
    private IHost? _worker;

    [OneTimeSetUp]
    public async Task StartApiNode()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);
        _apiEndpoint = _bus.Join();
        _workerEndpoint = _bus.Join();

        // Node A: no scheduler, so nothing here dispatches or runs a train.
        _api = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AliceKey, id: "alice", "Player")
                                .Add(BobKey, id: "bob", "Player")
                        );
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects
                                        .UsePostgres(connectionString)
                                        .AddJunctionEvents()
                                        .UseBroadcaster(b => _apiEndpoint.NotNull().Register(b))
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseWebSockets();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();
        await _api.NotNull().StartAsync();

        // Node B: built now, started by the test once Alice has seen her run queued.
        _worker = BuildWorkerNode(connectionString);
    }

    private IHost BuildWorkerNode(string connectionString)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddTrax(trax =>
                    trax.AddEffects(effects =>
                            effects
                                .UsePostgres(connectionString)
                                .AddDecisionRecording()
                                .AddJunctionEvents()
                                .UseBroadcaster(b => _workerEndpoint.NotNull().Register(b))
                        )
                        // A worker takes no API submissions, so it authorizes nobody.
                        .AddMediator(mediator =>
                            mediator
                                .ScanAssemblies(typeof(AuthE2EHost).Assembly)
                                .AllowMissingAuthorizationService()
                        )
                        .AddScheduler(scheduler => scheduler)
                );
            })
            .Build();
    }

    [OneTimeTearDown]
    public async Task StopNodes()
    {
        ProgressMarkerTrain.Gate.TrySetResult();
        await _worker.NotNull().StopAsync();
        _worker?.Dispose();
        await _api.NotNull().StopAsync();
        _api?.Dispose();
        if (_workerEndpoint is not null)
            await _workerEndpoint.DisposeAsync();
        if (_apiEndpoint is not null)
            await _apiEndpoint.DisposeAsync();
    }

    [Test]
    public async Task A_run_dispatched_on_another_node_reaches_its_starter()
    {
        ProgressMarkerTrain.Reset(open: false);
        var runId = await QueueAsync(AliceKey);

        await using var alice = await RunProgressE2ETests.Socket.ConnectAsync(
            _api.NotNull(),
            AliceKey
        );
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        (await alice.NextAsync()).GetProperty("state").GetString().Should().Be("QUEUED");

        using (var bob = await ReadAsync(runId, BobKey))
            Answer(bob)
                .GetProperty("problem")
                .GetProperty("code")
                .GetString()
                .Should()
                .Be("NOT_FOUND", "only the caller who started a run may read it, per " + Adr);

        await _worker.NotNull().StartAsync();

        // The run waits at the gate on node B, so all of this arrived over the transport.
        var live = await ReadUntilInProgressWithStepAsync(alice);
        await ProgressMarkerTrain.Waiting.Task.WaitAsync(
            TimeSpan.FromSeconds(30),
            TimeProvider.System
        );

        ProgressMarkerTrain.Gate.TrySetResult();
        var rest = await alice.ReadUntilCompleteAsync();

        var events = live.Concat(rest).ToList();
        events
            .Select(e => e.GetProperty("sequence").GetInt64())
            .Should()
            .BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        events[^1]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("COMPLETED", "the run's end on node B reaches its starter on node A, per " + Adr);
        alice.Received.Should().NotContain(RunProgressE2ETests.Marker);

        _apiEndpoint
            .NotNull()
            .Published.Should()
            .NotContain(
                m => m.ExternalId == runId,
                "node A runs nothing, so none of the run's events can have come from it"
            );
        _workerEndpoint.NotNull().Published.Should().Contain(m => m.ExternalId == runId);
    }

    private static async Task<List<JsonElement>> ReadUntilInProgressWithStepAsync(
        RunProgressE2ETests.Socket alice
    )
    {
        var live = new List<JsonElement>();
        while (
            !live.Exists(e =>
                string.Equals(
                    e.GetProperty("state").GetString(),
                    "IN_PROGRESS",
                    StringComparison.Ordinal
                )
            ) || !live.Exists(e => e.GetProperty("step").ValueKind == JsonValueKind.Object)
        )
            live.Add(await alice.NextAsync().ConfigureAwait(false));
        return live;
    }

    #region Helpers

    private static JsonElement Answer(JsonDocument response)
    {
        response
            .RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        return response.RootElement.GetProperty("data").GetProperty("runProgress");
    }

    private Task<JsonDocument> ReadAsync(string runId, string key) =>
        _api.NotNull()
            .PostGraphQLAsync(
                $$"""{ runProgress(runId: "{{runId}}") { {{Fields}} } }""",
                r => r.Headers.Add("X-Api-Key", key)
            );

    private async Task<string> QueueAsync(string key)
    {
        using var response = await _api.NotNull()
            .PostGraphQLAsync(
                $$"""mutation { dispatch { progressMarker(input: { note: "{{RunProgressE2ETests.Marker}}", fail: false }, mode: QUEUE) { externalId } } }""",
                r => r.Headers.Add("X-Api-Key", key)
            )
            .ConfigureAwait(false);
        var data = response.RootElement.GetProperty("data");
        data.ValueKind.Should().NotBe(JsonValueKind.Null, response.RootElement.GetRawText());
        return data.GetProperty("dispatch")
            .GetProperty("progressMarker")
            .GetProperty("externalId")
            .GetString()
            .NotNull();
    }

    #endregion
}
