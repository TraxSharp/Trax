using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Persisted-operation enforcement runs inside HotChocolate's execution pipeline, so every way a
/// document reaches the executor gets the same decision: a JSON POST, a GET, a multipart POST, a
/// WebSocket <c>subscribe</c>, and an in-process request. The carve-outs (a persisted id,
/// introspection judged from the document, an allowlisted name) hold on each.
///
/// <para>Enforces <c>docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md")]
[TestFixture]
[Category("Integration")]
public class PersistedOperationTransportTests
{
    private const string Adr =
        "docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md";

    private const string Required = "PERSISTED_OPERATION_REQUIRED";
    private const string InlineQuery = "{ hello }";
    private const string IntrospectionQuery = "{ __schema { queryType { name } } }";

    private WebApplication _app = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        _app = await StartAsync(po => po.AllowOperations("Allowed"));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp() => await PostgresFixture.ClearAsync();

    #region An inline document is refused on every transport

    [Test]
    public async Task JsonPost_InlineDocument_IsRefused()
    {
        (await PostJsonAsync(_app, new { query = InlineQuery })).Should().Be(Required);
    }

    [Test]
    public async Task Get_InlineDocument_IsRefused()
    {
        (await GetAsync(_app, "query=" + Uri.EscapeDataString(InlineQuery)))
            .Should()
            .Be(Required, "a GET reaches the same execution pipeline as a POST, per " + Adr);
    }

    [Test]
    public async Task Multipart_InlineDocument_IsRefused()
    {
        (await PostMultipartAsync(_app, new { query = InlineQuery, variables = new { } }))
            .Should()
            .Be(Required, "a multipart POST reaches the same execution pipeline, per " + Adr);
    }

    [Test]
    public async Task Socket_InlineDocument_IsRefused()
    {
        (await SubscribeAsync(_app, new { query = InlineQuery }))
            .Should()
            .Be(Required, "a socket operation reaches the same execution pipeline, per " + Adr);
    }

    [Test]
    public async Task InProcess_InlineDocument_IsRefused()
    {
        var executor = await ExecutorAsync(_app);

        var result = await executor.ExecuteAsync(InlineQuery);

        Classify(result.ExpectOperationResult().ToJson()).Should().Be(Required);
    }

    [Test]
    public async Task InProcess_HostAllowance_Executes()
    {
        // Host code building a request itself can say so with HotChocolate's own override.
        var executor = await ExecutorAsync(_app);

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(InlineQuery)
                .AllowNonPersistedOperation()
                .Build()
        );

        Classify(result.ExpectOperationResult().ToJson()).Should().Be("data");
    }

    #endregion

    #region The carve-outs hold on every transport

    [Test]
    public async Task JsonPost_Introspection_IsNotRefusedForPersistence()
    {
        (await PostJsonAsync(_app, new { query = IntrospectionQuery })).Should().Be("data");
    }

    [Test]
    public async Task Get_Introspection_IsNotRefusedForPersistence()
    {
        (await GetAsync(_app, "query=" + Uri.EscapeDataString(IntrospectionQuery)))
            .Should()
            .Be("data");
    }

    [Test]
    public async Task Multipart_Introspection_IsNotRefusedForPersistence()
    {
        (await PostMultipartAsync(_app, new { query = IntrospectionQuery, variables = new { } }))
            .Should()
            .Be("data");
    }

    [Test]
    public async Task Socket_Introspection_IsNotRefusedForPersistence()
    {
        (await SubscribeAsync(_app, new { query = IntrospectionQuery })).Should().Be("data");
    }

    [Test]
    public async Task DocumentNamedIntrospectionQuery_SelectingData_IsRefusedOverGet()
    {
        (
            await GetAsync(
                _app,
                "operationName=IntrospectionQuery&query="
                    + Uri.EscapeDataString("query IntrospectionQuery { hello }")
            )
        )
            .Should()
            .Be(Required, "introspection is judged from the document, not its name");
    }

    [Test]
    public async Task PersistedId_ExecutesOnEveryTransport()
    {
        var store = _app.Services.GetRequiredService<IPersistedOperationStore>();
        await store.UpsertAsync("greet_v1", GraphQLFixture.ValidDocument, null, default);

        (await PostJsonAsync(_app, new { id = "greet_v1" })).Should().Be("data");
        (await GetAsync(_app, "id=greet_v1")).Should().Be("data");
        (await SubscribeAsync(_app, new { id = "greet_v1" })).Should().Be("data");
    }

    [Test]
    public async Task AllowlistedName_ExecutesOverGetAndSocket()
    {
        const string query = "query Allowed { hello }";

        (await GetAsync(_app, "operationName=Allowed&query=" + Uri.EscapeDataString(query)))
            .Should()
            .Be("data");
        (await SubscribeAsync(_app, new { query, operationName = "Allowed" })).Should().Be("data");
    }

    [Test]
    public async Task EnforcementOff_InlineDocumentExecutesOverGet()
    {
        await using var app = await StartAsync(po =>
            po.RequirePersisted(false).LogNonPersistedRequests(true)
        );

        (await GetAsync(app, "query=" + Uri.EscapeDataString(InlineQuery))).Should().Be("data");
    }

    #endregion

    private static async Task<WebApplication> StartAsync(
        Action<PersistedOperationsBuilder> configure
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<IOperationsService>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddSingleton(
            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
        );
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(PostgresFixture.ConnectionString))
        );
        services.AddTraxGraphQL(g =>
            g.ExposeOperationQueries()
                .AllowAnonymousOperations()
                // GET is off unless the host opts in; these tests judge enforcement on it.
                .AllowGetRequests()
                .AddTypeExtension<GraphQLFixture.HelloQuery>()
                .UsePersistedOperations(po =>
                {
                    po.UseDatabase(PostgresFixture.ConnectionString)
                        .SingleNode()
                        .ExposeOperationsNamespace(false);
                    configure(po);
                })
        );

        var app = builder.Build();
        app.UseRouting();
        // The documented host setup. Enforcement does not depend on it; the in-process tests
        // above never pass through it.
        app.UsePersistedOperationsEnforcement();
        app.UseTraxGraphQL();
        await app.StartAsync();
        return app;
    }

    private static Task<IRequestExecutor> ExecutorAsync(WebApplication app) =>
        app
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .AsTask();

    private static async Task<string> PostJsonAsync(WebApplication app, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };
        var response = await app.GetTestClient().SendAsync(request);
        return Classify(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> GetAsync(WebApplication app, string queryString)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/trax/graphql?" + queryString);
        request.Headers.Add("GraphQL-Preflight", "1");
        var response = await app.GetTestClient().SendAsync(request);
        return Classify(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> PostMultipartAsync(WebApplication app, object operations)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(operations)), "operations" },
            { new StringContent("{}"), "map" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = form,
        };
        request.Headers.Add("GraphQL-Preflight", "1");
        var response = await app.GetTestClient().SendAsync(request);
        return Classify(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Runs one operation over graphql-transport-ws and classifies the first reply.
    /// </summary>
    private static async Task<string> SubscribeAsync(WebApplication app, object payload)
    {
        var client = app.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var ws = await client.ConnectClosingAsync(
            new Uri("ws://localhost/trax/graphql"),
            default
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await SendAsync(ws, """{"type":"connection_init","payload":{}}""", cts.Token);
        (await ReceiveAsync(ws, cts.Token))
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("connection_ack");

        await SendAsync(
            ws,
            JsonSerializer.Serialize(
                new
                {
                    id = "1",
                    type = "subscribe",
                    payload,
                }
            ),
            cts.Token
        );
        var reply = await ReceiveAsync(ws, cts.Token);
        return reply.GetProperty("type").GetString() switch
        {
            "next" => Classify(reply.GetProperty("payload").GetRawText()),
            "error" => Classify(
                JsonSerializer.Serialize(new { errors = reply.GetProperty("payload") })
            ),
            _ => reply.GetRawText(),
        };
    }

    /// <summary>
    /// The error code when the response carries one, "data" when the operation executed, otherwise
    /// the raw body so a failure says what came back.
    /// </summary>
    private static string Classify(string body)
    {
        if (!body.TrimStart().StartsWith('{'))
            return body;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            foreach (var error in errors.EnumerateArray())
                if (
                    error.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                )
                    return code.GetString() ?? body;
            return body;
        }

        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? "data"
            : body;
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
