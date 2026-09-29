using System.Security.Claims;
using System.Text;
using FluentAssertions;
using HotChocolate.AspNetCore;
using HotChocolate.AspNetCore.Subscriptions;
using HotChocolate.AspNetCore.Subscriptions.Protocols;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// A subscription JWT is authenticated by the scheme's own <c>JwtBearerHandler</c>, as an HTTP
/// request carrying it would be: the host's <see cref="JwtBearerEvents"/> run, an unknown signing
/// key refreshes the JWKS, and the session is closed when the token expires.
///
/// <para>Enforces <c>docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md")]
[TestFixture]
public class SocketJwtRunsTheSchemeHandlerTests
{
    private const string Adr =
        "docs/adr/0022-a-socket-authenticates-through-the-schemes-handler.md";

    private const string Issuer = "https://socket-jwt-issuer";
    private const string Audience = "socket-jwt";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(new string('s', 32));

    private static readonly TestTokenIssuer Tokens = TestTokenIssuer.Symmetric(
        Issuer,
        Audience,
        Key
    );

    // The bearer handler maps "sub" to NameIdentifier unless MapInboundClaims is off.
    private static bool IsRevoked(ClaimsPrincipal? principal) =>
        (principal?.FindFirst(ClaimTypes.NameIdentifier) ?? principal?.FindFirst("sub"))?.Value
        == "revoked";

    private static TraxCompositeSocketInterceptor Composite(IServiceProvider services) =>
        new(new TraxApplicationServices(services));

    private static ServiceProvider SymmetricHost(
        Action<JwtBearerOptions>? customize = null,
        TimeProvider? time = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null)
            services.AddSingleton(time);
        services.AddTraxJwtAuth(jwt =>
        {
            jwt.UseSymmetricKey(Issuer, Audience, Key).WithClockSkew(TimeSpan.Zero);
            if (customize is not null)
                jwt.CustomizeBearerOptions(customize);
        });
        return services.BuildServiceProvider();
    }

    private static Task<ConnectionStatus> ConnectAsync(
        TraxCompositeSocketInterceptor interceptor,
        ISocketSession session,
        string token
    ) =>
        interceptor
            .OnConnectAsync(
                session,
                Payload(new TraxJwtSocketInterceptor.ConnectionInitPayload(token, null)),
                CancellationToken.None
            )
            .AsTask();

    [Test]
    public async Task A_host_OnTokenValidated_rejection_refuses_the_socket()
    {
        await using var sp = SymmetricHost(options =>
        {
            var trax = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                if (IsRevoked(context.Principal))
                {
                    context.Fail("This user has been revoked.");
                    return;
                }
                await trax(context);
            };
        });
        var interceptor = Composite(sp);

        var (revoked, revokedHttp) = NewSession();
        var refused = await ConnectAsync(
            interceptor,
            revoked,
            Tokens.Mint(b => b.WithSubject("revoked"))
        );
        refused
            .Accepted.Should()
            .BeFalse("the host's OnTokenValidated refused this token, per " + Adr);
        revokedHttp.User.Identity?.IsAuthenticated.Should().NotBe(true);

        var (alice, aliceHttp) = NewSession();
        var accepted = await ConnectAsync(
            interceptor,
            alice,
            Tokens.Mint(b => b.WithSubject("alice"))
        );
        accepted.Accepted.Should().BeTrue();
        aliceHttp
            .User.FindFirst(TraxAuthClaimTypes.PrincipalId)!
            .Value.Should()
            .Be("TraxJwt:alice");
        aliceHttp.User.Identity!.AuthenticationType.Should().Be(JwtDefaults.SchemeName);
    }

    [Test]
    public async Task A_host_OnMessageReceived_failure_refuses_the_socket()
    {
        await using var sp = SymmetricHost(options =>
            options.Events.OnMessageReceived = context =>
            {
                context.Fail("Tokens are not accepted from this client.");
                return Task.CompletedTask;
            }
        );

        var (session, _) = NewSession();
        var status = await ConnectAsync(
            Composite(sp),
            session,
            Tokens.Mint(b => b.WithSubject("alice"))
        );

        status
            .Accepted.Should()
            .BeFalse("the host's OnMessageReceived refused the request, per " + Adr);
    }

    [Test]
    public async Task A_dispatcher_routed_socket_runs_the_schemes_events()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth(
            "Customer",
            jwt =>
                jwt.UseSymmetricKey(Issuer, Audience, Key)
                    .CustomizeBearerOptions(options =>
                    {
                        var trax = options.Events.OnTokenValidated;
                        options.Events.OnTokenValidated = async context =>
                        {
                            if (IsRevoked(context.Principal))
                            {
                                context.Fail("This user has been revoked.");
                                return;
                            }
                            await trax(context);
                        };
                    })
        );
        services.AddTraxJwtDispatcher(d => d.MapIssuer(Issuer, "Customer"));
        await using var sp = services.BuildServiceProvider();
        var interceptor = Composite(sp);

        var (revoked, _) = NewSession();
        (await ConnectAsync(interceptor, revoked, Tokens.Mint(b => b.WithSubject("revoked"))))
            .Accepted.Should()
            .BeFalse("the routed scheme's OnTokenValidated refused this token, per " + Adr);

        var (alice, aliceHttp) = NewSession();
        (await ConnectAsync(interceptor, alice, Tokens.Mint(b => b.WithSubject("alice"))))
            .Accepted.Should()
            .BeTrue();
        aliceHttp.User.Identity!.AuthenticationType.Should().Be("Customer");
    }

    [Test]
    public async Task A_signing_key_published_after_the_first_connection_is_found()
    {
        await using var server = await TestJwksServer.StartAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxJwtAuth(jwt =>
            jwt.UseAuthority(server.Issuer, Audience).AllowHttpMetadata()
        );
        await using var sp = services.BuildServiceProvider();
        var interceptor = Composite(sp);

        // The first connection primes the scheme's cached JWKS.
        var (first, _) = NewSession();
        (
            await ConnectAsync(
                interceptor,
                first,
                server.CreateIssuer(Audience).Mint(b => b.WithSubject("alice"))
            )
        )
            .Accepted.Should()
            .BeTrue();

        // The issuer rotates to a key the cached JWKS does not have.
        server.AddSigningKey();
        var (second, _) = NewSession();
        var status = await ConnectAsync(
            interceptor,
            second,
            server.CreateIssuer(Audience).Mint(b => b.WithSubject("bob"))
        );

        status
            .Accepted.Should()
            .BeTrue("the handler refreshes the JWKS on an unknown key id, per " + Adr);
    }

    [Test]
    public async Task A_session_is_closed_when_its_token_expires()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        await using var sp = SymmetricHost(time: time);
        var expires = time.GetUtcNow().AddMinutes(10);

        var (session, _) = NewSession();
        var status = await ConnectAsync(
            Composite(sp),
            session,
            Tokens.Mint(b => b.WithSubject("alice").WithExpires(expires.UtcDateTime))
        );
        status.Accepted.Should().BeTrue();

        time.Advance(TimeSpan.FromMinutes(9));
        Closes(session)
            .Should()
            .BeEmpty("the token is still valid, so the session stays open, per " + Adr);

        time.Advance(TimeSpan.FromMinutes(2));
        Closes(session)
            .Should()
            .ContainSingle("the token expired, so the session is closed, per " + Adr)
            .Which.Should()
            .Be(ConnectionCloseReason.PolicyViolation);
    }

    private static IEnumerable<ConnectionCloseReason> Closes(ISocketSession session) =>
        session
            .Connection.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISocketConnection.CloseAsync))
            .Select(call => call.GetArguments()[1])
            .OfType<ConnectionCloseReason>();

    /// <summary>
    /// A clock the test moves by hand. Timers fire synchronously inside <see cref="Advance"/>.
    /// </summary>
    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers)
                _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            ManualTimer[] timers;
            lock (_timers)
                timers = [.. _timers];
            foreach (var timer in timers)
                timer.FireIfDue(_now);
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private DateTimeOffset? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_due is { } due && due <= now)
                {
                    _due = null;
                    callback(state);
                }
            }

            public void Dispose() => _due = null;

            public ValueTask DisposeAsync()
            {
                _due = null;
                return ValueTask.CompletedTask;
            }
        }
    }
}
