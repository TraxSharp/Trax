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
    private const string AliceKey = "cross-node-alice-key";
    private const string BobKey = "cross-node-bob-key";

    private const string Fields = "runId state problem { code }";
    private const string EventFields =
        "sequence state failure step { name state } problem { code }";

    private readonly LoopbackBus _bus = new();
    private LoopbackBus.Endpoint _apiEndpoint = null!;
    private LoopbackBus.Endpoint _workerEndpoint = null!;
    private IHost _api = null!;
    private IHost _worker = null!;

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
                                        .UseBroadcaster(b => _apiEndpoint.Register(b))
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
        await _api.StartAsync();

        // Node B: built now, started by the test once Alice has seen her run queued.
        _worker = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddTrax(trax =>
                    trax.AddEffects(effects =>
                            effects
                                .UsePostgres(connectionString)
                                .AddDecisionRecording()
                                .AddJunctionEvents()
                                .UseBroadcaster(b => _workerEndpoint.Register(b))
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
        await _worker.StopAsync();
        _worker.Dispose();
        await _api.StopAsync();
        _api.Dispose();
        await _workerEndpoint.DisposeAsync();
        await _apiEndpoint.DisposeAsync();
    }

    [Test]
    public async Task A_run_dispatched_on_another_node_reaches_its_starter()
    {
        ProgressMarkerTrain.Reset(open: false);
        var runId = await QueueAsync(AliceKey);

        await using var alice = await RunProgressE2ETests.Socket.ConnectAsync(_api, AliceKey);
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

        await _worker.StartAsync();

        // The run waits at the gate on node B, so all of this arrived over the transport.
        var live = new List<JsonElement>();
        while (
            !live.Any(e => e.GetProperty("state").GetString() == "IN_PROGRESS")
            || !live.Any(e => e.GetProperty("step").ValueKind == JsonValueKind.Object)
        )
            live.Add(await alice.NextAsync());
        await ProgressMarkerTrain.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));

        ProgressMarkerTrain.Gate.TrySetResult();
        var rest = await alice.ReadUntilCompleteAsync();

        var events = live.Concat(rest).ToList();
        events
            .Select(e => e.GetProperty("sequence").GetInt64())
            .Should()
            .BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        events
            .Last()
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("COMPLETED", "the run's end on node B reaches its starter on node A, per " + Adr);
        alice.Received.Should().NotContain(RunProgressE2ETests.Marker);

        _apiEndpoint
            .Published.Should()
            .NotContain(
                m => m.ExternalId == runId,
                "node A runs nothing, so none of the run's events can have come from it"
            );
        _workerEndpoint.Published.Should().Contain(m => m.ExternalId == runId);
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
        _api.PostGraphQLAsync(
            $$"""{ runProgress(runId: "{{runId}}") { {{Fields}} } }""",
            r => r.Headers.Add("X-Api-Key", key)
        );

    private async Task<string> QueueAsync(string key)
    {
        using var response = await _api.PostGraphQLAsync(
            $$"""mutation { dispatch { progressMarker(input: { note: "{{RunProgressE2ETests.Marker}}", fail: false }, mode: QUEUE) { externalId } } }""",
            r => r.Headers.Add("X-Api-Key", key)
        );
        var data = response.RootElement.GetProperty("data");
        data.ValueKind.Should().NotBe(JsonValueKind.Null, response.RootElement.GetRawText());
        return data.GetProperty("dispatch")
            .GetProperty("progressMarker")
            .GetProperty("externalId")
            .GetString()!;
    }

    #endregion
}

/// <summary>
/// An in-process broadcast transport for hosts that share one process. Each host joins as an <see cref="Endpoint"/>,
/// which is both its broadcaster and its receiver. A published message is serialized to JSON bytes and handed, in
/// publish order, to every endpoint whose receiver has started, the publisher's own included.
/// </summary>
internal sealed class LoopbackBus
{
    private readonly Lock _gate = new();
    private readonly List<Endpoint> _started = [];

    public Endpoint Join() => new(this);

    private void Publish(byte[] body)
    {
        lock (_gate)
            foreach (var endpoint in _started)
                endpoint.Enqueue(body);
    }

    private void Start(Endpoint endpoint)
    {
        lock (_gate)
            _started.Add(endpoint);
    }

    private void Stop(Endpoint endpoint)
    {
        lock (_gate)
            _started.Remove(endpoint);
    }

    /// <summary>One host's side of the bus.</summary>
    internal sealed class Endpoint(LoopbackBus bus) : ITrainEventBroadcaster, ITrainEventReceiver
    {
        private readonly ConcurrentQueue<TrainLifecycleEventMessage> _published = new();
        private Channel<byte[]>? _inbox;
        private Task? _pump;

        /// <summary>Every message this host published.</summary>
        public IReadOnlyCollection<TrainLifecycleEventMessage> Published => _published.ToArray();

        /// <summary>Registers this endpoint as the host's transport, as <c>UseRabbitMq</c> registers its own.</summary>
        public BroadcasterBuilder Register(BroadcasterBuilder builder)
        {
            builder.ServiceCollection.AddSingleton<ITrainEventBroadcaster>(this);
            builder.ServiceCollection.AddSingleton<ITrainEventReceiver>(this);
            return builder;
        }

        public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
        {
            _published.Enqueue(message);
            bus.Publish(JsonSerializer.SerializeToUtf8Bytes(message));
            return Task.CompletedTask;
        }

        public Task StartAsync(
            Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
            CancellationToken ct
        )
        {
            var inbox = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions { SingleReader = true }
            );
            _inbox = inbox;
            _pump = Task.Run(async () =>
            {
                await foreach (var body in inbox.Reader.ReadAllAsync())
                {
                    var message = JsonSerializer.Deserialize<TrainLifecycleEventMessage>(body)!;
                    await handler(message, CancellationToken.None);
                }
            });
            bus.Start(this);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct)
        {
            bus.Stop(this);
            _inbox?.Writer.TryComplete();
            if (_pump is not null)
                await _pump.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

        internal void Enqueue(byte[] body) => _inbox?.Writer.TryWrite(body);
    }
}
