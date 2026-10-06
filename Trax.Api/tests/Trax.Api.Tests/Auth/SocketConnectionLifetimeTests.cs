using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using HotChocolate.AspNetCore.Subscriptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Every socket closes at the maximum connection lifetime, whatever authenticated it, and an
/// API-key socket closes within one re-check interval of its key no longer resolving to the
/// principal it connected as. A cookie socket closes when the sign-in it was opened with expires.
///
/// <para>Enforces <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md"
)]
[TestFixture]
public class SocketConnectionLifetimeTests
{
    private const string Adr =
        "docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md";

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan Recheck = TimeSpan.FromMinutes(5);

    private const string JwtIssuer = "https://socket-lifetime-issuer";
    private const string JwtAudience = "socket-lifetime";
    private static readonly byte[] JwtKey = Encoding.UTF8.GetBytes(new string('k', 32));

    private static readonly TraxPrincipal Alice = new(
        "alice",
        "Alice",
        ["Player"],
        new Dictionary<string, string> { ["tenant"] = "north" }
    );

    #region API-key sockets

    [Test]
    public async Task ApiKeySocket_WhoseKeyIsRevoked_ClosesWithinTheRecheckInterval()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        time.Advance(Recheck + TimeSpan.FromMinutes(1));
        Closes(session).Should().BeEmpty("the key still resolves to the same principal");

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>((TraxPrincipal?)null));
        time.Advance(Recheck);

        Closes(session)
            .Should()
            .ContainSingle("a revoked key's socket closes within one re-check interval, per " + Adr)
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public async Task ApiKeySocket_WhoseKeyNowResolvesToOtherRoles_Closes()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(Alice with { Roles = ["Admin"] }));
        time.Advance(Recheck);

        Closes(session)
            .Should()
            .ContainSingle(
                "the socket runs as the principal it connected as, so a changed one closes it, per "
                    + Adr
            )
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public async Task ApiKeySocket_WhoseKeyNowResolvesToOtherClaims_Closes()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new ValueTask<TraxPrincipal?>(
                    Alice with
                    {
                        Claims = new Dictionary<string, string> { ["tenant"] = "south" },
                    }
                )
            );
        time.Advance(Recheck);

        Closes(session)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public void SamePrincipal_ComparesWhatAPrincipalGrants()
    {
        var bare = new TraxPrincipal("alice", "Alice", ["Player"]);

        TraxApiKeySocketInterceptor
            .SamePrincipal(bare with { DisplayName = "A." }, bare)
            .Should()
            .BeTrue();
        TraxApiKeySocketInterceptor.SamePrincipal(null, bare).Should().BeFalse();
        TraxApiKeySocketInterceptor
            .SamePrincipal(bare with { Id = "bob" }, bare)
            .Should()
            .BeFalse();
        TraxApiKeySocketInterceptor
            .SamePrincipal(bare with { PrincipalType = "service" }, bare)
            .Should()
            .BeFalse();
        TraxApiKeySocketInterceptor
            .SamePrincipal(Alice, bare)
            .Should()
            .BeFalse("a claim was added");
    }

    [Test]
    public async Task ApiKeySocket_WhoseKeyNowResolvesWithOnlyANewDisplayName_StaysOpen()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(Alice with { DisplayName = "Alice B." }));
        time.Advance(Recheck);

        Closes(session).Should().BeEmpty("a display name grants nothing");
    }

    [Test]
    public async Task ApiKeySocket_WhoseResolverFailsOnRecheck_Closes()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("store unavailable"));
        time.Advance(Recheck);

        Closes(session)
            .Should()
            .ContainSingle("a key that cannot be re-checked is not trusted, per " + Adr)
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public async Task ApiKeySocket_WhoseKeyKeepsResolving_ClosesAtTheMaximumLifetime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var session = await ConnectWithKeyAsync(resolver, time);

        time.Advance(Lifetime - TimeSpan.FromMinutes(1));
        Closes(session).Should().BeEmpty();
        await resolver
            .Received(1 + 11)
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMinutes(2));
        Closes(session)
            .Should()
            .ContainSingle("every socket closes at the maximum lifetime, per " + Adr)
            .Which.Should()
            .Be(
                ConnectionCloseReason.EndpointUnavailable,
                "a lifetime close is Going Away, which a client reconnects after"
            );
    }

    [Test]
    public async Task ApiKeyInterceptor_UsedOnItsOwn_RechecksItsKey()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var resolver = ResolverReturning(Alice);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddScoped(_ => resolver);
        var interceptor = new TraxApiKeySocketInterceptor(
            new TraxApplicationServices(services.BuildServiceProvider()),
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<TraxApiKeySocketInterceptor>
                .Instance
        );
        var (session, _) = NewSession();
        (await interceptor.OnConnectAsync(session, KeyPayload())).Accepted.Should().BeTrue();

        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>((TraxPrincipal?)null));
        time.Advance(TraxCompositeSocketInterceptor.DefaultCredentialRecheckInterval);

        Closes(session)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    #endregion

    #region JWT sockets

    [Test]
    public async Task JwtSocket_WhoseTokenOutlivesTheLifetime_ClosesAtTheLifetime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var issuer = TestTokenIssuer.Symmetric(JwtIssuer, JwtAudience, JwtKey);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddTraxJwtAuth(jwt => jwt.UseSymmetricKey(JwtIssuer, JwtAudience, JwtKey));
        await using var sp = services.BuildServiceProvider();
        var (session, _) = NewSession();

        var status = await Composite(sp)
            .OnConnectAsync(
                session,
                Payload(
                    new TraxJwtSocketInterceptor.ConnectionInitPayload(
                        issuer.Mint(b =>
                            b.WithSubject("alice")
                                .WithExpires(time.GetUtcNow().AddHours(8).UtcDateTime)
                        ),
                        null
                    )
                )
            );
        status.Accepted.Should().BeTrue();

        time.Advance(Lifetime - TimeSpan.FromMinutes(1));
        Closes(session).Should().BeEmpty();

        time.Advance(TimeSpan.FromMinutes(1));
        Closes(session)
            .Should()
            .ContainSingle(
                "a JWT socket closes at the earlier of its token's exp and the lifetime, per " + Adr
            )
            .Which.Should()
            .Be(ConnectionCloseReason.EndpointUnavailable);
    }

    #endregion

    #region Sockets no token scheme authenticated

    [Test]
    public async Task SocketWithNoTokenScheme_ClosesAtTheMaximumLifetime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var interceptor = Composite(Services(time));
        var (session, _) = NewSession();
        (await interceptor.OnConnectAsync(session, EmptyPayload())).Accepted.Should().BeTrue();

        time.Advance(Lifetime - TimeSpan.FromSeconds(1));
        Closes(session).Should().BeEmpty();

        time.Advance(TimeSpan.FromSeconds(1));
        Closes(session)
            .Should()
            .ContainSingle(
                "a socket authenticated by its upgrade request has a lifetime too, per " + Adr
            )
            .Which.Should()
            .Be(ConnectionCloseReason.EndpointUnavailable);
    }

    [Test]
    public async Task CookieSocket_ClosesWhenItsSignInExpires()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var interceptor = Composite(Services(time));
        var (session, http) = NewSession();
        http.Features.Set<IAuthenticateResultFeature>(new SignIn(time.GetUtcNow().AddMinutes(10)));
        (await interceptor.OnConnectAsync(session, EmptyPayload())).Accepted.Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(9));
        Closes(session).Should().BeEmpty();

        time.Advance(TimeSpan.FromMinutes(1));
        Closes(session)
            .Should()
            .ContainSingle("the socket ends with the sign-in it was opened with, per " + Adr)
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public async Task CookieSocket_WhoseSignInOutlivesTheLifetime_ClosesAtTheLifetime()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var interceptor = Composite(Services(time));
        var (session, http) = NewSession();
        http.Features.Set<IAuthenticateResultFeature>(new SignIn(time.GetUtcNow().AddDays(14)));
        (await interceptor.OnConnectAsync(session, EmptyPayload())).Accepted.Should().BeTrue();

        time.Advance(Lifetime);

        Closes(session)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(ConnectionCloseReason.EndpointUnavailable);
    }

    [Test]
    public async Task RejectedConnection_IsNeverClosedLater()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var interceptor = Composite(Services(time, ResolverReturning(null)));
        var (session, _) = NewSession();
        (await interceptor.OnConnectAsync(session, KeyPayload())).Accepted.Should().BeFalse();

        time.Advance(Lifetime * 2);

        Closes(session).Should().BeEmpty("HotChocolate closes a rejected connection itself");
    }

    #endregion

    #region The timer

    [Test]
    public void DeadlineBeyondTheLongestTimerDue_IsReachedInSteps()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (session, _) = NewSession();
        Track(session, time)
            .CloseAt(
                time.GetUtcNow().AddDays(60),
                ConnectionCloseReason.PolicyViolation,
                "expired"
            );

        time.Advance(TimeSpan.FromDays(50));
        Closes(session).Should().BeEmpty("a timer cannot wait 60 days, so it waits in steps");

        time.Advance(TimeSpan.FromDays(10));
        Closes(session).Should().ContainSingle();
    }

    [Test]
    public void LaterDeadline_DoesNotReplaceAnEarlierOne()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (session, _) = NewSession();
        var lifetime = Track(session, time);

        lifetime.CloseAt(
            time.GetUtcNow().AddMinutes(10),
            ConnectionCloseReason.PolicyViolation,
            "expired"
        );
        lifetime.CloseAt(
            time.GetUtcNow().AddHours(1),
            ConnectionCloseReason.EndpointUnavailable,
            SocketConnectionLifetime.LifetimeMessage
        );
        time.Advance(TimeSpan.FromMinutes(10));

        Closes(session)
            .Should()
            .ContainSingle("the earliest deadline wins, per " + Adr)
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    [Test]
    public void ConnectionThatFailsToClose_DoesNotThrowFromTheTimer()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (session, _) = NewSession();
        session
            .Connection.CloseAsync(
                Arg.Any<string>(),
                Arg.Any<ConnectionCloseReason>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new System.Net.WebSockets.WebSocketException("already gone"));
        Track(session, time)
            .CloseAt(time.GetUtcNow(), ConnectionCloseReason.PolicyViolation, "expired");

        var advance = () => time.Advance(TimeSpan.Zero);

        advance.Should().NotThrow();
        Closes(session).Should().ContainSingle();
    }

    [Test]
    public void RecheckStillRunningAtTheDeadline_ClosesTheConnectionOnce()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (session, _) = NewSession();
        var lifetime = Track(session, time);
        var answer = new TaskCompletionSource<bool>();
        lifetime.RecheckEvery(Recheck, _ => new ValueTask<bool>(answer.Task));
        lifetime.CloseAt(
            time.GetUtcNow() + Recheck + TimeSpan.FromMinutes(1),
            ConnectionCloseReason.EndpointUnavailable,
            SocketConnectionLifetime.LifetimeMessage
        );

        time.Advance(Recheck);
        time.Advance(TimeSpan.FromMinutes(1));
        answer.SetResult(false);

        Closes(session)
            .Should()
            .ContainSingle("a re-check that answers after the connection closed changes nothing")
            .Which.Should()
            .Be(ConnectionCloseReason.EndpointUnavailable);
    }

    private static SocketConnectionLifetime Track(ISocketSession session, TimeProvider time) =>
        SocketConnectionLifetime.For(
            session.Connection,
            time,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
        );

    #endregion

    #region Over a real socket

    [Test]
    public async Task RealSocket_IsClosedWithGoingAwayAtItsLifetime()
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
            o.UseInMemoryDatabase("SocketLifetime_" + Guid.NewGuid())
        );
        services.AddTraxGraphQL(g =>
            g.AddDbContext<OrderTestDbContext>().MaxConnectionLifetime(TimeSpan.FromSeconds(1))
        );
        await using var app = builder.Build();
        app.UseRouting();
        app.UseTraxGraphQL();
        await app.StartAsync();

        var client = app.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var ws = await client.ConnectAsync(new Uri("ws://localhost/trax/graphql"), default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ws.SendAsync(
            Encoding.UTF8.GetBytes("""{"type":"connection_init","payload":{}}"""),
            WebSocketMessageType.Text,
            true,
            cts.Token
        );

        var buffer = new byte[4096];
        var ack = await ws.ReceiveAsync(buffer, cts.Token);
        Encoding.UTF8.GetString(buffer, 0, ack.Count).Should().Contain("connection_ack");

        // Bounded by the 10 s token: the server closes the socket at its 1 s lifetime.
        WebSocketReceiveResult received;
        do received = await ws.ReceiveAsync(buffer, cts.Token);
        while (received.MessageType != WebSocketMessageType.Close);

        ws.CloseStatus.Should()
            .Be(
                WebSocketCloseStatus.EndpointUnavailable,
                "the lifetime close is Going Away, per " + Adr
            );
    }

    #endregion

    #region Configuration

    [Test]
    public void Builder_Defaults_AreOneHourAndFiveMinutes()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxConnectionLifetimeValue.Should().Be(TimeSpan.FromHours(1));
        builder.ConnectionCredentialRecheckIntervalValue.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void Builder_Overrides_AreStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder
            .MaxConnectionLifetime(TimeSpan.FromMinutes(20))
            .RecheckConnectionCredentialsEvery(TimeSpan.FromSeconds(30));

        builder.MaxConnectionLifetimeValue.Should().Be(TimeSpan.FromMinutes(20));
        builder.ConnectionCredentialRecheckIntervalValue.Should().Be(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<TimeSpan> NonsensicalDurations()
    {
        yield return TimeSpan.Zero;
        yield return TimeSpan.FromSeconds(-1);
        yield return Timeout.InfiniteTimeSpan;
        yield return TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1);
        yield return TimeSpan.MaxValue;
    }

    [TestCaseSource(nameof(NonsensicalDurations))]
    public void Builder_MaxConnectionLifetime_RefusesANonsensicalValue(TimeSpan value)
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        ((Action)(() => builder.MaxConnectionLifetime(value)))
            .Should()
            .Throw<ArgumentOutOfRangeException>(
                "a socket's lifetime is always bounded, per " + Adr
            );
    }

    [TestCaseSource(nameof(NonsensicalDurations))]
    public void Builder_RecheckInterval_RefusesANonsensicalValue(TimeSpan value)
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        ((Action)(() => builder.RecheckConnectionCredentialsEvery(value)))
            .Should()
            .Throw<ArgumentOutOfRangeException>("an API key is always re-checked, per " + Adr);
    }

    [Test]
    public void Interceptor_RefusesANonsensicalLifetimeOrInterval()
    {
        var services = new TraxApplicationServices(new ServiceCollection().BuildServiceProvider());

        (
            (Action)(
                () => _ = new TraxCompositeSocketInterceptor(services, 1, TimeSpan.Zero, Recheck)
            )
        )
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        (
            (Action)(
                () => _ = new TraxCompositeSocketInterceptor(services, 1, Lifetime, TimeSpan.Zero)
            )
        )
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    private static ITraxPrincipalResolver<string> ResolverReturning(TraxPrincipal? principal)
    {
        var resolver = Substitute.For<ITraxPrincipalResolver<string>>();
        resolver
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TraxPrincipal?>(principal));
        return resolver;
    }

    private static IServiceProvider Services(
        TimeProvider time,
        ITraxPrincipalResolver<string>? resolver = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(time);
        if (resolver is not null)
            services.AddScoped(_ => resolver);
        return services.BuildServiceProvider();
    }

    private static TraxCompositeSocketInterceptor Composite(IServiceProvider services) =>
        new(new TraxApplicationServices(services), 100, Lifetime, Recheck);

    private static HotChocolate.AspNetCore.Subscriptions.Protocols.IOperationMessagePayload KeyPayload() =>
        Payload(new TraxApiKeySocketInterceptor.ConnectionInitPayload("key-1", null));

    private static async Task<ISocketSession> ConnectWithKeyAsync(
        ITraxPrincipalResolver<string> resolver,
        TimeProvider time
    )
    {
        var interceptor = Composite(Services(time, resolver));
        var (session, _) = NewSession();
        (await interceptor.OnConnectAsync(session, KeyPayload())).Accepted.Should().BeTrue();
        return session;
    }

    private static IEnumerable<ConnectionCloseReason> Closes(ISocketSession session) =>
        session
            .Connection.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISocketConnection.CloseAsync))
            .Select(call => call.GetArguments()[1])
            .OfType<ConnectionCloseReason>();

    /// <summary>The authentication result the cookie middleware leaves on an upgrade request.</summary>
    private sealed class SignIn(DateTimeOffset expires) : IAuthenticateResultFeature
    {
        public AuthenticateResult? AuthenticateResult { get; set; } =
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity("Cookies")
                    ),
                    new AuthenticationProperties { ExpiresUtc = expires },
                    "Cookies"
                )
            );
    }
}
