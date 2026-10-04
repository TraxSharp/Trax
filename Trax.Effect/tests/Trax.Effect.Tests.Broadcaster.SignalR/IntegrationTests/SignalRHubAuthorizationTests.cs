using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Effect.Broadcaster.SignalR.Configuration.TraxTrainEventHubOptions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

/// <summary>
/// The train-event hub carries the host's authorization: it is mapped with a posture or the host
/// does not start, the posture decides who may connect, and the default projection keeps a
/// failure reason off the wire.
/// <para>Enforces <c>docs/adr/0016-the-train-event-hub-carries-the-hosts-authorization.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0016-the-train-event-hub-carries-the-hosts-authorization.md")]
[TestFixture]
public class SignalRHubAuthorizationTests
{
    private const string Adr =
        "(docs/adr/0016-the-train-event-hub-carries-the-hosts-authorization.md)";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task MappingTheHubWithoutAPosture_FailsAtStartup()
    {
        var start = async () => await StartHostAsync(_ => { });

        (
            await start
                .Should()
                .ThrowAsync<InvalidOperationException>(
                    "a hub every client can subscribe to must say who may connect " + Adr
                )
        )
            .Which.Message.Should()
            .Contain("authorization posture");
    }

    [Test]
    public async Task AllowAnonymousTogetherWithARequirement_FailsAtStartup()
    {
        var start = async () =>
            await StartHostAsync(hub => hub.AllowAnonymous().RequireAuthorization());

        await start.Should().ThrowAsync<InvalidOperationException>(Adr);
    }

    [Test]
    public async Task RequireAuthorization_RefusesAnAnonymousConnection()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization());
        await using var connection = Client(host, user: null);

        var connect = async () => await connection.StartAsync().WaitAsync(Timeout);

        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RequireAuthorization_WithAPolicy_RefusesAUserOutsideIt()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization("TraxEvents"));
        await using var connection = Client(host, user: "mallory");

        var connect = async () => await connection.StartAsync().WaitAsync(Timeout);

        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RequireRoles_AdmitsAUserInTheRole_AndRefusesOneOutsideIt()
    {
        using var host = await StartHostAsync(hub => hub.RequireRoles("Operator"));

        await using (var outsider = Client(host, user: "bob"))
        {
            var connect = async () => await outsider.StartAsync().WaitAsync(Timeout);
            (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
                .Which.StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
        }

        await using var operatorClient = Client(host, user: "alice", role: "Operator");
        await operatorClient.StartAsync().WaitAsync(Timeout);
        operatorClient.State.Should().Be(HubConnectionState.Connected);
    }

    [Test]
    public async Task AnAuthorizedClient_ReceivesAFailedEventWithoutItsFailureReason()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization("TraxEvents"));
        await using var connection = Client(host, user: "alice", scope: "trax.events");

        var received = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.On<JsonElement>("TrainEvent", payload => received.TrySetResult(payload));
        await connection.StartAsync().WaitAsync(Timeout);

        var dispatcher = host.Services.GetRequiredService<SignalRTrainEventDispatcher>();
        await dispatcher.HandleAsync(
            new TrainLifecycleEventMessage(
                MetadataId: 9,
                ExternalId: "failed-run",
                TrainName: "Some.ITrain",
                TrainState: "Failed",
                Timestamp: DateTime.UtcNow,
                FailureJunction: "ChargeCard",
                FailureReason: "The card was declined.",
                EventType: "Failed",
                Executor: null,
                Output: null
            ),
            CancellationToken.None
        );

        var payload = await received.Task.WaitAsync(Timeout);
        payload.GetProperty("externalId").GetString().Should().Be("failed-run");
        payload
            .TryGetProperty("failureReason", out _)
            .Should()
            .BeFalse("the default projection leaves a failure reason off the wire " + Adr);
    }

    [Test]
    public async Task AllowAnonymous_AdmitsAnyClient_AndLogsAWarning()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await StartHostAsync(hub => hub.AllowAnonymous(), logs);
        await using var connection = Client(host, user: null);

        await connection.StartAsync().WaitAsync(Timeout);

        connection.State.Should().Be(HubConnectionState.Connected);
        logs.Warnings.Should()
            .Contain(
                w => w.Contains("AllowAnonymous()"),
                "opening the hub is a choice the host makes loudly " + Adr
            );
    }

    [Test]
    public async Task RequireAuthorization_WithANullPolicyList_AdmitsAnyAuthenticatedUser()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization(null!));

        await using (var anonymous = Client(host, user: null))
        {
            var connect = async () => await anonymous.StartAsync().WaitAsync(Timeout);
            (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
                .Which.StatusCode.Should()
                .Be(HttpStatusCode.Unauthorized);
        }

        await using var user = Client(host, user: "alice");
        await user.StartAsync().WaitAsync(Timeout);
        user.State.Should().Be(HubConnectionState.Connected);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task RequireAuthorization_WithABlankPolicyName_FailsAtStartup(string? policy)
    {
        var start = async () =>
            await StartHostAsync(hub => hub.RequireAuthorization("TraxEvents", policy!));

        (await start.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should()
            .Be("policies");
    }

    [Test]
    public async Task RequireRoles_WithNoRoles_FailsAtStartup()
    {
        var start = async () => await StartHostAsync(hub => hub.RequireRoles());

        (await start.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should()
            .Contain("at least one role");
    }

    [Test]
    public async Task RequireRoles_WithANullRoleList_FailsAtStartup()
    {
        var start = async () => await StartHostAsync(hub => hub.RequireRoles(null!));

        (await start.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("roles");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task RequireRoles_WithABlankRoleName_FailsAtStartup(string? role)
    {
        var start = async () => await StartHostAsync(hub => hub.RequireRoles("Operator", role!));

        (await start.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should()
            .Contain("whitespace role names");
    }

    [Test]
    public async Task RequireAuthorization_OnAHostWithAFallbackPolicy_RefusesAUserTheFallbackRefuses()
    {
        using var host = await StartHostAsync(
            hub => hub.RequireAuthorization(),
            authorization: OperatorFallback
        );

        await using (var outsider = Client(host, user: "bob"))
        {
            var connect = async () => await outsider.StartAsync().WaitAsync(Timeout);
            (
                await connect
                    .Should()
                    .ThrowAsync<HttpRequestException>(
                        "a bare RequireAuthorization() keeps the host's fallback policy " + Adr
                    )
            )
                .Which.StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
        }

        await using var operatorClient = Client(host, user: "alice", role: "Operator");
        await operatorClient.StartAsync().WaitAsync(Timeout);
        operatorClient.State.Should().Be(HubConnectionState.Connected);
    }

    [Test]
    public async Task RequireAuthorization_OverWebSocketsWithoutNegotiation_RefusesAUserTheFallbackRefuses()
    {
        using var host = await StartHostAsync(
            hub => hub.RequireAuthorization(),
            authorization: OperatorFallback
        );

        await using (var outsider = WebSocketClient(host, user: "bob"))
        {
            var connect = async () => await outsider.StartAsync().WaitAsync(Timeout);
            await connect
                .Should()
                .ThrowAsync<Exception>(
                    "the posture is checked on the WebSocket upgrade as on negotiate " + Adr
                );
            outsider.State.Should().Be(HubConnectionState.Disconnected);
        }

        await using var operatorClient = WebSocketClient(host, user: "alice", role: "Operator");
        await operatorClient.StartAsync().WaitAsync(Timeout);
        operatorClient.State.Should().Be(HubConnectionState.Connected);
    }

    private const string ForeignOrigin = "https://elsewhere.example";

    [Test]
    public async Task AnAuthorizedClientFromAForeignOrigin_IsRefusedWith403()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization());
        await using var connection = Client(host, user: "alice", origin: ForeignOrigin);

        var connect = async () => await connection.StartAsync().WaitAsync(Timeout);

        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden, "valid credentials do not admit a foreign origin");
    }

    [Test]
    public async Task AnAuthorizedClientFromAForeignOrigin_IsRefusedOnAWebSocketWithoutNegotiation()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization());

        await using (var foreign = WebSocketClient(host, user: "alice", origin: ForeignOrigin))
        {
            var connect = async () => await foreign.StartAsync().WaitAsync(Timeout);
            await connect.Should().ThrowAsync<Exception>(Adr);
            foreign.State.Should().Be(HubConnectionState.Disconnected);
        }

        await using var own = WebSocketClient(host, user: "alice", origin: "http://localhost");
        await own.StartAsync().WaitAsync(Timeout);
        own.State.Should().Be(HubConnectionState.Connected);
    }

    [TestCase("POST", "hubs/trax-events/negotiate?negotiateVersion=1")]
    [TestCase("GET", "hubs/trax-events")]
    public async Task ARequestFromAForeignOrigin_IsAnswered403_OnEveryHubEndpoint(
        string method,
        string path
    )
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization());
        using var client = host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-User", "alice");
        request.Headers.Add("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request).WaitAsync(Timeout);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, Adr);
    }

    [Test]
    public async Task AClientFromTheHubsOwnOrigin_IsAdmitted()
    {
        using var host = await StartHostAsync(hub => hub.RequireAuthorization());
        await using var connection = Client(host, user: "alice", origin: "http://localhost");

        await connection.StartAsync().WaitAsync(Timeout);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Test]
    public async Task AnAllowedOrigin_IsAdmitted_AndAnyOtherIsRefused()
    {
        using var host = await StartHostAsync(hub =>
            hub.RequireAuthorization().AllowOrigins("HTTPS://App.Example:443")
        );

        await using (var allowed = Client(host, user: "alice", origin: "https://app.example"))
        {
            await allowed.StartAsync().WaitAsync(Timeout);
            allowed.State.Should().Be(HubConnectionState.Connected);
        }

        await using var other = Client(host, user: "alice", origin: ForeignOrigin);
        var connect = async () => await other.StartAsync().WaitAsync(Timeout);
        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task WithoutAllowOrigins_TheHostsCorsDefaultPolicyDecides()
    {
        using var host = await StartHostAsync(
            hub => hub.RequireAuthorization(),
            services: s =>
                s.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://app.example")))
        );

        await using (var allowed = Client(host, user: "alice", origin: "https://app.example"))
        {
            await allowed.StartAsync().WaitAsync(Timeout);
            allowed.State.Should().Be(HubConnectionState.Connected);
        }

        await using var other = Client(host, user: "alice", origin: ForeignOrigin);
        var connect = async () => await other.StartAsync().WaitAsync(Timeout);
        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AllowOriginsWithNoOrigins_IgnoresTheCorsDefaultPolicy()
    {
        using var host = await StartHostAsync(
            hub => hub.RequireAuthorization().AllowOrigins(),
            services: s => s.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin()))
        );
        await using var connection = Client(host, user: "alice", origin: ForeignOrigin);

        var connect = async () => await connection.StartAsync().WaitAsync(Timeout);

        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnAnonymousHub_StillRefusesAForeignOrigin()
    {
        using var host = await StartHostAsync(hub => hub.AllowAnonymous());
        await using var connection = Client(host, user: null, origin: ForeignOrigin);

        var connect = async () => await connection.StartAsync().WaitAsync(Timeout);

        (await connect.Should().ThrowAsync<HttpRequestException>(Adr))
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [TestCase("app.example")]
    [TestCase("https://app.example/path")]
    [TestCase("ftp://app.example")]
    [TestCase("null")]
    public async Task AllowOrigins_WithSomethingThatIsNotAnOrigin_FailsAtStartup(string origin)
    {
        var start = async () =>
            await StartHostAsync(hub => hub.RequireAuthorization().AllowOrigins(origin));

        (await start.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should()
            .Be("origins");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AConnectionWhoseAuthenticationExpires_IsClosed(bool hostTriesToTurnItOff)
    {
        using var host = await StartHostAsync(hub =>
            hub.RequireAuthorization()
                .ConfigureConnection(c =>
                {
                    if (hostTriesToTurnItOff)
                        c.CloseOnAuthenticationExpiration = false;
                })
        );
        await using var connection = Client(host, user: "alice", expiresInMs: 500);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await connection.StartAsync().WaitAsync(Timeout);

        var wait = async () => await closed.Task.WaitAsync(Timeout);
        await wait.Should()
            .NotThrowAsync(
                "a connection stops receiving once the authentication it was admitted on expires "
                    + Adr
            );
    }

    [Test]
    public async Task RequireAuthorization_WithAnUnknownPolicy_FailsAtMapping()
    {
        var start = async () => await StartHostAsync(hub => hub.RequireAuthorization("TraxEvnets"));

        (
            await start
                .Should()
                .ThrowAsync<InvalidOperationException>(
                    "a policy the host never registered is refused where the hub is mapped " + Adr
                )
        )
            .Which.Message.Should()
            .Contain("TraxEvnets");
    }

    [Test]
    public async Task RequireRoles_WithACommaInARoleName_FailsAtStartup()
    {
        var start = async () => await StartHostAsync(hub => hub.RequireRoles("Operator,User"));

        (
            await start
                .Should()
                .ThrowAsync<ArgumentException>(
                    "a role name is one role, never a list of them " + Adr
                )
        )
            .Which.ParamName.Should()
            .Be("roles");
    }

    private static void OperatorFallback(AuthorizationOptions options) =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole("Operator")
            .Build();

    private static async Task<IHost> StartHostAsync(
        Action<TraxTrainEventHubOptions> hub,
        ILoggerProvider? logs = null,
        Action<AuthorizationOptions>? authorization = null,
        Action<IServiceCollection>? services = null
    )
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(collection =>
                {
                    services?.Invoke(collection);
                    collection.AddLogging(l =>
                    {
                        if (logs is not null)
                            l.AddProvider(logs);
                    });
                    collection.AddSignalR();
                    collection.AddRouting();
                    collection
                        .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                            HeaderAuthenticationHandler.SchemeName,
                            _ => { }
                        );
                    collection.AddAuthorization(o =>
                    {
                        o.AddPolicy("TraxEvents", p => p.RequireClaim("scope", "trax.events"));
                        authorization?.Invoke(o);
                    });

                    var registry = new EffectRegistry();
                    collection.AddSingleton<IEffectRegistry>(registry);
                    new TraxBuilder(collection, registry).AddEffects(effects =>
                        effects.UseBroadcaster(b => b.UseSignalRHub())
                    );
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapTraxTrainEventHub(hub));
                })
        );

        return await builder.StartAsync();
    }

    /// <summary>
    /// A client that skips negotiation and opens a WebSocket straight away, so the posture is
    /// checked on the WebSocket upgrade request rather than on a negotiate call.
    /// </summary>
    private static HubConnection WebSocketClient(
        IHost host,
        string user,
        string? role = null,
        string? origin = null
    )
    {
        var server = host.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "hubs/trax-events"),
                options =>
                {
                    options.Transports = HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        var client = server.CreateWebSocketClient();
                        client.ConfigureRequest = request =>
                        {
                            request.Headers["X-Test-User"] = user;
                            if (role is not null)
                                request.Headers["X-Test-Role"] = role;
                            if (origin is not null)
                                request.Headers["Origin"] = origin;
                        };
                        return await client.ConnectAsync(context.Uri, cancellationToken);
                    };
                }
            )
            .Build();
        connection.HandshakeTimeout = TimeSpan.FromSeconds(5);
        return connection;
    }

    private static HubConnection Client(
        IHost host,
        string? user,
        string? role = null,
        string? scope = null,
        int? expiresInMs = null,
        string? origin = null
    )
    {
        var server = host.GetTestServer();
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "hubs/trax-events"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    if (user is not null)
                        options.Headers["X-Test-User"] = user;
                    if (role is not null)
                        options.Headers["X-Test-Role"] = role;
                    if (scope is not null)
                        options.Headers["X-Test-Scope"] = scope;
                    if (origin is not null)
                        options.Headers["Origin"] = origin;
                    if (expiresInMs is not null)
                        options.Headers["X-Test-Expires-In-Ms"] = expiresInMs.Value.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                }
            )
            .Build();
        // ADR 0014 (Trax.Docs): the handshake is bounded below the test's own ceiling.
        connection.HandshakeTimeout = TimeSpan.FromSeconds(5);
        return connection;
    }

    /// <summary>Authenticates a request that names its user, role and scope in headers.</summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Header";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            if (Request.Headers.TryGetValue("X-Test-Scope", out var scope))
                claims.Add(new Claim("scope", scope.ToString()));

            var properties = new AuthenticationProperties();
            if (Request.Headers.TryGetValue("X-Test-Expires-In-Ms", out var expiresIn))
                properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMilliseconds(
                    int.Parse(
                        expiresIn.ToString(),
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                );

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(principal, properties, SchemeName)
                )
            );
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                    return _warnings.ToList();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (logLevel != LogLevel.Warning)
                    return;
                lock (owner._warnings)
                    owner._warnings.Add(formatter(state, exception));
            }
        }
    }
}
