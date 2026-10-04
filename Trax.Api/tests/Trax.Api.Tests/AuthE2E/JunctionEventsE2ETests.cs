using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A run's steps, end to end: a real train on a host that called <c>AddJunctionEvents()</c>,
/// followed over a real WebSocket through <c>onJunctionEvent</c> and read back through
/// <c>operations.junctionRuns</c>, by callers with and without the right to see the run.
/// </summary>
/// <remarks>
/// The trains' input, intermediate output, final output and failure message all carry
/// <see cref="Marker"/>; it must appear in nothing any caller receives. One question is about a
/// <c>[TraxSensitive]</c> type, whose answer must stay absent everywhere.
///
/// <para>Enforces <c>docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md</c>.</para>
/// </remarks>
[Property("adr", "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md")]
[TestFixture]
[NonParallelizable]
public class JunctionEventsE2ETests
{
    private const string Adr =
        "docs/adr/0037-a-runs-step-feed-follows-one-run-with-its-trains-visibility.md";
    private const string Database = "trax_api_junction_events";
    private const string WsUri = "ws://localhost/trax/graphql";

    private const string AdminKey = "junction-admin-key";
    private const string PlayerKey = "junction-player-key";
    private const string GuestKey = "junction-guest-key";

    /// <summary>Carried by every input, output and failure message the trains handle.</summary>
    internal const string Marker = "S3CRET-MARKER-7f2c";

    private const string StepFields =
        "position kind name state startedAt endedAt durationMs failureClass failureException "
        + "questionKey answer confidence replayed decider answerWithheld attempt nameWithheld "
        + "trackPosition";

    private static string Subscription(long metadataId) =>
        $"subscription {{ onJunctionEvent(metadataId: {metadataId}) {{ sequence metadataId trainName eventType junction {{ {StepFields} }} }} }}";

    private static string TimelineQuery(long metadataId) =>
        $"{{ operations {{ junctionRuns(metadataId: {metadataId}) {{ {StepFields} }} }} }}";

    private static string ExecutionQuery(long metadataId) =>
        $"{{ operations {{ execution(id: {metadataId}) {{ id }} }} }}";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminKey, id: "admin", "Admin", "Player")
                                .Add(PlayerKey, id: "player", "Player")
                                .Add(GuestKey, id: "guest", "Guest")
                        );
                        services.AddAuthorization();

                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJunctionEvents()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Admin")
                        );

                        // The operations surface needs the scheduler's service at startup; nothing
                        // read here goes through it.
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
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

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    #region Live steps

    [Test]
    public async Task Admin_FollowsARunsSteps_WithoutItsDataAndWithTheSensitiveAnswerWithheld()
    {
        await using var admin = await Subscriber.ConnectAsync(_host, AdminKey);
        await using var run = await StartRunAsync<IHiddenMarkerTrain>(fail: true);
        (await admin.SubscribeAsync(Subscription(run.MetadataId))).Should().BeTrue();
        await admin.WaitUntilListeningAsync(() =>
            Probe(run.MetadataId, typeof(IHiddenMarkerTrain))
        );

        run.Release();
        var steps = await admin.ReadUntilAsync(IsLastStep);
        await run.Finished;

        var events = steps.Select(s => (Type: s.EventType, s.Name)).ToList();
        events
            .Should()
            .ContainInOrder(
                ("JUNCTION_COMPLETED", nameof(HoldForRelease)),
                ("DECIDED", "MarkerLane"),
                ("ROUTED", "MarkerLane"),
                ("DECIDED", "MarkerTier"),
                ("ROUTED", "MarkerTier"),
                ("JUNCTION_STARTED", Withheld),
                ("JUNCTION_FAILED", Withheld)
            );

        var lane = steps.First(s => s.EventType == "DECIDED" && s.Name == "MarkerLane");
        lane.Fields.GetProperty("answer").GetString().Should().Be("Fast");
        lane.Fields.GetProperty("confidence").GetDouble().Should().Be(0.8);
        lane.Fields.GetProperty("decider")
            .GetString()
            .Should()
            .Be(typeof(ScriptedDecider).FullName);

        foreach (var tier in steps.Where(s => s.Name == "MarkerTier"))
        {
            tier.Fields.GetProperty("answerWithheld").GetBoolean().Should().BeTrue();
            tier.Fields.GetProperty("answer")
                .ValueKind.Should()
                .Be(JsonValueKind.Null, "a [TraxSensitive] question's answer is never published");
            tier.Fields.GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var failed = steps.Last();
        failed
            .Fields.GetProperty("failureException")
            .GetString()
            .Should()
            .Be("InvalidOperationException");
        failed.Fields.GetProperty("failureClass").ValueKind.Should().NotBe(JsonValueKind.Null);

        admin
            .Received.Should()
            .NotContain(Marker, "a step carries no input, output or message, per " + Adr);
        admin.Received.Should().NotContain("\"Red\"", "the withheld answer appears nowhere");
        admin
            .Received.Should()
            .NotContain(nameof(RevealSecret), "a junction on a sensitive track is named to nobody")
            .And.NotContain(nameof(FinishOrExplode));
        steps
            .Should()
            .Contain(
                s => s.Name == nameof(PassLane),
                "the operations view sees the names on a track whose answer it may see"
            );
    }

    [Test]
    public async Task Player_ReceivesNoneOfTheStepsOfARunWhoseTrainItCannotSee()
    {
        await using var admin = await Subscriber.ConnectAsync(_host, AdminKey);
        await using var player = await Subscriber.ConnectAsync(_host, PlayerKey);
        await using var run = await StartRunAsync<IHiddenMarkerTrain>(fail: false);

        (await admin.SubscribeAsync(Subscription(run.MetadataId))).Should().BeTrue();
        (await player.SubscribeAsync(Subscription(run.MetadataId))).Should().BeTrue();
        // The probes name a train the player may see, so both are known to be listening.
        await admin.WaitUntilListeningAsync(() =>
            Probe(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );
        await player.WaitUntilListeningAsync(() =>
            Probe(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );

        run.Release();
        await admin.ReadUntilAsync(IsLastStep);
        await run.Finished;

        // Published after every real step, on the same topic: the player reads it next only if
        // it was handed none of the hidden train's steps.
        Probe(run.MetadataId, typeof(IBroadcastMarkerTrain), name: "barrier");
        var next = await player.NextStepAsync();
        while (next.Name == "probe")
            next = await player.NextStepAsync();
        next.Name.Should()
            .Be(
                "barrier",
                "a subscriber who could not see the train's events sees none of its steps, per "
                    + Adr
            );
    }

    [Test]
    public async Task Player_FollowsABroadcastRun_WithHostDetailWithheld()
    {
        await using var player = await Subscriber.ConnectAsync(_host, PlayerKey);
        await using var run = await StartRunAsync<IBroadcastMarkerTrain>(fail: true);
        (await player.SubscribeAsync(Subscription(run.MetadataId))).Should().BeTrue();
        await player.WaitUntilListeningAsync(() =>
            Probe(run.MetadataId, typeof(IBroadcastMarkerTrain))
        );

        run.Release();
        var steps = await player.ReadUntilAsync(IsLastStep);
        await run.Finished;

        steps.Should().Contain(s => s.Name == "MarkerLane" && s.EventType == "DECIDED");
        steps
            .Should()
            .OnlyContain(s =>
                s.Fields.GetProperty("decider").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("answer").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("confidence").ValueKind == JsonValueKind.Null
                && s.Fields.GetProperty("failureException").ValueKind == JsonValueKind.Null
            );
        player.Received.Should().NotContain(Marker);
        player
            .Received.Should()
            .NotContain(
                nameof(PassLane),
                "a junction on a track gives the answer away to a subscriber not shown it, per "
                    + Adr
            );
        steps
            .Where(s => s.Fields.GetProperty("trackPosition").ValueKind == JsonValueKind.Number)
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(s =>
                s.Name == Withheld && s.Fields.GetProperty("nameWithheld").GetBoolean()
            );
        player.Received.Should().NotContain("\"Red\"");
    }

    [Test]
    public async Task Guest_WhoCouldSeeNoTrain_IsRefused()
    {
        await using var guest = await Subscriber.ConnectAsync(_host, GuestKey);

        (await guest.SubscribeAsync(Subscription(1)))
            .Should()
            .BeFalse(
                "a subscriber who could receive nothing is refused when subscribing, per " + Adr
            );
    }

    #endregion

    #region The recorded timeline

    [Test]
    public async Task Timeline_IsReadableExactlyWhenTheRunsDetailIs()
    {
        await using var run = await StartRunAsync<IHiddenMarkerTrain>(fail: true);
        run.Release();
        await run.Finished;
        var timeline = await TimelineWhenCompleteAsync(run.MetadataId);
        timeline.Should().NotBeEmpty();

        foreach (var key in new[] { AdminKey, PlayerKey, GuestKey, null })
        {
            using var execution = await PostAsync(ExecutionQuery(run.MetadataId), key);
            using var steps = await PostAsync(TimelineQuery(run.MetadataId), key);

            Denied(steps)
                .Should()
                .Be(
                    Denied(execution),
                    $"the timeline is denied exactly when the run's detail is (caller {key ?? "anonymous"}), per {Adr}"
                );
            if (Denied(steps))
                steps.RootElement.GetRawText().Should().NotContain(nameof(HoldForRelease));
        }

        using var forAdmin = await PostAsync(TimelineQuery(run.MetadataId), AdminKey);
        Denied(forAdmin).Should().BeFalse();
    }

    [Test]
    public async Task Timeline_CarriesNoRunDataAndWithholdsTheSensitiveAnswer()
    {
        await using var run = await StartRunAsync<IBroadcastMarkerTrain>(fail: true);
        run.Release();
        await run.Finished;
        var timeline = await TimelineWhenCompleteAsync(run.MetadataId);

        timeline
            .Select(s => (s.GetProperty("kind").GetString(), s.GetProperty("name").GetString()))
            .Should()
            .Equal(
                ("JUNCTION", nameof(HoldForRelease)),
                ("CHOICE", "MarkerLane"),
                ("ROUTE", "MarkerLane"),
                ("JUNCTION", nameof(PassLane)),
                ("CHOICE", "MarkerTier"),
                ("ROUTE", "MarkerTier"),
                ("JUNCTION", Withheld),
                ("JUNCTION", Withheld)
            );
        timeline[^1].GetProperty("nameWithheld").GetBoolean().Should().BeTrue();
        timeline[^1].GetProperty("trackPosition").GetInt32().Should().Be(5);

        foreach (var tier in timeline.Where(s => s.GetProperty("name").GetString() == "MarkerTier"))
        {
            tier.GetProperty("answerWithheld").GetBoolean().Should().BeTrue();
            tier.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
            tier.GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
        }

        timeline[1].GetProperty("answer").GetString().Should().Be("Fast");
        timeline[^1].GetProperty("state").GetString().Should().Be("FAILED");
        timeline[^1].GetProperty("durationMs").ValueKind.Should().Be(JsonValueKind.Number);

        var raw = string.Join("", timeline.Select(s => s.GetRawText()));
        raw.Should()
            .NotContain(Marker, "a recorded step carries no input, output or message, per " + Adr);
        raw.Should().NotContain("\"Red\"");
    }

    [Test]
    public async Task Timeline_PagesByPosition()
    {
        await using var run = await StartRunAsync<IHiddenMarkerTrain>(fail: false);
        run.Release();
        await run.Finished;
        await TimelineWhenCompleteAsync(run.MetadataId);

        using var doc = await PostAsync(
            $"{{ operations {{ junctionRuns(metadataId: {run.MetadataId}, afterPosition: 2, take: 3) {{ position }} }} }}",
            AdminKey
        );

        doc.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("junctionRuns")
            .EnumerateArray()
            .Select(s => s.GetProperty("position").GetInt32())
            .Should()
            .Equal(3, 4, 5);
    }

    [TestCase(0L)]
    [TestCase(-5L)]
    public async Task Timeline_OfAnIdThatIsNotPositive_IsRefused(long metadataId)
    {
        using var doc = await PostAsync(TimelineQuery(metadataId), AdminKey);

        AuthOperations
            .HasErrorCode(doc, Trax.Api.GraphQL.Validation.RunIdArgument.ErrorCode)
            .Should()
            .BeTrue(
                "an id of 0 would read every unsaved run at once, per "
                    + Adr
                    + ": "
                    + doc.RootElement.GetRawText()
            );
    }

    [Test]
    public async Task Timeline_OverGet_IsRefusedLikeEveryOtherQuery()
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/trax/graphql?query=" + Uri.EscapeDataString(TimelineQuery(1))
        );
        request.Headers.Add("X-Api-Key", AdminKey);
        request.Headers.Add("GraphQL-preflight", "1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("junctionRuns\":");
    }

    #endregion

    #region Helpers

    private const string Withheld = JunctionStep.WithheldName;

    /// <summary>The run's last junction (position 7) ending; its name is withheld on every view.</summary>
    private static bool IsLastStep(Step step) =>
        step.Fields.GetProperty("position").GetInt32() == 7
        && step.EventType is "JUNCTION_FAILED" or "JUNCTION_COMPLETED";

    private static bool Denied(JsonDocument doc) =>
        AuthOperations.HasErrorCode(doc, "TRAX_AUTHORIZATION")
        || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHORIZED")
        || AuthOperations.HasErrorCode(doc, "AUTH_NOT_AUTHENTICATED");

    private async Task<List<JsonElement>> TimelineWhenCompleteAsync(long metadataId)
    {
        // The rows are written off the run's path and trail it by moments.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            using var doc = await PostAsync(TimelineQuery(metadataId), AdminKey);
            doc.RootElement.TryGetProperty("errors", out var errors)
                .Should()
                .BeFalse(errors.ValueKind == JsonValueKind.Undefined ? "" : errors.GetRawText());
            var steps = doc
                .RootElement.GetProperty("data")
                .GetProperty("operations")
                .GetProperty("junctionRuns")
                .EnumerateArray()
                .Select(s => s.Clone())
                .ToList();

            if (
                steps.Count > 0
                && steps[^1].GetProperty("position").GetInt32() == 7
                && steps[^1].GetProperty("state").GetString() != "IN_PROGRESS"
            )
                return steps;

            if (DateTime.UtcNow > deadline)
                Assert.Fail($"The timeline of run {metadataId} was not recorded in time.");

            // allowed-delay: polls the background writer, bounded by the 15s deadline above.
            await Task.Delay(100);
        }
    }

    private async Task<JsonDocument> PostAsync(string query, string? apiKey)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Publishes a made-up step of <paramref name="metadataId"/> through the host's own junction
    /// event handler, as the run's path does.
    /// </summary>
    private void Probe(long metadataId, Type train, string name = "probe")
    {
        var message = new TrainLifecycleEventMessage(
            MetadataId: metadataId,
            ExternalId: "probe",
            TrainName: train.FullName!,
            TrainState: nameof(TrainState.InProgress),
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: TrainLifecycleEventMessage.JunctionStartedEventType,
            Executor: null,
            Output: null
        )
        {
            Junction = new JunctionEventPayload(
                Position: 999,
                Kind: JunctionRunKind.Junction,
                Name: name,
                State: JunctionRunState.InProgress,
                StartedAt: DateTime.UtcNow
            ),
        };

        foreach (var handler in _host.Services.GetServices<IJunctionEventHandler>())
            handler.HandleAsync(message, default).GetAwaiter().GetResult();
    }

    private async Task<RunningTrain> StartRunAsync<TTrain>(bool fail)
        where TTrain : IServiceTrain<MarkerInput, string>
    {
        var key = Guid.NewGuid().ToString("N");
        var gate = MarkerGates.Hold(key);
        var scope = _host.Services.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<TTrain>();
        var finished = Task.Run(async () =>
        {
            try
            {
                await train.Run(new MarkerInput(key, Marker, fail));
            }
            catch (InvalidOperationException) when (fail)
            {
                // The failing run fails by design.
            }
        });

        var concrete = (ServiceTrain<MarkerInput, string>)(object)train;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (concrete.Metadata is not { Id: > 0 })
        {
            if (DateTime.UtcNow > deadline || finished.IsCompleted)
            {
                gate.TrySetResult();
                await finished;
                Assert.Fail("The run did not start in time.");
            }

            // allowed-delay: waits for the run to persist its metadata, bounded by the deadline.
            await Task.Delay(20);
        }

        return new RunningTrain(scope, gate, concrete.Metadata.Id, finished);
    }

    private sealed class RunningTrain(
        IServiceScope scope,
        TaskCompletionSource gate,
        long metadataId,
        Task finished
    ) : IAsyncDisposable
    {
        public long MetadataId => metadataId;

        public Task Finished => finished.WaitAsync(TimeSpan.FromSeconds(30));

        public void Release() => gate.TrySetResult();

        public async ValueTask DisposeAsync()
        {
            gate.TrySetResult();
            await finished.WaitAsync(TimeSpan.FromSeconds(30));
            scope.Dispose();
        }
    }

    internal sealed record Step(string EventType, string Name, JsonElement Fields);

    /// <summary>One graphql-transport-ws connection with one subscription.</summary>
    private sealed class Subscriber : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly StringBuilder _received = new();
        private Task<JsonElement>? _pending;

        private Subscriber(WebSocket socket) => _socket = socket;

        /// <summary>Every frame received, raw, for checks that nothing leaked.</summary>
        public string Received => _received.ToString();

        public static async Task<Subscriber> ConnectAsync(IHost host, string apiKey)
        {
            var client = host.GetTestServer().CreateWebSocketClient();
            client.SubProtocols.Add("graphql-transport-ws");
            var socket = await client.ConnectClosingAsync(new Uri(WsUri), CancellationToken.None);
            var subscriber = new Subscriber(socket);
            await subscriber.SendAsync(new { type = "connection_init", payload = new { apiKey } });
            var ack = await subscriber.ReceiveAsync();
            ack.GetProperty("type").GetString().Should().Be("connection_ack");
            return subscriber;
        }

        /// <summary>
        /// Starts the subscription. False when it is refused: an <c>error</c> frame, or a result
        /// carrying errors and no data.
        /// </summary>
        public async Task<bool> SubscribeAsync(string query)
        {
            await SendAsync(
                new
                {
                    id = "1",
                    type = "subscribe",
                    payload = new { query },
                }
            );

            // A refusal answers at once; an accepted subscription answers nothing until an event.
            var first = ReceiveAsync();
            var settled = await Task.WhenAny(
                first,
                // allowed-delay: how long a refusal has to arrive; an accepted subscription is
                // silent, so nothing faster can tell the two apart.
                Task.Delay(TimeSpan.FromMilliseconds(500))
            );
            if (settled != first)
            {
                _pending = first;
                return true;
            }

            var message = await first;
            if (message.GetProperty("type").GetString() == "error")
                return false;

            var payload = message.GetProperty("payload");
            if (payload.TryGetProperty("errors", out _))
                return false;

            _pending = Task.FromResult(message);
            return true;
        }

        /// <summary>Publishes probes until this subscriber receives one.</summary>
        public async Task WaitUntilListeningAsync(Action probe)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            var next = NextStepAsync();
            while (!next.IsCompleted && DateTime.UtcNow < deadline)
            {
                probe();
                // allowed-delay: re-probe interval, bounded by the deadline; WhenAny wakes on receipt.
                await Task.WhenAny(next, Task.Delay(100));
            }

            next.IsCompleted.Should().BeTrue("the subscription should start listening");
            (await next).Name.Should().Be("probe");
        }

        /// <summary>Reads real steps, skipping probes, until <paramref name="last"/> matches.</summary>
        public async Task<List<Step>> ReadUntilAsync(Func<Step, bool> last)
        {
            var steps = new List<Step>();
            while (true)
            {
                var step = await NextStepAsync();
                if (step.Name == "probe")
                    continue;

                steps.Add(step);
                if (last(step))
                    return steps;
            }
        }

        public async Task<Step> NextStepAsync()
        {
            while (true)
            {
                var message = await (_pending ?? ReceiveAsync());
                _pending = null;
                var type = message.GetProperty("type").GetString();
                if (type is "ping" or "pong")
                    continue;

                type.Should().Be("next", message.GetRawText());
                var payload = message.GetProperty("payload");
                payload.TryGetProperty("errors", out _).Should().BeFalse(payload.GetRawText());
                var e = payload.GetProperty("data").GetProperty("onJunctionEvent");
                var step = e.GetProperty("junction");
                return new Step(
                    e.GetProperty("eventType").GetString()!,
                    step.GetProperty("name").GetString()!,
                    step.Clone()
                );
            }
        }

        private async Task SendAsync(object message)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }

        private async Task<JsonElement> ReceiveAsync()
        {
            var buffer = new byte[8192];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException(
                        $"The socket closed: {result.CloseStatus} {result.CloseStatusDescription}"
                    );

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage && ms.Length > 0)
                    break;
            }

            var text = Encoding.UTF8.GetString(ms.ToArray());
            lock (_received)
                _received.Append(text).Append('\n');
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}

/// <summary>Error-code checks shared with the operations authorization tests.</summary>
internal static class AuthOperations
{
    public static bool HasErrorCode(JsonDocument doc, string code) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors
            .EnumerateArray()
            .Any(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var c)
                && c.GetString() == code
            );
}

#region Trains whose input, output and failure carry a secret

/// <summary>Holds a run at its first junction until the test releases it.</summary>
internal static class MarkerGates
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new();

    public static TaskCompletionSource Hold(string key) =>
        Gates.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    public static Task Wait(string key) =>
        Gates.TryGetValue(key, out var gate) ? gate.Task : Task.CompletedTask;
}

public sealed record MarkerInput(string Key, string Secret, bool Fail);

[Asks("Which lane does this order take?")]
public enum MarkerLane
{
    Fast,
    Slow,
}

[TraxSensitive]
[Asks("Which risk tier is this customer in?")]
public enum MarkerTier
{
    Green,
    Red,
}

public class HoldForRelease : EffectJunction<MarkerInput, MarkerInput>
{
    public override async Task<MarkerInput> Run(MarkerInput input)
    {
        await MarkerGates.Wait(input.Key).WaitAsync(TimeSpan.FromSeconds(30));
        return input;
    }
}

public class PassLane : EffectJunction<MarkerInput, MarkerInput>
{
    public override Task<MarkerInput> Run(MarkerInput input) => Task.FromResult(input);
}

/// <summary>Its output carries the secret.</summary>
public class RevealSecret : EffectJunction<MarkerInput, string>
{
    public override Task<string> Run(MarkerInput input) =>
        Task.FromResult($"{input.Secret}|{(input.Fail ? "fail" : "ok")}");
}

/// <summary>Fails with the secret in its message, or returns it as the train's output.</summary>
public class FinishOrExplode : EffectJunction<string, string>
{
    public override Task<string> Run(string input) =>
        input.EndsWith("|fail", StringComparison.Ordinal)
            ? throw new InvalidOperationException($"exploded while holding {input}")
            : Task.FromResult(input);
}

public abstract class MarkerTrainBase : ServiceTrain<MarkerInput, string>
{
    // Carried by the train rather than registered, so the other hosts that scan this assembly do
    // not need a decider of their own.
    private static readonly IDecider Decider = new ScriptedDecider()
        .Choose(MarkerLane.Fast, 0.8)
        .Choose(MarkerTier.Red, 0.9);

    protected override Task<Either<Exception, string>> Junctions() =>
        AddServices(Decider)
            .Chain<HoldForRelease>()
            .Switch<MarkerInput, MarkerLane>(tracks =>
                tracks
                    .When(MarkerLane.Fast, t => t.Chain<PassLane>())
                    .When(MarkerLane.Slow, t => t.Chain<PassLane>())
            )
            .Switch<MarkerInput, MarkerTier>(tracks =>
                tracks
                    .When(MarkerTier.Green, t => t.Chain<RevealSecret>())
                    .When(MarkerTier.Red, t => t.Chain<RevealSecret>())
            )
            .Chain<FinishOrExplode>()
            .Resolve();
}

public interface IBroadcastMarkerTrain : IServiceTrain<MarkerInput, string>;

/// <summary>Broadcast to players.</summary>
[TraxBroadcast]
[TraxAuthorize(Roles = "Player")]
public class BroadcastMarkerTrain : MarkerTrainBase, IBroadcastMarkerTrain;

public interface IHiddenMarkerTrain : IServiceTrain<MarkerInput, string>;

/// <summary>Not broadcast: only the operations view sees it.</summary>
[TraxAuthorize(Roles = "Admin")]
public class HiddenMarkerTrain : MarkerTrainBase, IHiddenMarkerTrain;

#endregion
