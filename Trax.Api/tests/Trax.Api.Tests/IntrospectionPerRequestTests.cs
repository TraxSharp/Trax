using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
/// Introspection is decided per request, over the real transports: in Development it is
/// allowed, elsewhere it is not, and a host predicate given to <c>AllowIntrospection</c> replaces
/// both. The schema download (<c>?sdl</c>, <c>/schema</c>, <c>/schema.graphql</c>) and the
/// GraphQL IDE follow the same decision.
///
/// <para>Enforces <c>docs/adr/0012-introspection-is-decided-per-request.md</c>.</para>
/// </summary>
/// <remarks>
/// These run over a <see cref="TestServer"/> on purpose. An executor built in-process has no
/// HTTP request, so it cannot show what a remote caller is given.
/// </remarks>
[Property("adr", "docs/adr/0012-introspection-is-decided-per-request.md")]
[TestFixture]
public class IntrospectionPerRequestTests
{
    private const string Adr = "docs/adr/0012-introspection-is-decided-per-request.md";

    private const string SchemaQuery = "{ __schema { queryType { name } } }";

    #region HTTP execution

    [Test]
    public async Task Production_Introspection_IsRefused()
    {
        await using var app = await StartAsync(Environments.Production);

        (await PostAsync(app, SchemaQuery))
            .Should()
            .Be("refused", "introspection is off outside Development, per " + Adr);
    }

    [Test]
    public async Task Staging_Introspection_IsRefused()
    {
        await using var app = await StartAsync(Environments.Staging);

        (await PostAsync(app, SchemaQuery)).Should().Be("refused");
    }

    [Test]
    public async Task Development_Introspection_IsAllowed()
    {
        await using var app = await StartAsync(Environments.Development);

        (await PostAsync(app, SchemaQuery)).Should().Be("data");
    }

    [Test]
    public async Task Production_TypeNameOnly_IsNotIntrospection()
    {
        await using var app = await StartAsync(Environments.Production);

        (await PostAsync(app, "{ __typename }")).Should().Be("data");
    }

    [Test]
    public async Task Production_PredicateReturningTrue_Allows()
    {
        await using var app = await StartAsync(
            Environments.Production,
            g => g.AllowIntrospection(_ => true)
        );

        (await PostAsync(app, SchemaQuery)).Should().Be("data");
    }

    [Test]
    public async Task Development_PredicateReturningFalse_Refuses()
    {
        await using var app = await StartAsync(
            Environments.Development,
            g => g.AllowIntrospection(_ => false)
        );

        (await PostAsync(app, SchemaQuery))
            .Should()
            .Be("refused", "the host predicate replaces the environment default, per " + Adr);
    }

    [Test]
    public async Task Predicate_SeesTheRequest()
    {
        await using var app = await StartAsync(
            Environments.Production,
            g => g.AllowIntrospection(http => http.Request.Headers.ContainsKey("X-Schema-Reader"))
        );

        (await PostAsync(app, SchemaQuery)).Should().Be("refused");
        (await PostAsync(app, SchemaQuery, r => r.Headers.Add("X-Schema-Reader", "1")))
            .Should()
            .Be("data", "the predicate is evaluated against each request, per " + Adr);
    }

    [Test]
    public async Task Predicate_IsCalledForEveryRequest()
    {
        var calls = 0;
        await using var app = await StartAsync(
            Environments.Production,
            g =>
                g.AllowIntrospection(_ =>
                {
                    Interlocked.Increment(ref calls);
                    return true;
                })
        );

        await PostAsync(app, SchemaQuery);
        await PostAsync(app, SchemaQuery);

        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Production_IntrospectionOverGet_IsRefused()
    {
        // GET is off unless the host opts in; with it on, introspection over GET is still refused.
        await using var app = await StartAsync(Environments.Production, g => g.AllowGetRequests());
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/trax/graphql?query=" + Uri.EscapeDataString(SchemaQuery)
        );
        request.Headers.Add("GraphQL-Preflight", "1");

        var response = await client.SendAsync(request);

        Classify(await response.Content.ReadAsStringAsync()).Should().Be("refused");
    }

    #endregion

    #region WebSocket

    [Test]
    public async Task Production_IntrospectionOverSocket_IsRefused()
    {
        await using var app = await StartAsync(Environments.Production);

        (await SubscribeAsync(app, SchemaQuery))
            .Should()
            .Be("refused", "the decision lives in the execution pipeline, per " + Adr);
    }

    [Test]
    public async Task Development_IntrospectionOverSocket_IsAllowed()
    {
        await using var app = await StartAsync(Environments.Development);

        (await SubscribeAsync(app, SchemaQuery)).Should().Be("data");
    }

    [Test]
    public async Task Production_PredicateReturningTrue_AllowsOverSocket()
    {
        await using var app = await StartAsync(
            Environments.Production,
            g => g.AllowIntrospection(_ => true)
        );

        (await SubscribeAsync(app, SchemaQuery)).Should().Be("data");
    }

    #endregion

    #region Schema download and IDE

    [TestCase("/trax/graphql?sdl")]
    [TestCase("/trax/graphql/schema")]
    [TestCase("/trax/graphql/schema/")]
    [TestCase("/trax/graphql/schema.graphql")]
    public async Task Production_SchemaDownload_Is404(string path)
    {
        await using var app = await StartAsync(Environments.Production);

        var response = await app.GetTestClient().GetAsync(path);

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.NotFound,
                "the schema download follows the same decision, per " + Adr
            );
    }

    [Test]
    public async Task Production_SchemaDownloadBesideAnOperation_Is404()
    {
        await using var app = await StartAsync(Environments.Production);

        var response = await app.GetTestClient()
            .GetAsync("/trax/graphql?sdl&query=" + Uri.EscapeDataString("{ __typename }"));

        (await response.Content.ReadAsStringAsync()).Should().NotContain("type RootQuery");
    }

    [TestCase("/trax/graphql?sdl")]
    [TestCase("/trax/graphql/schema.graphql")]
    public async Task Development_SchemaDownload_IsServed(string path)
    {
        await using var app = await StartAsync(Environments.Development);

        var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("type RootQuery");
    }

    [Test]
    public async Task Production_PredicateReturningTrue_ServesSchemaDownloadPrivately()
    {
        await using var app = await StartAsync(
            Environments.Production,
            g => g.AllowIntrospection(_ => true)
        );

        var response = await app.GetTestClient().GetAsync("/trax/graphql?sdl");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response
            .Headers.CacheControl!.Public.Should()
            .BeFalse("a download decided per caller is not a shared-cache response, per " + Adr);
        response.Headers.CacheControl.Private.Should().BeTrue();
    }

    [Test]
    public async Task Development_PredicateReturningFalse_SchemaDownloadIs404()
    {
        await using var app = await StartAsync(
            Environments.Development,
            g => g.AllowIntrospection(_ => false)
        );

        var response = await app.GetTestClient().GetAsync("/trax/graphql?sdl");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Production_Ide_Is404()
    {
        await using var app = await StartAsync(Environments.Production);

        var response = await GetHtmlAsync(app, "/trax/graphql");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Development_Ide_IsServed()
    {
        await using var app = await StartAsync(Environments.Development);

        var response = await GetHtmlAsync(app, "/trax/graphql/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Test]
    public async Task Production_SelfMappedEndpoint_ServesNeitherSchemaNorIde()
    {
        // A host that maps the schema itself skips UseTraxGraphQL's per-request gate. Without a
        // predicate the answer outside Development is always no, so the schema's server options
        // turn both off as well.
        await using var app = await StartAsync(Environments.Production, selfMapped: true);

        var sdl = await app.GetTestClient().GetAsync("/trax/graphql?sdl");
        (await sdl.Content.ReadAsStringAsync()).Should().NotContain("type RootQuery");
        (await GetHtmlAsync(app, "/trax/graphql/")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Production_SocketUpgrade_IsNotTreatedAsASchemaRequest()
    {
        await using var app = await StartAsync(Environments.Production);

        (await SubscribeAsync(app, "{ __typename }")).Should().Be("data");
    }

    #endregion

    private static async Task<HttpResponseMessage> GetHtmlAsync(WebApplication app, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<WebApplication> StartAsync(
        string environment,
        Action<TraxGraphQLBuilder>? graphql = null,
        bool selfMapped = false
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment }
        );
        builder.WebHost.UseTestServer();
        // Development turns on build-time validation; the stubs below do not satisfy the whole
        // graph, and nothing here resolves the services it would complain about.
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("Introspection_" + Guid.NewGuid())
        );
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
        await app.StartAsync();
        return app;
    }

    private static async Task<string> PostAsync(
        WebApplication app,
        string query,
        Action<HttpRequestMessage>? configure = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { query }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        configure?.Invoke(request);
        var response = await app.GetTestClient().SendAsync(request);
        return Classify(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// "refused" when the response carries HotChocolate's introspection refusal, "data" when the
    /// operation executed, otherwise the raw body so a failure says what came back.
    /// </summary>
    private static string Classify(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors))
        {
            foreach (var error in errors.EnumerateArray())
                if (
                    error.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                    && code.GetString() == "HC0046"
                )
                    return "refused";
            return body;
        }

        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? "data"
            : body;
    }

    /// <summary>
    /// Runs <paramref name="query"/> over graphql-transport-ws and classifies the first reply the
    /// same way as <see cref="Classify"/>.
    /// </summary>
    private static async Task<string> SubscribeAsync(WebApplication app, string query)
    {
        var client = app.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var ws = await client.ConnectAsync(new Uri("ws://localhost/trax/graphql"), default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await SendAsync(ws, """{"type":"connection_init","payload":{}}""", cts.Token);
        var ack = await ReceiveAsync(ws, cts.Token);
        ack.GetProperty("type").GetString().Should().Be("connection_ack");

        await SendAsync(
            ws,
            JsonSerializer.Serialize(
                new
                {
                    id = "1",
                    type = "subscribe",
                    payload = new { query },
                }
            ),
            cts.Token
        );
        var reply = await ReceiveAsync(ws, cts.Token);
        var type = reply.GetProperty("type").GetString();
        return type switch
        {
            "next" => Classify(reply.GetProperty("payload").GetRawText()),
            "error" => Classify(
                JsonSerializer.Serialize(new { errors = reply.GetProperty("payload") })
            ),
            _ => reply.GetRawText(),
        };
    }

    private static Task SendAsync(WebSocket ws, string message, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

    private static async Task<JsonElement> ReceiveAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var received = await ws.ReceiveAsync(buffer, ct);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
        return doc.RootElement.Clone();
    }
}
