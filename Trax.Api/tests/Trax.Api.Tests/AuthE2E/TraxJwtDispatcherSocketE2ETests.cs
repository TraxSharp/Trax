using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for the dispatcher-aware subscription interceptor over a
/// real WebSocket: two JWT schemes (a JWKS/RS256 "cognito" scheme and a symmetric
/// "internal" scheme) both authenticate through a single AddTraxJwtDispatcher,
/// routed by the token's issuer. This is the multi-scheme story the single-scheme
/// stock interceptor cannot serve.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TraxJwtDispatcherSocketE2ETests
{
    private const string Database = "trax_api_jwt_dispatch";
    private const string Audience = "trax-ws";
    private const string InternalIssuer = "https://internal-issuer";
    private static readonly byte[] InternalKey = Encoding.UTF8.GetBytes(new string('i', 32));
    private const string WsUri = "ws://localhost/trax/graphql";
    private static readonly TimeSpan WarmupBudget = TimeSpan.FromSeconds(30);

    private static async Task<(IHost Host, TestJwksServer Jwks)> StartAsync()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var cs = AuthE2EHost.ConnectionString(Database);
        var jwks = await TestJwksServer.StartAsync();

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxJwtAuth(
                            "cognito",
                            jwt =>
                                jwt.UseAuthority(jwks.Issuer, Audience)
                                    .AllowHttpMetadata()
                                    // Generous on purpose. ADR 0014 asks that nothing
                                    // slower than a deadline sit under it, and warming
                                    // the configuration below achieves that directly:
                                    // once it is cached no HTTP happens under the socket
                                    // receive at all. Pinning this under the receive
                                    // instead only moved the failure into setup, where a
                                    // slow first connect to the loopback JWKS server blew
                                    // a 5s budget on a loaded CI runner.
                                    .CustomizeBearerOptions(o =>
                                        o.BackchannelTimeout = WarmupBudget
                                    )
                        );
                        s.AddTraxJwtAuth(
                            "internal",
                            jwt => jwt.UseSymmetricKey(InternalIssuer, Audience, InternalKey)
                        );
                        s.AddTraxJwtDispatcher(d =>
                            d.MapIssuer(jwks.Issuer, "cognito")
                                .MapIssuer(InternalIssuer, "internal")
                        );
                        s.AddAuthorization();
                        s.AddTrax(trax =>
                            trax.AddEffects(effects => effects.UsePostgres(cs).AddJson())
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                        );
                        s.AddTraxApi();
                        s.AddDbContextFactory<TestDbContext>(o => o.UseNpgsql(cs));
                        s.AddTraxGraphQL(graphql => graphql.AddDbContext<TestDbContext>());
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
        await WarmJwksMetadataAsync(host, jwks);
        return (host, jwks);
    }

    /// <summary>
    /// Loads the "cognito" scheme's discovery document once, before any test
    /// opens a socket, so the first <c>connection_init</c> on a JWKS-backed
    /// scheme does not pay for it inside the receive deadline. That is what
    /// keeps ADR 0014 satisfied here: after this returns the socket path
    /// resolves signing keys from cache and makes no HTTP call.
    /// </summary>
    /// <remarks>
    /// Patient on purpose. The first connection to the in-process JWKS server
    /// has taken more than five seconds on a loaded CI runner, waiting inside
    /// HttpConnectionPool for a connection rather than on a reply. A slow
    /// connect wants one generous attempt, not several short ones. This runs in
    /// setup under no assertion, so the budget costs nothing when things are
    /// healthy, and a genuine failure names the metadata address instead of
    /// surfacing later as a socket that never acked.
    /// </remarks>
    private static async Task WarmJwksMetadataAsync(IHost host, TestJwksServer jwks)
    {
        var options = host
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("cognito");
        var discovery = $"{jwks.Issuer}/.well-known/openid-configuration";

        try
        {
            // Prove the endpoint answers on a client we control before the
            // bearer handler's backchannel spends its attempt on it.
            using var probe = new HttpClient { Timeout = WarmupBudget };
            (await probe.GetAsync(discovery)).EnsureSuccessStatusCode();

            using var cts = new CancellationTokenSource(WarmupBudget);
            await options.ConfigurationManager!.GetConfigurationAsync(cts.Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The cognito scheme's discovery document at {discovery} did not load within "
                    + $"{WarmupBudget.TotalSeconds}s of host start.",
                ex
            );
        }
    }

    [Test]
    public async Task CognitoJwksToken_Acks()
    {
        var (host, jwks) = await StartAsync();
        await using var _ = jwks;
        using var __ = host;

        var token = jwks.CreateIssuer(Audience)
            .Mint(b => b.WithSubject("cog").WithClaim("name", "Cog"));

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });
        var type = (await ReceiveAsync(ws)).GetProperty("type").GetString();

        type.Should().Be("connection_ack");
    }

    [Test]
    public async Task InternalSymmetricToken_Acks()
    {
        var (host, jwks) = await StartAsync();
        await using var _ = jwks;
        using var __ = host;

        var token = TestTokenIssuer
            .Symmetric(InternalIssuer, Audience, InternalKey)
            .Mint(b => b.WithSubject("int").WithClaim("name", "Int"));

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });
        var type = (await ReceiveAsync(ws)).GetProperty("type").GetString();

        type.Should().Be("connection_ack");
    }

    [Test]
    public async Task UnknownIssuer_Rejected()
    {
        var (host, jwks) = await StartAsync();
        await using var _ = jwks;
        using var __ = host;

        var token = TestTokenIssuer
            .Symmetric("https://stranger", Audience, InternalKey)
            .Mint(b => b.WithSubject("nobody"));

        using var ws = await ConnectAsync(host);
        await SendInitAsync(ws, new { authToken = token });

        (await WaitForCloseAsync(ws)).Should().BeTrue();
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
        var budget = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(budget);
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException(
                    $"No frame arrived within {budget.TotalSeconds}s. Every timeout under "
                        + "this wait must be shorter than it (ADR 0014).",
                    ex
                );
            }
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
