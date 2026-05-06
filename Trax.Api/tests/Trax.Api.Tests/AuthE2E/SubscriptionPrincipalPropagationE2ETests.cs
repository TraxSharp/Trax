using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Verifies that authentication established during the graphql-transport-ws
/// handshake correctly propagates into subscription resolvers, and that
/// concurrent subscribers with different principals stay isolated.
///
/// The <c>whoAmI</c> subscription in <see cref="TestSubscriptions"/> reads
/// the authenticated principal from <c>IHttpContextAccessor.HttpContext.User</c>
/// and returns its trax principal-id whenever the <c>pokeWhoAmI</c> mutation
/// broadcasts. Every subscriber receives the same event, but each subscriber
/// resolves against its own socket's <c>HttpContext</c> — so the returned id
/// must match the subscriber, never the poker or another subscriber.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SubscriptionPrincipalPropagationE2ETests
{
    private const string Database = "trax_api_auth_subprop";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    private const string WsUri = "ws://localhost/trax/graphql";

    private const string WhoAmISubscription = """
        subscription { whoAmI }
        """;

    private const string PokeMutation = """
        mutation Poke($tag: String!) { pokeWhoAmI(tag: $tag) }
        """;

    // ── Single subscriber, both schemes ─────────────────────────────────

    [Test]
    public async Task ApiKey_Subscriber_ReceivesOwnPrincipalId()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        await InitAsync(ws, new { authToken = AdminApiKey });
        await ExpectAckAsync(ws);
        await SubscribeAsync(ws, "sub-1", WhoAmISubscription);

        await PokeAsync(host, "ping", AdminApiKey, scheme: Schemes.ApiKey);
        var payload = await ReceiveNextAsync(ws, "sub-1");

        payload.GetString().Should().Be("admin");
    }

    [Test]
    public async Task Jwt_Subscriber_ReceivesOwnPrincipalId()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var ws = await ConnectAsync(host);
        await InitAsync(ws, new { authToken = token });
        await ExpectAckAsync(ws);
        await SubscribeAsync(ws, "sub-1", WhoAmISubscription);

        await PokeAsync(host, "ping", token, scheme: Schemes.Jwt);
        var payload = await ReceiveNextAsync(ws, "sub-1");

        payload.GetString().Should().Be("alice");
    }

    // ── Subscriber identity is independent of poker identity ────────────

    [Test]
    public async Task Subscriber_IdentityIsIndependentOf_PokerIdentity()
    {
        // Subscriber is admin. Poker is player. Subscriber must see "admin".
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        await InitAsync(ws, new { authToken = AdminApiKey });
        await ExpectAckAsync(ws);
        await SubscribeAsync(ws, "sub-1", WhoAmISubscription);

        await PokeAsync(host, "ping", PlayerApiKey, scheme: Schemes.ApiKey);
        var payload = await ReceiveNextAsync(ws, "sub-1");

        payload.GetString().Should().Be("admin");
    }

    // ── Concurrent subscribers with different principals ────────────────

    [Test]
    public async Task TwoConcurrentSubscribers_EachReceiveOwnPrincipalId()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var wsAdmin = await ConnectAsync(host);
        await InitAsync(wsAdmin, new { authToken = AdminApiKey });
        await ExpectAckAsync(wsAdmin);
        await SubscribeAsync(wsAdmin, "sub-admin", WhoAmISubscription);

        using var wsPlayer = await ConnectAsync(host);
        await InitAsync(wsPlayer, new { authToken = PlayerApiKey });
        await ExpectAckAsync(wsPlayer);
        await SubscribeAsync(wsPlayer, "sub-player", WhoAmISubscription);

        await PokeAsync(host, "ping", AdminApiKey, scheme: Schemes.ApiKey);

        var adminPayload = await ReceiveNextAsync(wsAdmin, "sub-admin");
        var playerPayload = await ReceiveNextAsync(wsPlayer, "sub-player");

        adminPayload.GetString().Should().Be("admin");
        playerPayload.GetString().Should().Be("player");
    }

    [Test]
    public async Task TwoConcurrentSubscribers_AcrossSchemes_StayIsolated()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var jwtToken = SignJwt("jwt-user", "JwtUser", "Player");

        // API-key subscriber is rejected by the last-registered socket
        // interceptor (JWT) — documented limitation in Phase 3. Use JWT on
        // both sides so both connect successfully.
        var token1 = SignJwt("sub-one", "One", "Player");
        using var ws1 = await ConnectAsync(host);
        await InitAsync(ws1, new { authToken = token1 });
        await ExpectAckAsync(ws1);
        await SubscribeAsync(ws1, "s1", WhoAmISubscription);

        var token2 = SignJwt("sub-two", "Two", "Admin");
        using var ws2 = await ConnectAsync(host);
        await InitAsync(ws2, new { authToken = token2 });
        await ExpectAckAsync(ws2);
        await SubscribeAsync(ws2, "s2", WhoAmISubscription);

        await PokeAsync(host, "ping", jwtToken, scheme: Schemes.Jwt);

        var one = await ReceiveNextAsync(ws1, "s1");
        var two = await ReceiveNextAsync(ws2, "s2");

        one.GetString().Should().Be("sub-one");
        two.GetString().Should().Be("sub-two");
    }

    // ── Under load: N subscribers, N distinct principals ────────────────

    [Test]
    public async Task ManyConcurrentSubscribers_EachReceivesOwnPrincipalId()
    {
        const int N = 10;
        using var host = await StartAsync(Schemes.Jwt);

        var subscribers = new List<(WebSocket Ws, string ExpectedId, string SubId)>();
        try
        {
            for (var i = 0; i < N; i++)
            {
                var id = $"user-{i}";
                var token = SignJwt(id, id, "Player");
                var ws = await ConnectAsync(host);
                await InitAsync(ws, new { authToken = token });
                await ExpectAckAsync(ws);
                await SubscribeAsync(ws, $"sub-{i}", WhoAmISubscription);
                subscribers.Add((ws, id, $"sub-{i}"));
            }

            // Single broadcast that fans out to all N subscribers.
            await PokeAsync(host, "fanout", SignJwt("poker", "poker"), scheme: Schemes.Jwt);

            var collected = await Task.WhenAll(
                subscribers.Select(async s =>
                {
                    var payload = await ReceiveNextAsync(s.Ws, s.SubId);
                    return (Expected: s.ExpectedId, Actual: payload.GetString());
                })
            );

            foreach (var (expected, actual) in collected)
                actual.Should().Be(expected);
        }
        finally
        {
            foreach (var (ws, _, _) in subscribers)
                ws.Dispose();
        }
    }

    // ── Multiple events on the same subscription keep the principal ─────

    [Test]
    public async Task RepeatedEvents_SamePrincipalEveryTime()
    {
        const int Events = 5;
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var ws = await ConnectAsync(host);
        await InitAsync(ws, new { authToken = token });
        await ExpectAckAsync(ws);
        await SubscribeAsync(ws, "sub-1", WhoAmISubscription);

        var pokerToken = SignJwt("poker", "poker");
        for (var i = 0; i < Events; i++)
        {
            await PokeAsync(host, $"ping-{i}", pokerToken, scheme: Schemes.Jwt);
            var payload = await ReceiveNextAsync(ws, "sub-1");
            payload.GetString().Should().Be("alice");
        }
    }

    // ── Helpers (graphql-transport-ws protocol) ─────────────────────────

    private static async Task<WebSocket> ConnectAsync(IHost host)
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectAsync(new Uri(WsUri), CancellationToken.None);
    }

    private static async Task InitAsync(WebSocket ws, object payload)
    {
        var msg = JsonSerializer.Serialize(new { type = "connection_init", payload });
        await SendRawAsync(ws, msg);
    }

    private static async Task ExpectAckAsync(WebSocket ws)
    {
        var msg = await ReceiveAsync(ws);
        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    private static async Task SubscribeAsync(WebSocket ws, string id, string query)
    {
        var msg = JsonSerializer.Serialize(
            new
            {
                id,
                type = "subscribe",
                payload = new { query },
            }
        );
        await SendRawAsync(ws, msg);
        // graphql-transport-ws has no "subscribe_ack". Give HC a moment to
        // fully wire the subscription to the in-memory topic before events
        // fire, or the first event will be lost.
        await Task.Delay(100);
    }

    private static async Task SendRawAsync(WebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket ws)
    {
        // Receive a full WebSocket message, accumulating fragmented frames and
        // surfacing close frames with a useful diagnostic instead of letting
        // the empty buffer fall through to a confusing JsonReaderException.
        // The previous version called ReceiveAsync once and parsed whatever
        // came back, which fails non-deterministically when the host sends a
        // text message in multiple frames or returns a zero-length frame
        // before the actual payload.
        var buffer = new byte[16 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, cts.Token);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException(
                    $"WebSocket closed unexpectedly while waiting for a message. "
                        + $"Status: {result.CloseStatus}, Description: {result.CloseStatusDescription}"
                );

            if (result.Count > 0)
                ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage && ms.Length > 0)
                break;
        }

        var text = Encoding.UTF8.GetString(ms.ToArray());
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// Reads messages until a <c>next</c> frame for the given subscription id
    /// arrives, then returns the inner <c>data.whoAmI</c> field as a JSON
    /// element. Ignores <c>ka</c> (keep-alive) and <c>ping</c>/<c>pong</c>
    /// frames along the way.
    /// </summary>
    private static async Task<JsonElement> ReceiveNextAsync(WebSocket ws, string subscriptionId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16 * 1024];

        while (!cts.IsCancellationRequested)
        {
            // Accumulate one full message across fragmented frames before parsing.
            using var ms = new MemoryStream();
            while (!cts.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException(
                        $"WebSocket closed unexpectedly while waiting for subscription {subscriptionId}. "
                            + $"Status: {result.CloseStatus}, Description: {result.CloseStatusDescription}"
                    );

                if (result.Count > 0)
                    ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    break;
            }

            if (ms.Length == 0)
                continue;

            var text = Encoding.UTF8.GetString(ms.ToArray());
            var msg = JsonDocument.Parse(text).RootElement;
            var type = msg.GetProperty("type").GetString();

            if (type == "next")
            {
                if (msg.TryGetProperty("id", out var idEl) && idEl.GetString() == subscriptionId)
                {
                    return msg.GetProperty("payload")
                        .GetProperty("data")
                        .GetProperty("whoAmI")
                        .Clone();
                }
            }
            // ka / ping / pong / other sub's "next" → keep waiting.
        }
        throw new TimeoutException(
            $"Did not receive a 'next' frame for subscription {subscriptionId}."
        );
    }

    /// <summary>
    /// Fires the <c>pokeWhoAmI</c> mutation as the given principal, fanning
    /// out the event to all active subscribers.
    /// </summary>
    private static async Task PokeAsync(IHost host, string tag, string credential, Schemes scheme)
    {
        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query = PokeMutation, variables = new { tag } }),
        };
        if (scheme == Schemes.ApiKey)
            req.Headers.Add("X-Api-Key", credential);
        else
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }
}
