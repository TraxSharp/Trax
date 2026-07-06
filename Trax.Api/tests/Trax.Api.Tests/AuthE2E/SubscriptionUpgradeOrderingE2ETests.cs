using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Guards the WebSocket-upgrade ordering fix (WebSocketsStartupFilter). A host
/// that wires an endpoint terminal (Blazor's MapRazorComponents via the
/// dashboard, an explicit UseEndpoints, etc.) before UseTraxGraphQL used to
/// serve the subscription handshake as a plain HTTP response because the
/// upgrade middleware ran too late. AddTraxGraphQL now prepends UseWebSockets()
/// via a startup filter, so the upgrade works regardless of ordering. Delete
/// the filter and these tests fail.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SubscriptionUpgradeOrderingE2ETests
{
    private const string Database = "trax_api_auth_sub";

    [Test]
    public async Task DashboardShapePipeline_UpgradesTo101()
    {
        await using var app = await StartHostAsync(dashboardShape: true);
        var status = await RawHandshakeStatusAsync(app);
        status.Should().Be("HTTP/1.1 101 Switching Protocols");
    }

    [Test]
    public async Task DashboardShapePipeline_ConnectionAck()
    {
        await using var app = await StartHostAsync(dashboardShape: true);
        var type = await ConnectionInitTypeAsync(app);
        type.Should().Be("connection_ack");
    }

    [Test]
    public async Task NoExplicitUseWebSockets_StillUpgrades()
    {
        await using var app = await StartHostAsync(dashboardShape: false);
        var status = await RawHandshakeStatusAsync(app);
        status.Should().Be("HTTP/1.1 101 Switching Protocols");
    }

    private static async Task<WebApplication> StartHostAsync(bool dashboardShape)
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var cs = AuthE2EHost.ConnectionString(Database);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(cs).AddJson())
                .AddMediator(typeof(AuthE2EHost).Assembly)
        );
        services.AddTraxApi();
        services.AddDbContextFactory<TestDbContext>(o => o.UseNpgsql(cs));
        services.AddTraxGraphQL(graphql => graphql.AddDbContext<TestDbContext>());

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        if (dashboardShape)
            // Stands in for UseTraxDashboard()'s MapRazorComponents terminal —
            // an endpoint execution point wired before UseTraxGraphQL.
            app.UseEndpoints(_ => { });
        app.UseTraxGraphQL();

        await app.StartAsync();
        return app;
    }

    private static Uri BaseUri(WebApplication app) =>
        new(
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First()
        );

    private static async Task<string> RawHandshakeStatusAsync(WebApplication app)
    {
        var uri = BaseUri(app);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(uri.Host, uri.Port);
        using var stream = tcp.GetStream();
        var handshake =
            "GET /trax/graphql HTTP/1.1\r\n"
            + $"Host: {uri.Host}:{uri.Port}\r\n"
            + "Upgrade: websocket\r\n"
            + "Connection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n"
            + "Sec-WebSocket-Version: 13\r\n"
            + "Sec-WebSocket-Protocol: graphql-transport-ws\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(handshake));
        var buffer = new byte[1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var read = await stream.ReadAsync(buffer, cts.Token);
        return Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
    }

    private static async Task<string?> ConnectionInitTypeAsync(WebApplication app)
    {
        var baseUri = BaseUri(app);
        var wsUri = new Uri($"ws://{baseUri.Host}:{baseUri.Port}/trax/graphql");
        using var client = new ClientWebSocket();
        client.Options.AddSubProtocol("graphql-transport-ws");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(wsUri, cts.Token);

        var init = JsonSerializer.Serialize(new { type = "connection_init", payload = new { } });
        await client.SendAsync(
            Encoding.UTF8.GetBytes(init),
            WebSocketMessageType.Text,
            true,
            cts.Token
        );

        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await client.ReceiveAsync(buffer, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close)
                return "(closed)";
            if (r.Count > 0)
                ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage && ms.Length > 0)
                break;
        }
        return JsonDocument
            .Parse(Encoding.UTF8.GetString(ms.ToArray()))
            .RootElement.GetProperty("type")
            .GetString();
    }
}
