using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="TraxHttpAuthenticationInterceptor"/> when no endpoint policy is set.
/// The interceptor populates <c>HttpContext.User</c> by walking every registered
/// authentication scheme; the E2E suite proves the happy path against real HC
/// infrastructure, while these tests pin the branches around it — the
/// short-circuit when the request is already authenticated, and the silent
/// no-op when no scheme matches the inbound credentials.
///
/// <para>Enforces <c>docs/adr/0010-a-scheme-policy-requires-its-scheme.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0010-a-scheme-policy-requires-its-scheme.md")]
[TestFixture]
public class TraxHttpAuthenticationInterceptorTests
{
    [Test]
    public async Task OnCreateAsync_UserAlreadyAuthenticated_DoesNotWalkSchemes()
    {
        // Upstream middleware (e.g. endpoint-level RequireAuthorization or a
        // default-scheme UseAuthentication() pass) has already authenticated
        // the request. The interceptor must NOT walk schemes again — doing so
        // would double-authenticate, potentially overwriting a richer principal
        // with one from a fall-through scheme.
        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        var httpContext = BuildHttpContext(
            schemeProvider,
            authenticatedAs: new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "alice") },
                    authenticationType: "preauth"
                )
            )
        );

        var sut = new TraxHttpAuthenticationInterceptor(NoEndpointPolicy);

        await sut.OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        await schemeProvider.DidNotReceive().GetAllSchemesAsync();
        httpContext.User.Identity!.Name.Should().Be("alice");
        httpContext.User.Identity.AuthenticationType.Should().Be("preauth");
    }

    [Test]
    public async Task OnCreateAsync_NoSchemeSucceeds_LeavesUserAnonymous()
    {
        // None of the registered schemes recognise the request's credentials
        // (e.g. an anonymous request, or a request whose Bearer token does not
        // match any configured issuer). The interceptor must finish with the
        // anonymous principal intact so HC's @authorize directive can reject
        // the request on its own terms — not crash trying to read a partial
        // half-authenticated principal.
        var schemeA = new AuthenticationScheme(
            "schemeA",
            displayName: "A",
            typeof(NoResultAuthenticationHandler)
        );
        var schemeB = new AuthenticationScheme(
            "schemeB",
            displayName: "B",
            typeof(NoResultAuthenticationHandler)
        );

        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider.GetAllSchemesAsync().Returns(new[] { schemeA, schemeB });

        var httpContext = BuildHttpContext(schemeProvider, authenticatedAs: null);

        var sut = new TraxHttpAuthenticationInterceptor(NoEndpointPolicy);

        await sut.OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext
            .User.Identity!.IsAuthenticated.Should()
            .BeFalse(
                "a request no scheme authenticates stays anonymous, per docs/adr/0010-a-scheme-policy-requires-its-scheme.md"
            );
    }

    [Test]
    public async Task OnCreateAsync_FirstSchemeSucceeds_AssignsItsPrincipalAndStopsWalking()
    {
        // Two schemes are registered; the first one returns a successful
        // ticket. The interceptor must assign that principal and stop —
        // continuing into the second scheme could overwrite the principal
        // and would also waste cycles (e.g. JWT validation against a JWKS).
        var winningPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "winner") },
                authenticationType: "schemeA"
            )
        );

        var winningScheme = new AuthenticationScheme(
            "schemeA",
            displayName: "A",
            typeof(SuccessAuthenticationHandler)
        );
        var loserScheme = new AuthenticationScheme(
            "schemeB",
            displayName: "B",
            typeof(ThrowingAuthenticationHandler)
        );
        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider.GetAllSchemesAsync().Returns(new[] { winningScheme, loserScheme });

        var httpContext = BuildHttpContext(
            schemeProvider,
            authenticatedAs: null,
            successPrincipalForScheme: ("schemeA", winningPrincipal)
        );

        var sut = new TraxHttpAuthenticationInterceptor(NoEndpointPolicy);

        await sut.OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext.User.Identity!.Name.Should().Be("winner");
        httpContext.User.Identity.AuthenticationType.Should().Be("schemeA");
    }

    [Test]
    public async Task OnCreateAsync_UserWithNoIdentity_WalksTheSchemes()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "bob")], authenticationType: "schemeA")
        );
        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider
            .GetAllSchemesAsync()
            .Returns([
                new AuthenticationScheme("schemeA", "A", typeof(SuccessAuthenticationHandler)),
            ]);
        var httpContext = BuildHttpContext(
            schemeProvider,
            authenticatedAs: new ClaimsPrincipal(),
            successPrincipalForScheme: ("schemeA", principal)
        );

        await new TraxHttpAuthenticationInterceptor(NoEndpointPolicy).OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext.User.Identity!.Name.Should().Be("bob");
    }

    [Test]
    public async Task OnCreateAsync_EndpointPolicyNamingNoScheme_AuthenticatesWithAnyScheme()
    {
        // A policy that names no scheme leaves the choice to the registered schemes, as ASP.NET
        // Core's default scheme would, so the first one that authenticates the request wins.
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "carol")], authenticationType: "schemeA")
        );
        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider
            .GetAllSchemesAsync()
            .Returns([
                new AuthenticationScheme("schemeA", "A", typeof(SuccessAuthenticationHandler)),
            ]);
        var httpContext = BuildHttpContext(
            schemeProvider,
            authenticatedAs: null,
            successPrincipalForScheme: ("schemeA", principal),
            policy: p => p.RequireAuthenticatedUser()
        );

        await new TraxHttpAuthenticationInterceptor(EndpointPolicyNamed("SignedIn")).OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext
            .User.Identity!.Name.Should()
            .Be("carol", "per docs/adr/0010-a-scheme-policy-requires-its-scheme.md");
    }

    [Test]
    public async Task OnCreateAsync_EndpointPolicyNotRegistered_Throws()
    {
        var httpContext = BuildHttpContext(
            Substitute.For<IAuthenticationSchemeProvider>(),
            authenticatedAs: null,
            policy: null
        );

        var act = () =>
            new TraxHttpAuthenticationInterceptor(EndpointPolicyNamed("Missing"))
                .OnCreateAsync(
                    httpContext,
                    Substitute.For<IRequestExecutor>(),
                    OperationRequestBuilder.New(),
                    CancellationToken.None
                )
                .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'Missing'*");
    }

    private static GraphQLConfiguration EndpointPolicyNamed(string policy) =>
        new Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder.TraxGraphQLBuilder(
            new ServiceCollection()
        )
            .RequireAuthorization(policy)
            .Build();

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an HttpContext wired with an IAuthenticationService whose
    /// AuthenticateAsync produces either NoResult (for every scheme) or a
    /// successful ticket only for a named scheme.
    /// </summary>
    private static readonly GraphQLConfiguration NoEndpointPolicy = new([], [], [], []);

    private static HttpContext BuildHttpContext(
        IAuthenticationSchemeProvider schemeProvider,
        ClaimsPrincipal? authenticatedAs,
        (string Scheme, ClaimsPrincipal Principal)? successPrincipalForScheme = null,
        Action<Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder>? policy = null
    )
    {
        var ctx = new DefaultHttpContext();
        if (authenticatedAs is not null)
            ctx.User = authenticatedAs;

        var authService = Substitute.For<IAuthenticationService>();
        if (successPrincipalForScheme is { } match)
        {
            authService
                .AuthenticateAsync(ctx, match.Scheme)
                .Returns(
                    AuthenticateResult.Success(
                        new AuthenticationTicket(match.Principal, match.Scheme)
                    )
                );
        }
        // All other scheme names return NoResult — the default for a substitute
        // returns null which IAuthenticationService.AuthenticateAsync interprets
        // as failure, but to keep the path explicit we wire NoResult for any
        // unhandled scheme name.
        authService
            .AuthenticateAsync(ctx, Arg.Any<string>())
            .Returns(callInfo =>
            {
                if (successPrincipalForScheme is { } m && callInfo.ArgAt<string>(1) == m.Scheme)
                    return Task.FromResult(
                        AuthenticateResult.Success(new AuthenticationTicket(m.Principal, m.Scheme))
                    );
                return Task.FromResult(AuthenticateResult.NoResult());
            });

        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(schemeProvider);
        services.AddAuthorizationCore(o =>
        {
            if (policy is not null)
                o.AddPolicy("SignedIn", policy);
        });
        ctx.RequestServices = services.BuildServiceProvider();
        return ctx;
    }

    // The handler types below exist solely as type tokens on AuthenticationScheme.
    // Authentication is short-circuited via the IAuthenticationService substitute
    // wired into HttpContext.RequestServices, so these handlers never execute.
    private class NoResultAuthenticationHandler : IAuthenticationHandler
    {
        public Task<AuthenticateResult> AuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) =>
            Task.CompletedTask;
    }

    private sealed class SuccessAuthenticationHandler : NoResultAuthenticationHandler { }

    private sealed class ThrowingAuthenticationHandler : NoResultAuthenticationHandler { }
}
