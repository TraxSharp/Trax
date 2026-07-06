using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Jwt;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Pins the supported way to supply a custom subscription socket interceptor:
/// <c>graphql.ConfigureSchema(b =&gt; b.AddSocketSessionInterceptor&lt;T&gt;())</c>.
/// It runs, overrides the stock interceptors, and is independent of when auth is
/// registered relative to GraphQL. A future refactor that breaks the seam fails
/// these tests.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CustomSocketInterceptorE2ETests
{
    private const string Database = "trax_api_auth_sub";
    private const string WsUri = "ws://localhost/trax/graphql";

    /// <summary>Accepts only the sentinel payload, so its participation is observable:
    /// the stock/default interceptor accepts everything.</summary>
    public sealed class MagicSocketInterceptor : DefaultSocketSessionInterceptor
    {
        public override ValueTask<ConnectionStatus> OnConnectAsync(
            ISocketSession session,
            IOperationMessagePayload init,
            CancellationToken ct = default
        )
        {
            MagicPayload? payload = null;
            try
            {
                payload = init.As<MagicPayload>();
            }
            catch (JsonException) { }

            return new ValueTask<ConnectionStatus>(
                payload?.Magic == "open-sesame"
                    ? ConnectionStatus.Accept()
                    : ConnectionStatus.Reject("magic word required")
            );
        }

        public sealed record MagicPayload(string? Magic);
    }

    private enum AuthMode
    {
        None,
        JwtBeforeGraphQL,
        JwtAfterGraphQL,
    }

    private static async Task<IHost> StartAsync(AuthMode authMode)
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var cs = AuthE2EHost.ConnectionString(Database);
        var key = Encoding.UTF8.GetBytes(new string('e', 32));

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddAuthentication();
                        s.AddAuthorization();

                        if (authMode == AuthMode.JwtBeforeGraphQL)
                            s.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey("https://iss", "aud", key));

                        s.AddTrax(trax =>
                            trax.AddEffects(effects => effects.UsePostgres(cs).AddJson())
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        s.AddTraxApi();
                        s.AddDbContextFactory<TestDbContext>(o => o.UseNpgsql(cs));
                        s.AddTraxGraphQL(graphql =>
                            graphql
                                .AddDbContext<TestDbContext>()
                                .ConfigureSchema(b =>
                                    b.AddSocketSessionInterceptor<MagicSocketInterceptor>()
                                )
                        );

                        // Auth registered AFTER AddTraxGraphQL: the stock G5 gate
                        // would have skipped its interceptor here, but the custom
                        // one is wired regardless of order.
                        if (authMode == AuthMode.JwtAfterGraphQL)
                            s.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey("https://iss", "aud", key));
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    [Test]
    public async Task ConfigureSchemaInterceptor_Runs_RejectsEmptyPayload()
    {
        using var host = await StartAsync(AuthMode.None);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { });

        // The default interceptor would accept an empty payload; a rejection
        // proves the custom interceptor is the one making the decision.
        (await WaitForCloseAsync(ws))
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task ConfigureSchemaInterceptor_Runs_AcceptsSentinel()
    {
        using var host = await StartAsync(AuthMode.None);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { magic = "open-sesame" });
        var type = (await ReceiveAsync(ws)).GetProperty("type").GetString();

        type.Should().Be("connection_ack");
    }

    [Test]
    public async Task ConfigureSchemaInterceptor_OverridesStockJwt()
    {
        // JWT auth registered before GraphQL wires the stock JWT interceptor at
        // G5. The ConfigureSchema interceptor is applied later and wins: a non-JWT
        // sentinel the stock interceptor would reject is accepted.
        using var host = await StartAsync(AuthMode.JwtBeforeGraphQL);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { magic = "open-sesame" });
        var type = (await ReceiveAsync(ws)).GetProperty("type").GetString();

        type.Should().Be("connection_ack");
    }

    [Test]
    public async Task ConfigureSchemaInterceptor_OrderIndependent_AuthAfterGraphQL()
    {
        using var host = await StartAsync(AuthMode.JwtAfterGraphQL);

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { magic = "open-sesame" });
        var type = (await ReceiveAsync(ws)).GetProperty("type").GetString();

        type.Should().Be("connection_ack");
    }

    // ── WS helpers (mirror SubscriptionE2ETests) ──

    private static async Task<WebSocket> ConnectAsync(IHost host)
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectAsync(new Uri(WsUri), CancellationToken.None);
    }

    private static async Task SendInitAsync(WebSocket ws, object payload)
    {
        var msg = JsonSerializer.Serialize(new { type = "connection_init", payload });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket ws)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException(
                    $"WebSocket closed unexpectedly: {result.CloseStatus} {result.CloseStatusDescription}"
                );
            if (result.Count > 0)
                ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage && ms.Length > 0)
                break;
        }
        return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray())).RootElement.Clone();
    }

    private static async Task<bool> WaitForCloseAsync(WebSocket ws)
    {
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
                var type = JsonDocument
                    .Parse(Encoding.UTF8.GetString(ms.ToArray()))
                    .RootElement.GetProperty("type")
                    .GetString();
                if (type == "connection_error")
                    return true;
                if (type == "connection_ack")
                    return false;
            }
        }
        catch (WebSocketException)
        {
            return true;
        }
        return false;
    }
}
