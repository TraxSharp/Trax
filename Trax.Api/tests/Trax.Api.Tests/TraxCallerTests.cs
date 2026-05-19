using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Auth;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests;

/// <summary>
/// Unit coverage for <see cref="TraxCaller"/> against the full state matrix it has
/// to handle: HttpContext present/absent × authenticated/anonymous × trusted scope
/// open/closed. The E2E suite exercises the wire-level invariants on a real server;
/// these tests pin the in-process semantics so a refactor that subtly changes
/// what <c>IsTrusted</c> or <c>Principal</c> returns fails loudly here.
/// </summary>
[TestFixture]
public class TraxCallerTests
{
    // ── HttpContext absent (scheduler / background services) ─────────────

    [Test]
    public void NoHttpContext_NoTrustedScope_AllFlagsFalse()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        sut.IsAuthenticated.Should().BeFalse();
        sut.IsTrusted.Should().BeFalse();
        sut.Principal.Should().BeNull();
    }

    [Test]
    public void NoHttpContext_InsideTrustedScope_IsTrustedTrueRestFalse()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        using (scope.BeginTrusted("test.scheduler-path"))
        {
            sut.IsTrusted.Should().BeTrue();
            sut.IsAuthenticated.Should().BeFalse();
            sut.Principal.Should().BeNull();
        }

        // Scope closes, flag returns to false.
        sut.IsTrusted.Should().BeFalse();
    }

    // ── HttpContext present, anonymous user ──────────────────────────────

    [Test]
    public void HttpContext_AnonymousUser_NoTrustedScope_AllFlagsFalse()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        sut.IsAuthenticated.Should().BeFalse();
        sut.IsTrusted.Should().BeFalse();
        sut.Principal.Should().BeNull();
    }

    [Test]
    public void HttpContext_AnonymousUser_TrustedScope_OnlyIsTrustedTrue()
    {
        // The "developer mistake" case: a junction inside an anonymous-handling
        // path opens BeginTrusted. TraxCaller honestly reports both flags;
        // filter authors can decide what to do (most should treat IsTrusted as
        // privileged framework code and let it through).
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        using (scope.BeginTrusted("test.in-anonymous-flow"))
        {
            sut.IsAuthenticated.Should().BeFalse();
            sut.IsTrusted.Should().BeTrue();
            sut.Principal.Should().BeNull();
        }
    }

    // ── HttpContext present, authenticated user ──────────────────────────

    [Test]
    public void HttpContext_AuthenticatedUser_NoTrustedScope_IsAuthenticatedTrue()
    {
        var ctx = BuildHttpContextWithPrincipal(id: "user-123", "Alice", roles: ["Admin"]);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        sut.IsAuthenticated.Should().BeTrue();
        sut.IsTrusted.Should().BeFalse();
        sut.Principal.Should().NotBeNull();
        sut.Principal!.Id.Should().Be("user-123");
        sut.Principal.DisplayName.Should().Be("Alice");
        sut.Principal.Roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Test]
    public void HttpContext_AuthenticatedUser_TrustedScope_BothFlagsTrue()
    {
        // Edge case but representable: an authenticated user is in a scope that
        // also got marked trusted (e.g., a framework call elevated mid-handler).
        // Both flags coexist; downstream code can decide which gates apply.
        var ctx = BuildHttpContextWithPrincipal(id: "user-99", "Bob", roles: []);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);

        using (scope.BeginTrusted("test.both"))
        {
            sut.IsAuthenticated.Should().BeTrue();
            sut.IsTrusted.Should().BeTrue();
            sut.Principal!.Id.Should().Be("user-99");
        }
    }

    // ── Live-read semantics ──────────────────────────────────────────────

    [Test]
    public void Principal_ReadAfterAuthenticationCompletes_ReflectsNewState()
    {
        // QueryModelAuthenticationInterceptor authenticates lazily on the first
        // GraphQL request. TraxCaller is scoped and was constructed before that
        // authentication finished. Reading Principal AFTER must return the now-
        // authenticated principal, not whatever the context held at construction.
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);
        sut.IsAuthenticated.Should().BeFalse();

        // Simulate the interceptor populating User after the fact.
        ctx.User = BuildTraxClaimsPrincipal(id: "late-auth", "Late Alice", roles: ["Player"]);

        sut.IsAuthenticated.Should().BeTrue();
        sut.Principal!.Id.Should().Be("late-auth");
    }

    [Test]
    public void IsTrusted_ChangesAsScopesOpenAndClose()
    {
        // AsyncLocal scope state evolves across the lifetime of one TraxCaller
        // (one DI scope). Live-read is the only correct behavior.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var scope = new TrustedExecutionScope();

        var sut = new TraxCaller(accessor, scope);
        sut.IsTrusted.Should().BeFalse();

        using (scope.BeginTrusted("outer"))
        {
            sut.IsTrusted.Should().BeTrue();
            using (scope.BeginTrusted("inner"))
            {
                sut.IsTrusted.Should().BeTrue();
            }
            sut.IsTrusted.Should().BeTrue(); // outer still open
        }

        sut.IsTrusted.Should().BeFalse();
    }

    // ── DI wiring ────────────────────────────────────────────────────────

    [Test]
    public void AddTraxPrincipalAccessor_RegistersTraxCallerAsScoped()
    {
        // The same idempotent extension method every auth scheme calls must
        // also register TraxCaller. A regression that drops the registration
        // would surface as DI-resolution failures across every host.
        var services = new ServiceCollection();
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();

        services.AddTraxPrincipalAccessor();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var caller = scope.ServiceProvider.GetRequiredService<TraxCaller>();
        caller.Should().NotBeNull();

        // Re-resolving in the same scope returns the same instance (Scoped).
        var second = scope.ServiceProvider.GetRequiredService<TraxCaller>();
        second.Should().BeSameAs(caller);
    }

    [Test]
    public void AddTraxPrincipalAccessor_CalledTwice_RegistersOnce()
    {
        // Auth schemes (ApiKey, Jwt, Oidc) all call AddTraxPrincipalAccessor.
        // Pinning idempotency keeps multi-scheme hosts safe.
        var services = new ServiceCollection();
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();

        services.AddTraxPrincipalAccessor();
        services.AddTraxPrincipalAccessor();

        var traxCallerRegistrations = services.Count(d => d.ServiceType == typeof(TraxCaller));
        traxCallerRegistrations.Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static HttpContext BuildHttpContextWithPrincipal(
        string id,
        string displayName,
        string[] roles
    ) => new DefaultHttpContext { User = BuildTraxClaimsPrincipal(id, displayName, roles) };

    private static ClaimsPrincipal BuildTraxClaimsPrincipal(
        string id,
        string displayName,
        string[] roles
    )
    {
        var principal = new TraxPrincipal(id, displayName, roles);
        return principal.ToClaimsPrincipal("test");
    }
}
