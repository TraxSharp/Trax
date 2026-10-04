using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Registration order must never change behaviour silently.
/// </summary>
/// <remarks>
/// <c>AddTraxGraphQL()</c> used to read the <c>IServiceCollection</c> to decide which subscription
/// interceptor to wire, so a scheme registered after it was invisible and HotChocolate's
/// accept-everything interceptor stayed in place: WebSocket clients connected unauthenticated
/// while HTTP kept working. It registers one composite interceptor now, which reads the schemes
/// from the finished container, so either order authenticates.
/// <para>
/// The query and mutation halves guard the opposite property. Their auth does not depend on
/// order at all, and has to keep working whichever way round the host is composed.
/// </para>
/// <para>Enforces <c>docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md</c> for the subscription half.</para>
/// </remarks>
[Property("adr", "docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md")]
[TestFixture]
public class RegistrationOrderTests
{
    #region Subscriptions — a scheme is enforced whichever side of AddTraxGraphQL it lands

    [Test]
    public async Task JwtAuthBeforeGraphQL_WiresTheCompositeInterceptor()
    {
        var services = BaseServices();
        AddJwtAuth(services);
        AddGraphQL(services);

        (await InterceptorTypeAsync(services)).Should().Be(nameof(TraxCompositeSocketInterceptor));
    }

    [Test]
    public async Task JwtAuthAfterGraphQL_WiresTheCompositeInterceptor()
    {
        var services = BaseServices();
        AddGraphQL(services);
        AddJwtAuth(services);

        (await InterceptorTypeAsync(services)).Should().Be(nameof(TraxCompositeSocketInterceptor));
    }

    [Test]
    public async Task JwtAuthBeforeGraphQL_RefusesAnUnauthenticatedSubscriber()
    {
        await using var app = await StartHostAsync(authBeforeGraphQL: true);

        (await ConnectionInitTypeAsync(app)).Should().NotBe("connection_ack");
    }

    [Test]
    public async Task JwtAuthAfterGraphQL_RefusesAnUnauthenticatedSubscriber()
    {
        // This order used to compose a host whose subscriptions accepted every connection_init,
        // and later one that refused to start. The composite reads the schemes from the finished
        // container, so the order no longer matters, per
        // docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md.
        await using var app = await StartHostAsync(authBeforeGraphQL: false);

        (await ConnectionInitTypeAsync(app))
            .Should()
            .NotBe(
                "connection_ack",
                "a scheme registered after AddTraxGraphQL() is still enforced, per "
                    + "docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md"
            );
    }

    [Test]
    public async Task ApiKeyAuthAfterGraphQL_HostStarts()
    {
        var services = BaseServices();
        AddGraphQL(services);
        AddApiKeyAuth(services);

        var act = async () => await RunHostedServicesAsync(services);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task DefaultInterceptorWithASchemeRegistered_HostRefusesToStart()
    {
        // The backstop: whatever puts HotChocolate's accept-everything interceptor in front of a
        // host with a token scheme, the host does not start.
        var services = BaseServices();
        AddApiKeyAuth(services);
        services.AddTraxGraphQL(g =>
            g.AddDbContext<OrderTestDbContext>()
                .ConfigureSchema(b =>
                    b.AddSocketSessionInterceptor<DefaultSocketSessionInterceptor>()
                )
        );

        var act = async () => await RunHostedServicesAsync(services);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*AddTraxApiKeyAuth*")
            .WithMessage("*accepts every connection_init*");
    }

    [Test]
    public async Task NoAuthScheme_HostStartsFine()
    {
        // The validator must stay quiet for a host that never wanted subscription auth.
        var services = BaseServices();
        AddGraphQL(services);

        var act = async () => await RunHostedServicesAsync(services);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Queries — gating does not depend on order

    [Test]
    public async Task GatedQuery_AuthenticationBeforeGraphQL_RejectsAnonymous()
    {
        await using var app = await StartGatedHostAsync(authenticationFirst: true);

        (await GatedQueryErrorCodeAsync(app)).Should().Be("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task GatedQuery_AuthenticationAfterGraphQL_StillRejectsAnonymous()
    {
        // The regression guard for the application-service bridge. The interceptor that
        // populates HttpContext.User is built from the schema container, and deciding what to
        // bridge from a half-filled collection left it unable to activate at all, which
        // surfaced as a 500 on every request instead of an authorization error.
        await using var app = await StartGatedHostAsync(authenticationFirst: false);

        (await GatedQueryErrorCodeAsync(app)).Should().Be("TRAX_AUTHORIZATION");
    }

    [Test]
    public async Task GatedQuery_AuthorizeDirective_IsOnTheSchemaInBothOrders()
    {
        foreach (var authenticationFirst in new[] { true, false })
        {
            var executor = await GatedExecutorAsync(authenticationFirst);

            executor
                .Schema.ToString()
                .Should()
                .Contain(
                    "@authorize",
                    "the gate is attached to the schema, so it cannot depend on registration "
                        + $"order (authenticationFirst: {authenticationFirst})"
                );
        }
    }

    #endregion

    #region Mutations — the execution gate does not depend on order

    [Test]
    public async Task RequireAuthorization_AuthorizationServiceAfterGraphQL_HostStarts()
    {
        // RequireAuthorization() wires TraxGraphQLAuthInterceptor over the mutation surface, and
        // that interceptor needs IAuthorizationService out of the application container.
        var services = BaseServices();
        // Everything the gate needs is registered after the GraphQL builder.
        services.AddTraxGraphQL(g =>
            g.AddDbContext<GatedOrderTestDbContext>().RequireAuthorization(TestPolicy)
        );
        AddAuthorizationWithPolicy(services);

        var act = async () => await RunHostedServicesAsync(services);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RequireAuthorization_AuthorizationServiceAfterGraphQL_InterceptorActivates()
    {
        var services = BaseServices();
        // Everything the gate needs is registered after the GraphQL builder.
        services.AddTraxGraphQL(g =>
            g.AddDbContext<GatedOrderTestDbContext>().RequireAuthorization(TestPolicy)
        );
        AddAuthorizationWithPolicy(services);

        var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        // Activating it is the test: an unbridged dependency throws right here.
        var act = () => executor.Schema.Services.GetService<IHttpRequestInterceptor>();

        act.Should().NotThrow();
    }

    #endregion

    #region Helpers

    private static void AddGraphQL(IServiceCollection services) =>
        services.AddTraxGraphQL(g => g.AddDbContext<OrderTestDbContext>());

    private static void AddJwtAuth(IServiceCollection services) =>
        services.AddSingleton(Substitute.For<ITraxPrincipalResolver<JwtTokenInput>>());

    private static void AddApiKeyAuth(IServiceCollection services) =>
        services.AddSingleton(Substitute.For<ITraxPrincipalResolver<string>>());

    private const string TestPolicy = "RegistrationOrderTestPolicy";

    private static void AddAuthorizationWithPolicy(IServiceCollection services)
    {
        services.AddAuthentication();
        services.AddAuthorization(options =>
            options.AddPolicy(TestPolicy, policy => policy.RequireAssertion(_ => true))
        );
    }

    /// <summary>
    /// Starts every registered hosted service, which is where Trax's startup validators live,
    /// without standing up a web host.
    /// </summary>
    private static async Task RunHostedServicesAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    /// <summary>The interceptor HotChocolate would run for a connection_init on this host.</summary>
    private static async Task<string> InterceptorTypeAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        return executor.Schema.Services.GetService<ISocketSessionInterceptor>()?.GetType().Name
            ?? "<none>";
    }

    private static async Task<IRequestExecutor> GatedExecutorAsync(bool authenticationFirst)
    {
        var services = BaseServices();

        if (authenticationFirst)
        {
            services.AddAuthentication();
            services.AddAuthorization();
            services.AddTraxGraphQL(g => g.AddDbContext<GatedOrderTestDbContext>());
        }
        else
        {
            services.AddTraxGraphQL(g => g.AddDbContext<GatedOrderTestDbContext>());
            services.AddAuthentication();
            services.AddAuthorization();
        }

        var provider = services.BuildServiceProvider();
        return await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    /// <summary>
    /// Posts a gated query anonymously over the real pipeline and returns the error code the
    /// server answered with. Going over HTTP is the point: the gate reads HttpContext.User,
    /// which only exists once authentication middleware has run.
    /// </summary>
    private static async Task<string?> GatedQueryErrorCodeAsync(WebApplication app)
    {
        using var client = new HttpClient { BaseAddress = BaseAddress(app) };
        using var response = await client.PostAsync(
            "/trax/graphql",
            new StringContent(
                """{"query":"{ discover { gatedOrderTestWidgets { totalCount } } }"}""",
                Encoding.UTF8,
                "application/json"
            )
        );

        var body = await response.Content.ReadAsStringAsync();
        var errors = JsonDocument.Parse(body).RootElement.GetProperty("errors");
        return errors[0].GetProperty("extensions").GetProperty("code").GetString();
    }

    private static async Task<WebApplication> StartGatedHostAsync(bool authenticationFirst)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var services = builder.Services;
        AddBaseTo(services);
        services.AddRouting();

        void AddAuth()
        {
            services.AddAuthentication();
            services.AddAuthorization();
        }
        void AddGated() => services.AddTraxGraphQL(g => g.AddDbContext<GatedOrderTestDbContext>());

        if (authenticationFirst)
        {
            AddAuth();
            AddGated();
        }
        else
        {
            AddGated();
            AddAuth();
        }

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTraxGraphQL();
        await app.StartAsync();
        return app;
    }

    private static Uri BaseAddress(WebApplication app) =>
        new(
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First()
        );

    private static async Task<WebApplication> StartHostAsync(bool authBeforeGraphQL)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var services = builder.Services;
        AddBaseTo(services);
        services.AddRouting();

        if (authBeforeGraphQL)
        {
            AddJwtAuth(services);
            AddGraphQL(services);
        }
        else
        {
            AddGraphQL(services);
            AddJwtAuth(services);
        }

        var app = builder.Build();
        app.UseRouting();
        app.UseTraxGraphQL();
        await app.StartAsync();
        return app;
    }

    private static async Task<string?> ConnectionInitTypeAsync(WebApplication app)
    {
        var baseUri = BaseAddress(app);

        using var client = new ClientWebSocket();
        client.Options.AddSubProtocol("graphql-transport-ws");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.ConnectAsync(
            new Uri($"ws://{baseUri.Host}:{baseUri.Port}/trax/graphql"),
            cts.Token
        );

        await client.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"connection_init","payload":{}}"""),
            WebSocketMessageType.Text,
            true,
            cts.Token
        );

        var buffer = new byte[4096];
        var received = await client.ReceiveAsync(buffer, cts.Token);
        if (received.MessageType == WebSocketMessageType.Close)
            return "(closed)";

        return JsonDocument
            .Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))
            .RootElement.GetProperty("type")
            .GetString();
    }

    private static IServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        AddBaseTo(services);
        return services;
    }

    private static void AddBaseTo(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());

        var name = "OrderTest_" + Guid.NewGuid();
        services.AddDbContext<OrderTestDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddDbContext<GatedOrderTestDbContext>(o => o.UseInMemoryDatabase(name + "_gated"));
    }

    #endregion
}

public class OrderTestDbContext(DbContextOptions<OrderTestDbContext> options) : DbContext(options)
{
    public DbSet<OrderTestWidget> Widgets => Set<OrderTestWidget>();
}

public class GatedOrderTestDbContext(DbContextOptions<GatedOrderTestDbContext> options)
    : DbContext(options)
{
    public DbSet<GatedOrderTestWidget> Widgets => Set<GatedOrderTestWidget>();
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "orderTestWidgets")]
public class OrderTestWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[TraxAuthorize]
[TraxQueryModel(Name = "gatedOrderTestWidgets")]
public class GatedOrderTestWidget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
