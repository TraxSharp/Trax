using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A run followed by the caller who started it, end to end: a real train marked <c>[TraxRunProgress]</c>, started over
/// HTTP by one caller, read through <c>query { runProgress }</c> and followed over a real WebSocket through
/// <c>subscription { runProgress }</c>, by its starter and by another caller who may run the same train.
/// </summary>
/// <remarks>
/// The train's input, output and failure carry <see cref="Marker"/>; it reaches nobody.
/// <para>Enforces <c>Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md</c>.</para>
/// </remarks>
[TestFixture]
[NonParallelizable]
[Property("adr", Adr)]
public class RunProgressE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    private const string Database = "trax_api_run_progress";
    private const string WsUri = "ws://localhost/trax/graphql";
    private const string AliceKey = "progress-alice-key";
    private const string BobKey = "progress-bob-key";
    internal const string Marker = "PROGRESS-MARKER-41d9";

    private const string Fields =
        "runId train state failure retriedAs steps { name state } problem { code }";
    private const string EventFields =
        "sequence state failure step { name state } problem { code }";

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
                            keys.Add(AliceKey, id: "alice", "Player")
                                .Add(BobKey, id: "bob", "Player")
                        );
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJunctionEvents()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL();
                        services.AddHttpContextAccessor();
                        services.AddScoped<ISnapshotPrincipal, HeaderUserKey>();
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
        ProgressMarkerTrain.Gate.TrySetResult();
        await _host.StopAsync();
        _host.Dispose();
    }

    [SetUp]
    public void OpenGate() => ProgressMarkerTrain.Reset(open: true);

    #region The first read

    [Test]
    public async Task The_starter_reads_a_queued_run_and_another_caller_finds_nothing()
    {
        var runId = await QueueAsync(AliceKey);

        using var alice = await ReadAsync(runId, AliceKey);
        using var bob = await ReadAsync(runId, BobKey);

        Answer(alice).GetProperty("state").GetString().Should().Be("QUEUED");
        Answer(alice).GetProperty("train").GetString().Should().Be("progressMarker");
        Answer(alice).GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        Answer(bob)
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("NOT_FOUND", "only the caller who started a run may read it, per " + Adr);
        Answer(bob).GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task A_missing_run_and_a_foreign_run_answer_alike()
    {
        var runId = await RunAsync(AliceKey, fail: false);

        using var foreign = await ReadAsync(runId, BobKey);
        using var missing = await ReadAsync(new string('0', 32), BobKey);

        Answer(foreign)
            .GetRawText()
            .Replace(runId, "ID")
            .Should()
            .Be(
                Answer(missing).GetRawText().Replace(new string('0', 32), "ID"),
                "a foreign run and a missing one answer alike, per " + Adr
            );
    }

    [Test]
    public async Task A_finished_run_lists_its_steps_and_no_data()
    {
        var runId = await RunAsync(AliceKey, fail: false);

        using var alice = await ReadWithStepsAsync(runId, AliceKey);

        Answer(alice).GetProperty("state").GetString().Should().Be("COMPLETED");
        Answer(alice)
            .GetProperty("steps")
            .EnumerateArray()
            .Select(s => s.GetProperty("name").GetString())
            .Should()
            .Contain(nameof(WaitForGate));
        alice.RootElement.GetRawText().Should().NotContain(Marker);
    }

    [Test]
    public async Task A_failure_that_is_not_a_TrainException_carries_no_message()
    {
        var runId = await RunAsync(AliceKey, fail: true);

        using var alice = await ReadAsync(runId, AliceKey);

        Answer(alice).GetProperty("state").GetString().Should().Be("FAILED");
        Answer(alice).GetProperty("failure").ValueKind.Should().Be(JsonValueKind.Null);
        alice.RootElement.GetRawText().Should().NotContain(Marker);
    }

    [Test]
    public async Task A_user_key_equal_to_another_callers_principal_id_does_not_reach_their_run()
    {
        var runId = await RunAsync(AliceKey, fail: false);
        var alicePrincipalId = TraxPrincipalId.Qualify(ApiKeyDefaults.SchemeName, "alice");

        using var bob = await _host.PostGraphQLAsync(
            $$"""{ runProgress(runId: "{{runId}}") { {{Fields}} } }""",
            r =>
            {
                r.Headers.Add("X-Api-Key", BobKey);
                r.Headers.Add(HeaderUserKey.Header, alicePrincipalId);
            }
        );

        Answer(bob)
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(
                "NOT_FOUND",
                "a machine user key is hashed apart from a principal id, so a key that reads like "
                    + "alice's id is not alice, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        using var anonymous = await _host.PostGraphQLAsync(
            $$"""{ runProgress(runId: "{{new string('0', 32)}}") { {{Fields}} } }"""
        );

        anonymous.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
    }

    #endregion

    #region The stream

    [Test]
    public async Task The_starter_follows_the_run_live_until_it_ends()
    {
        ProgressMarkerTrain.Reset(open: false);
        var running = RunAsync(AliceKey, fail: false);
        var runId = await RunningIdAsync();

        await using var alice = await Socket.ConnectAsync(_host, AliceKey);
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        var first = await alice.NextAsync();
        first.GetProperty("state").GetString().Should().Be("IN_PROGRESS");

        ProgressMarkerTrain.Gate.TrySetResult();
        await running;
        var events = await alice.ReadUntilCompleteAsync();

        events
            .Select(e => e.GetProperty("sequence").GetInt64())
            .Should()
            .BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        events.Last().GetProperty("state").GetString().Should().Be("COMPLETED");
        events.Should().Contain(e => e.GetProperty("step").ValueKind == JsonValueKind.Object);
        alice.Received.Should().NotContain(Marker);
    }

    [Test]
    public async Task A_run_followed_after_it_ended_answers_its_end_and_closes()
    {
        var runId = await RunAsync(AliceKey, fail: false);

        await using var alice = await Socket.ConnectAsync(_host, AliceKey);
        await alice.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        var events = await alice.ReadUntilCompleteAsync();

        events
            .Should()
            .ContainSingle()
            .Which.GetProperty("state")
            .GetString()
            .Should()
            .Be("COMPLETED");
    }

    [Test]
    public async Task A_followed_queued_run_that_is_cancelled_before_dispatch_ends_the_stream()
    {
        var runId = await QueueAsync(AliceKey);
        var poll = RunProgressStream.QueuedPoll;
        RunProgressStream.QueuedPoll = TimeSpan.FromMilliseconds(100);
        try
        {
            await using var alice = await Socket.ConnectAsync(_host, AliceKey);
            await alice.SubscribeAsync(
                $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
            );
            (await alice.NextAsync()).GetProperty("state").GetString().Should().Be("QUEUED");

            // As leaving an invoking state or the operations surface cancels it: the entry, and no lifecycle event.
            await using (
                var db = await _host
                    .Services.GetRequiredService<IDataContextProviderFactory>()
                    .CreateDbContextAsync(CancellationToken.None)
            )
                await db
                    .WorkQueues.Where(w => w.ExternalId == runId)
                    .ExecuteUpdateAsync(u =>
                        u.SetProperty(w => w.Status, WorkQueueStatus.Cancelled)
                    );
            var events = await alice.ReadUntilCompleteAsync();

            events
                .Should()
                .ContainSingle()
                .Which.GetProperty("state")
                .GetString()
                .Should()
                .Be("CANCELLED", "a run cancelled before dispatch ends its stream, per " + Adr);
        }
        finally
        {
            RunProgressStream.QueuedPoll = poll;
        }
    }

    [Test]
    public async Task Another_caller_is_told_not_found_and_the_stream_closes()
    {
        ProgressMarkerTrain.Reset(open: false);
        var running = RunAsync(AliceKey, fail: false);
        var runId = await RunningIdAsync();

        await using var bob = await Socket.ConnectAsync(_host, BobKey);
        await bob.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{runId}}") { {{EventFields}} } }"""
        );
        var events = await bob.ReadUntilCompleteAsync();
        ProgressMarkerTrain.Gate.TrySetResult();
        await running;

        events
            .Should()
            .ContainSingle()
            .Which.GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("NOT_FOUND");
        bob.Received.Should().NotContain(nameof(WaitForGate));
    }

    #region Retries

    [Test]
    public async Task A_failed_run_of_a_manifest_names_the_run_that_retries_it()
    {
        var failed = await RunAsync(AliceKey, fail: true);
        var retry = await RunAsync(AliceKey, fail: false);
        await OfOneManifestAsync(failed, retry);

        using var alice = await ReadAsync(failed, AliceKey);
        Answer(alice)
            .GetProperty("retriedAs")
            .GetString()
            .Should()
            .Be(retry, "the starter follows a manifest's retries run by run, per " + Adr);

        await using var socket = await Socket.ConnectAsync(_host, AliceKey);
        await socket.SubscribeAsync(
            $$"""subscription { runProgress(runId: "{{failed}}") { sequence state retriedAs } }"""
        );
        var events = await socket.ReadUntilCompleteAsync();

        events.First().GetProperty("state").GetString().Should().Be("FAILED");
        events.Last().GetProperty("retriedAs").GetString().Should().Be(retry);
    }

    [Test]
    public async Task Another_callers_retry_is_not_named()
    {
        var failed = await RunAsync(AliceKey, fail: true);
        var theirs = await RunAsync(BobKey, fail: false);
        await OfOneManifestAsync(failed, theirs);

        using var alice = await ReadAsync(failed, AliceKey);

        Answer(alice).GetProperty("retriedAs").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // Makes the runs occurrences of one manifest, in order, as the scheduler's retries are.
    private async Task OfOneManifestAsync(params string[] runIds)
    {
        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        var group = new ManifestGroup
        {
            Name = $"progress-{Guid.NewGuid():N}",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await db.Track(group);
        await db.SaveChanges(default);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IProgressMarkerTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);
        foreach (var runId in runIds)
            await db
                .Metadatas.Where(m => m.ExternalId == runId)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.ManifestId, manifest.Id));
    }

    #endregion

    #endregion

    #region Helpers

    private static JsonElement Answer(JsonDocument response)
    {
        response
            .RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeFalse(errors.ToString());
        return response.RootElement.GetProperty("data").GetProperty("runProgress");
    }

    // The steps are written to the database after the run, in the background; read until they are there.
    private async Task<JsonDocument> ReadWithStepsAsync(string runId, string key)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var response = await ReadAsync(runId, key);
            if (
                Answer(response).GetProperty("steps").GetArrayLength() >= 2
                || DateTime.UtcNow > deadline
            )
                return response;
            response.Dispose();
            // allowed-delay: polls the background step writer, bounded by the deadline.
            await Task.Delay(50);
        }
    }

    private Task<JsonDocument> ReadAsync(string runId, string key) =>
        _host.PostGraphQLAsync(
            $$"""{ runProgress(runId: "{{runId}}") { {{Fields}} } }""",
            r => r.Headers.Add("X-Api-Key", key)
        );

    private async Task<string> QueueAsync(string key)
    {
        using var response = await _host.PostGraphQLAsync(
            $$"""mutation { dispatch { progressMarker(input: { note: "{{Marker}}", fail: false }, mode: QUEUE) { externalId } } }""",
            r => r.Headers.Add("X-Api-Key", key)
        );
        return Dispatched(response);
    }

    private async Task<string> RunAsync(string key, bool fail)
    {
        using var response = await _host.PostGraphQLAsync(
            $$"""mutation { dispatch { progressMarker(input: { note: "{{Marker}}", fail: {{(fail ? "true" : "false")}} }, mode: RUN) { externalId } } }""",
            r => r.Headers.Add("X-Api-Key", key)
        );
        if (!fail)
            return Dispatched(response);

        // A failed run answers an error rather than its handle; its id is on its row.
        using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.Name == typeof(IProgressMarkerTrain).FullName && m.TrainState == TrainState.Failed
            )
            .OrderByDescending(m => m.Id)
            .Select(m => m.ExternalId)
            .FirstAsync();
    }

    private static string Dispatched(JsonDocument response)
    {
        var data = response.RootElement.GetProperty("data");
        data.ValueKind.Should().NotBe(JsonValueKind.Null, response.RootElement.GetRawText());
        return data.GetProperty("dispatch")
            .GetProperty("progressMarker")
            .GetProperty("externalId")
            .GetString()!;
    }

    // The external id of the run waiting at the gate, read from its row.
    private async Task<string> RunningIdAsync()
    {
        await ProgressMarkerTrain.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.Name == typeof(IProgressMarkerTrain).FullName
                && m.TrainState == TrainState.InProgress
            )
            .OrderByDescending(m => m.Id)
            .Select(m => m.ExternalId)
            .FirstAsync();
    }

    // A host whose machine user key is whatever the request says, as a host that maps a raw claim would be.
    private sealed class HeaderUserKey(IHttpContextAccessor http) : ISnapshotPrincipal
    {
        internal const string Header = "X-User-Key";

        public string? CurrentUserKey =>
            http.HttpContext?.Request.Headers[Header].FirstOrDefault() is { Length: > 0 } key
                ? key
                : null;
    }

    /// <summary>One graphql-transport-ws connection with one runProgress subscription.</summary>
    internal sealed class Socket : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly StringBuilder _received = new();

        private Socket(WebSocket socket) => _socket = socket;

        public string Received => _received.ToString();

        public static async Task<Socket> ConnectAsync(IHost host, string apiKey)
        {
            var client = host.GetTestServer().CreateWebSocketClient();
            client.SubProtocols.Add("graphql-transport-ws");
            var socket = await client.ConnectAsync(new Uri(WsUri), CancellationToken.None);
            var connected = new Socket(socket);
            await connected.SendAsync(new { type = "connection_init", payload = new { apiKey } });
            (await connected.ReceiveAsync())
                .GetProperty("type")
                .GetString()
                .Should()
                .Be("connection_ack");
            return connected;
        }

        public Task SubscribeAsync(string query) =>
            SendAsync(
                new
                {
                    id = "1",
                    type = "subscribe",
                    payload = new { query },
                }
            );

        /// <summary>The next event of the stream.</summary>
        public async Task<JsonElement> NextAsync()
        {
            while (true)
            {
                var message = await ReceiveAsync();
                var type = message.GetProperty("type").GetString();
                if (type is "ping" or "pong")
                    continue;
                type.Should().Be("next", message.GetRawText());
                var payload = message.GetProperty("payload");
                payload.TryGetProperty("errors", out _).Should().BeFalse(payload.GetRawText());
                return payload.GetProperty("data").GetProperty("runProgress").Clone();
            }
        }

        /// <summary>Every event until the server completes the stream.</summary>
        public async Task<List<JsonElement>> ReadUntilCompleteAsync()
        {
            var events = new List<JsonElement>();
            while (true)
            {
                var message = await ReceiveAsync();
                var type = message.GetProperty("type").GetString();
                if (type is "ping" or "pong")
                    continue;
                if (type == "complete")
                    return events;
                type.Should().Be("next", message.GetRawText());
                events.Add(
                    message
                        .GetProperty("payload")
                        .GetProperty("data")
                        .GetProperty("runProgress")
                        .Clone()
                );
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
                }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            }
            _socket.Dispose();
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
            _received.Append(text).Append('\n');
            return JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    #endregion
}

public record ProgressMarkerInput : IManifestProperties
{
    public required string Note { get; init; }
    public bool Fail { get; init; }
}

public record ProgressMarkerOutput
{
    public required string Echo { get; init; }
}

public interface IProgressMarkerTrain : IServiceTrain<ProgressMarkerInput, ProgressMarkerOutput>;

/// <summary>Waits at <see cref="Gate"/>, then answers its note back or throws it.</summary>
[TraxMutation]
[TraxRunProgress]
[TraxAuthorize]
public class ProgressMarkerTrain
    : ServiceTrain<ProgressMarkerInput, ProgressMarkerOutput>,
        IProgressMarkerTrain
{
    internal static TaskCompletionSource Gate { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static TaskCompletionSource Waiting { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Reset(bool open)
    {
        Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (open)
            Gate.TrySetResult();
    }

    protected override Task<Either<Exception, ProgressMarkerOutput>> Junctions() =>
        Chain<WaitForGate>().Chain<EchoOrThrow>().Resolve();
}

public class WaitForGate : EffectJunction<ProgressMarkerInput, ProgressMarkerInput>
{
    public override async Task<ProgressMarkerInput> Run(ProgressMarkerInput input)
    {
        ProgressMarkerTrain.Waiting.TrySetResult();
        await ProgressMarkerTrain.Gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return input;
    }
}

public class EchoOrThrow : EffectJunction<ProgressMarkerInput, ProgressMarkerOutput>
{
    public override Task<ProgressMarkerOutput> Run(ProgressMarkerInput input) =>
        input.Fail
            ? throw new InvalidOperationException(input.Note)
            : Task.FromResult(new ProgressMarkerOutput { Echo = input.Note });
}
