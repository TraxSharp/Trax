using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for GraphQL subscription authentication over real
/// WebSocket connections. Exercises the full handshake: TestServer WS
/// upgrade → graphql-transport-ws <c>connection_init</c> → Trax socket
/// interceptor → principal attached (or connection rejected).
/// </summary>
[TestFixture]
[NonParallelizable]
public class SubscriptionE2ETests
{
    private const string Database = "trax_api_auth_sub";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    private const string WsUri = "ws://localhost/trax/graphql";

    // ── ApiKey scheme ────────────────────────────────────────────────────

    [Test]
    public async Task ApiKey_ValidToken_ConnectionAck()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = AdminApiKey });
        var msg = await ReceiveAsync(ws);

        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    [Test]
    public async Task ApiKey_AlternateKeyFieldName_ConnectionAck()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        // TraxApiKeySocketInterceptor accepts either "authToken" or "apiKey".
        await SendInitAsync(ws, new { apiKey = PlayerApiKey });
        var msg = await ReceiveAsync(ws);

        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    [Test]
    public async Task ApiKey_InvalidToken_ConnectionRejected()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = "bogus-key" });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    [Test]
    public async Task ApiKey_MissingToken_ConnectionRejected()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    // ── JWT scheme ───────────────────────────────────────────────────────

    [Test]
    public async Task Jwt_ValidToken_ConnectionAck()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });
        var msg = await ReceiveAsync(ws);

        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    [Test]
    public async Task Jwt_BearerFieldName_ConnectionAck()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var ws = await ConnectAsync(host);
        // TraxJwtSocketInterceptor accepts either "authToken" or "bearer".
        await SendInitAsync(ws, new { bearer = token });
        var msg = await ReceiveAsync(ws);

        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    [Test]
    public async Task Jwt_MalformedToken_ConnectionRejected()
    {
        using var host = await StartAsync(Schemes.Jwt);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = "not.a.jwt" });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    [Test]
    public async Task Jwt_ExpiredToken_ConnectionRejected()
    {
        using var host = await StartAsync(Schemes.Jwt);

        // Forge an expired but otherwise valid token.
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(JwtKey);
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key,
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
        );
        var now = DateTime.UtcNow;
        var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new System.Security.Claims.Claim("sub", "alice")],
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1),
            signingCredentials: creds
        );
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
            expired
        );

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    [Test]
    public async Task Jwt_WrongSignature_ConnectionRejected()
    {
        using var host = await StartAsync(Schemes.Jwt);

        var otherKey = Encoding.UTF8.GetBytes(new string('z', 32));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(otherKey),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
        );
        var now = DateTime.UtcNow;
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new System.Security.Claims.Claim("sub", "alice")],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(15),
            signingCredentials: creds
        );
        var signed = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
            token
        );

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = signed });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    // ── Multi-scheme coexistence over WS (known limitation) ─────────────
    //
    // HotChocolate supports a single ISocketSessionInterceptor per schema.
    // When both ApiKey and Jwt interceptors are registered, the last one
    // registered wins. In AuthE2EHost that's JWT (it registers after
    // ApiKey), so a WS connection that presents an API-key token while both
    // schemes are wired gets rejected by the JWT interceptor because the
    // token isn't a valid JWT. Presenting a JWT works.
    //
    // Hosts that need BOTH credential types over WS must pick one to use
    // on subscriptions, or author their own composite interceptor.

    [Test]
    public async Task BothSchemes_Jwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });
        var msg = await ReceiveAsync(ws);

        msg.GetProperty("type").GetString().Should().Be("connection_ack");
    }

    [Test]
    public async Task BothSchemes_ApiKeyToken_RejectedByLastInterceptor()
    {
        // Regression guard: when both socket interceptors are registered,
        // only the last one (JWT) runs. An API-key token presented over WS
        // gets rejected because JWT doesn't recognize it. If this test
        // starts passing, a composite interceptor has been added and the
        // multi-scheme story improved.
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = AdminApiKey });

        var closed = await WaitForCloseAsync(ws);
        closed.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<WebSocket> ConnectAsync(IHost host)
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectAsync(new Uri(WsUri), CancellationToken.None);
    }

    private static async Task SendInitAsync(WebSocket ws, object payload)
    {
        var msg = JsonSerializer.Serialize(new { type = "connection_init", payload });
        var bytes = Encoding.UTF8.GetBytes(msg);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket ws)
    {
        // Receive a full WebSocket message, accumulating fragmented frames and
        // surfacing close frames with a useful diagnostic instead of letting
        // the empty buffer fall through to a confusing JsonReaderException.
        var buffer = new byte[4096];
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
    /// Reads until the socket is closed or a connection_error arrives. HC's
    /// graphql-transport-ws implementation closes the socket on auth
    /// failure; other implementations may send a connection_error frame
    /// first. Either shape counts as "rejected."
    /// </summary>
    private static async Task<bool> WaitForCloseAsync(WebSocket ws)
    {
        // Mirror the framing handling from ReceiveAsync: accumulate fragmented
        // frames before parsing, surface close frames immediately, and skip
        // zero-length frames. A single-shot ReceiveAsync would fail with an
        // uncaught JsonReaderException on a partial buffer, defeating the
        // purpose of this rejection check.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        try
        {
            while (!cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                while (!cts.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return true;

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
                if (type == "connection_error")
                    return true;
                if (type == "connection_ack")
                    return false; // authentication succeeded — test expected rejection
            }
        }
        catch (WebSocketException)
        {
            return true;
        }
        return false;
    }
}
