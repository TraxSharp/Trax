using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A WebSocket upgrade to the Trax GraphQL endpoint is accepted from the endpoint's own origin,
/// from an origin the host allows, or with no <c>Origin</c> header at all, and is refused with
/// 403 before the handshake completes otherwise. The allowed origins come from
/// <c>AllowSocketOrigins(...)</c>, or by default from the host's CORS default policy.
///
/// <para>Enforces <c>docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md")]
[TestFixture]
public class SocketUpgradeOriginTests
{
    private const string Adr =
        "docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md";

    [Test]
    public async Task UnlistedOrigin_IsRefusedWith403()
    {
        await using var app = await StartAsync();

        (await UpgradeAsync(app, "https://unlisted.example"))
            .Should()
            .Be(
                "403",
                "an origin that is neither the endpoint's own nor allowed is refused, per " + Adr
            );
    }

    [Test]
    public async Task SameOrigin_IsAcked()
    {
        await using var app = await StartAsync();

        (await UpgradeAsync(app, "http://localhost")).Should().Be("connection_ack");
    }

    [Test]
    public async Task SameHostOverHttps_IsAcked()
    {
        // TLS is commonly terminated in front of the app, so the request the app sees is http
        // while the page's origin is https. The host is what identifies the origin the app serves.
        await using var app = await StartAsync();

        (await UpgradeAsync(app, "https://localhost")).Should().Be("connection_ack");
    }

    [Test]
    public async Task NoOriginHeader_IsAcked()
    {
        await using var app = await StartAsync();

        (await UpgradeAsync(app, origin: null))
            .Should()
            .Be(
                "connection_ack",
                "a client that sends no Origin is not a browser page, per " + Adr
            );
    }

    [Test]
    public async Task ExplicitlyAllowedOrigin_IsAcked()
    {
        await using var app = await StartAsync(graphql =>
            graphql.AllowSocketOrigins("https://app.example")
        );

        (await UpgradeAsync(app, "https://app.example")).Should().Be("connection_ack");
        (await UpgradeAsync(app, "https://unlisted.example")).Should().Be("403");
    }

    [Test]
    public async Task CorsDefaultPolicyOrigin_IsAcked()
    {
        await using var app = await StartAsync(configureServices: s =>
            s.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://spa.example")))
        );

        (await UpgradeAsync(app, "https://spa.example"))
            .Should()
            .Be(
                "connection_ack",
                "the CORS default policy's origins are the default allowlist, per " + Adr
            );
        (await UpgradeAsync(app, "https://unlisted.example")).Should().Be("403");
    }

    [Test]
    public async Task CorsDefaultPolicyAllowingAnyOrigin_AcksAnyOrigin()
    {
        await using var app = await StartAsync(configureServices: s =>
            s.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin()))
        );

        (await UpgradeAsync(app, "https://anything.example")).Should().Be("connection_ack");
    }

    [Test]
    public async Task ExplicitList_ReplacesTheCorsDefault()
    {
        await using var app = await StartAsync(
            graphql => graphql.AllowSocketOrigins("https://app.example"),
            s => s.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://spa.example")))
        );

        (await UpgradeAsync(app, "https://spa.example")).Should().Be("403");
        (await UpgradeAsync(app, "https://app.example")).Should().Be("connection_ack");
    }

    [Test]
    public async Task EmptyExplicitList_AllowsTheEndpointsOwnOriginOnly()
    {
        await using var app = await StartAsync(
            graphql => graphql.AllowSocketOrigins(),
            s => s.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://spa.example")))
        );

        (await UpgradeAsync(app, "https://spa.example")).Should().Be("403");
        (await UpgradeAsync(app, "http://localhost")).Should().Be("connection_ack");
    }

    [Test]
    public async Task OriginWithDefaultPortSpelledOut_MatchesTheAllowedOrigin()
    {
        await using var app = await StartAsync(graphql =>
            graphql.AllowSocketOrigins("https://App.Example")
        );

        (await UpgradeAsync(app, "https://app.example:443")).Should().Be("connection_ack");
    }

    [Test]
    public async Task HttpPostFromAnUnlistedOrigin_IsNotAffected()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                """{"query":"{ __typename }"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        request.Headers.Add("Origin", "https://unlisted.example");

        using var response = await client.SendAsync(request);

        ((int)response.StatusCode).Should().Be(200);
    }

    #region Schema mapped by the host with MapGraphQL(path, "trax")

    [Test]
    public async Task SelfMapped_UnlistedOrigin_IsRefusedWith403()
    {
        await using var app = await StartAsync(selfMapped: true);

        (await UpgradeAsync(app, "https://unlisted.example"))
            .Should()
            .Be(
                "403",
                "the rule applies wherever the Trax schema serves a socket, not only through "
                    + "UseTraxGraphQL(), per "
                    + Adr
            );
    }

    [Test]
    public async Task SelfMapped_SameOrigin_IsAcked()
    {
        await using var app = await StartAsync(selfMapped: true);

        (await UpgradeAsync(app, "http://localhost")).Should().Be("connection_ack");
    }

    [Test]
    public async Task SelfMapped_NoOriginHeader_IsAcked()
    {
        await using var app = await StartAsync(selfMapped: true);

        (await UpgradeAsync(app, origin: null)).Should().Be("connection_ack");
    }

    [Test]
    public async Task SelfMapped_ExplicitlyAllowedOrigin_IsAcked()
    {
        await using var app = await StartAsync(
            graphql => graphql.AllowSocketOrigins("https://app.example"),
            selfMapped: true
        );

        (await UpgradeAsync(app, "https://app.example")).Should().Be("connection_ack");
        (await UpgradeAsync(app, "https://unlisted.example")).Should().Be("403");
    }

    [Test]
    public async Task SelfMapped_CorsDefaultPolicyOrigin_IsAcked()
    {
        await using var app = await StartAsync(
            configureServices: s =>
                s.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://spa.example"))),
            selfMapped: true
        );

        (await UpgradeAsync(app, "https://spa.example")).Should().Be("connection_ack");
        (await UpgradeAsync(app, "https://unlisted.example")).Should().Be("403");
    }

    [Test]
    public async Task AnotherSchemaOnTheSameHost_IsNotAffected()
    {
        // The rule belongs to the Trax schema. A host's own HotChocolate schema keeps its own
        // behaviour.
        await using var app = await StartAsync(
            configureServices: s =>
                s.AddGraphQLServer("other")
                    .AddQueryType(d =>
                        d.Name("Query")
                            .Field("ok")
                            .Type<HotChocolate.Types.BooleanType>()
                            .Resolve(_ => new ValueTask<object?>(true))
                    ),
            mapOther: true
        );

        (await UpgradeAsync(app, "https://unlisted.example", "/other/graphql"))
            .Should()
            .Be("connection_ack");
        (await UpgradeAsync(app, "https://unlisted.example")).Should().Be("403");
    }

    #endregion

    [TestCase("https://app.example/path")]
    [TestCase("https://app.example?x=1")]
    [TestCase("app.example")]
    [TestCase("")]
    public void AllowSocketOrigins_RejectsAnythingButAnOrigin(string origin)
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var act = () => builder.AllowSocketOrigins(origin);

        act.Should().Throw<ArgumentException>().WithMessage("*origin*");
    }

    private static async Task<WebApplication> StartAsync(
        Action<TraxGraphQLBuilder>? graphql = null,
        Action<IServiceCollection>? configureServices = null,
        bool selfMapped = false,
        bool mapOther = false
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("SocketOrigin_" + Guid.NewGuid())
        );
        configureServices?.Invoke(services);
        services.AddTraxGraphQL(g =>
        {
            g.AddDbContext<OrderTestDbContext>();
            graphql?.Invoke(g);
            return g;
        });

        var app = builder.Build();
        app.UseRouting();
        if (selfMapped)
            app.MapGraphQL("/trax/graphql", "trax");
        else
            app.UseTraxGraphQL();
        if (mapOther)
            app.MapGraphQL("/other/graphql", "other");
        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Opens a graphql-transport-ws socket carrying <paramref name="origin"/>. Returns the HTTP
    /// status when the handshake is refused, otherwise the type of the reply to
    /// <c>connection_init</c>.
    /// </summary>
    private static async Task<string> UpgradeAsync(
        WebApplication app,
        string? origin,
        string path = "/trax/graphql"
    )
    {
        var client = app.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        if (origin is not null)
            client.ConfigureRequest = r => r.Headers["Origin"] = origin;

        WebSocket ws;
        try
        {
            ws = await client.ConnectClosingAsync(new Uri("ws://localhost" + path), default);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("status code"))
        {
            return ex.Message[(ex.Message.LastIndexOf(' ') + 1)..];
        }

        using (ws)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ws.SendAsync(
                Encoding.UTF8.GetBytes("""{"type":"connection_init","payload":{}}"""),
                WebSocketMessageType.Text,
                true,
                cts.Token
            );
            var buffer = new byte[4096];
            var received = await ws.ReceiveAsync(buffer, cts.Token);
            if (received.MessageType == WebSocketMessageType.Close)
                return "(closed)";
            return JsonDocument
                .Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))
                .RootElement.GetProperty("type")
                .GetString()!;
        }
    }
}
