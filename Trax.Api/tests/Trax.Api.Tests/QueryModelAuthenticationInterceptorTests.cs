using System.Security.Claims;
using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Authorization;

namespace Trax.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="QueryModelAuthenticationInterceptor"/>.
/// The interceptor populates <c>HttpContext.User</c> by walking every registered
/// authentication scheme; the E2E suite proves the happy path against real HC
/// infrastructure, while these tests pin the branches around it — the
/// short-circuit when the request is already authenticated, and the silent
/// no-op when no scheme matches the inbound credentials.
/// </summary>
[TestFixture]
public class QueryModelAuthenticationInterceptorTests
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
            authenticatedAs: new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "alice") },
                    authenticationType: "preauth"
                )
            )
        );

        var sut = new QueryModelAuthenticationInterceptor(schemeProvider);

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

        var httpContext = BuildHttpContext(authenticatedAs: null);

        var sut = new QueryModelAuthenticationInterceptor(schemeProvider);

        await sut.OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext.User.Identity!.IsAuthenticated.Should().BeFalse();
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
            authenticatedAs: null,
            successPrincipalForScheme: ("schemeA", winningPrincipal)
        );

        var sut = new QueryModelAuthenticationInterceptor(schemeProvider);

        await sut.OnCreateAsync(
            httpContext,
            Substitute.For<IRequestExecutor>(),
            OperationRequestBuilder.New(),
            CancellationToken.None
        );

        httpContext.User.Identity!.Name.Should().Be("winner");
        httpContext.User.Identity.AuthenticationType.Should().Be("schemeA");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an HttpContext wired with an IAuthenticationService whose
    /// AuthenticateAsync produces either NoResult (for every scheme) or a
    /// successful ticket only for a named scheme.
    /// </summary>
    private static HttpContext BuildHttpContext(
        ClaimsPrincipal? authenticatedAs,
        (string Scheme, ClaimsPrincipal Principal)? successPrincipalForScheme = null
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
