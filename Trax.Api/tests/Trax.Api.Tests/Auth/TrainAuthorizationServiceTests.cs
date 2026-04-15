using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.Exceptions;
using Trax.Api.Services.Authorization;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TrainAuthorizationServiceTests
{
    private const string AdminPolicy = "Admin";
    private const string TenantInternalPolicy = "TenantInternal";
    private const string AlwaysFailPolicy = "AlwaysFail";

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, p => p.RequireRole("Admin"))
            .AddPolicy(TenantInternalPolicy, p => p.RequireClaim("tenant", "internal"))
            .AddPolicy(AlwaysFailPolicy, p => p.RequireClaim("never-present-claim"));
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static TrainAuthorizationService CreateService(
        HttpContext? httpContext,
        ITrustedExecutionScope? trustedScope = null
    )
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return new TrainAuthorizationService(
            accessor,
            BuildAuthorizationService(),
            trustedScope ?? new TrustedExecutionScope(),
            NullLogger<TrainAuthorizationService>.Instance
        );
    }

    /// <summary>
    /// Asserts the call throws <see cref="TrainAuthorizationException"/> with the
    /// canonical public message. The caller's <paramref name="reasonPattern"/> is matched
    /// against <see cref="TrainAuthorizationException.Reason"/> (which the filter strips
    /// before forwarding). Supports FluentAssertions wildcard syntax.
    /// </summary>
    private static async Task AssertDeniedWithReason(Func<Task> act, string reasonPattern)
    {
        var assertion = await act.Should().ThrowAsync<TrainAuthorizationException>();
        assertion.Which.Message.Should().Be(TrainAuthorizationException.PublicMessage);
        assertion.Which.Reason.Should().Match(reasonPattern);
    }

    private static HttpContext AnonymousContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        return ctx;
    }

    private static HttpContext AuthenticatedContext(
        string name = "alice",
        IEnumerable<string>? roles = null,
        IEnumerable<KeyValuePair<string, string>>? claims = null
    )
    {
        var claimList = new List<Claim> { new(ClaimTypes.Name, name) };
        if (roles is not null)
            claimList.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (claims is not null)
            claimList.AddRange(claims.Select(kv => new Claim(kv.Key, kv.Value)));

        var identity = new ClaimsIdentity(claimList, "TestScheme");
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    private static TrainRegistration Registration(
        bool hasAuthorize = false,
        IReadOnlyList<string>? policies = null,
        IReadOnlyList<string>? roles = null,
        string serviceTypeName = "Test.ITestTrain"
    )
    {
        return new TrainRegistration
        {
            ServiceType = typeof(object),
            ImplementationType = typeof(object),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = serviceTypeName,
            ImplementationTypeName = "Test.TestTrain",
            InputTypeName = "object",
            OutputTypeName = "object",
            RequiredPolicies = policies ?? [],
            RequiredRoles = roles ?? [],
            HasAuthorizeAttribute = hasAuthorize,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    #region NoAuthorizeAttribute

    [Test]
    public async Task NoAttribute_Anonymous_Allows()
    {
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: false);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task NoAttribute_Authenticated_Allows()
    {
        var service = CreateService(AuthenticatedContext());
        var reg = Registration(hasAuthorize: false);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task NoAttribute_NoHttpContext_Allows()
    {
        var service = CreateService(httpContext: null);
        var reg = Registration(hasAuthorize: false);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task NoAttribute_EvenWithStaleRequiredCollections_Allows()
    {
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: false, policies: [AdminPolicy], roles: ["Admin"]);

        await service.AuthorizeAsync(reg);
    }

    #endregion

    #region BareAttribute

    [Test]
    public async Task BareAttribute_Anonymous_Denies()
    {
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: true);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No authenticated user*"
        );
    }

    [Test]
    public async Task BareAttribute_NoUserOnContext_Denies()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = null!;
        var service = CreateService(ctx);
        var reg = Registration(hasAuthorize: true);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No authenticated user*"
        );
    }

    [Test]
    public async Task BareAttribute_Authenticated_Allows()
    {
        var service = CreateService(AuthenticatedContext());
        var reg = Registration(hasAuthorize: true);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task BareAttribute_NoHttpContext_NoTrustedScope_Throws()
    {
        var service = CreateService(httpContext: null);
        var reg = Registration(hasAuthorize: true);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No request context and no trusted execution scope*"
        );
    }

    [Test]
    public async Task BareAttribute_NoHttpContext_TrustedScopeActive_Allows()
    {
        var scope = new TrustedExecutionScope();
        using var _ = scope.BeginTrusted("test.trusted");
        var service = CreateService(httpContext: null, trustedScope: scope);
        var reg = Registration(hasAuthorize: true);

        await service.AuthorizeAsync(reg);
    }

    #endregion

    #region PolicyOnly

    [Test]
    public async Task Policy_Satisfied_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Policy_NotSatisfied_Denies()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Player"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            $"*Policy '{AdminPolicy}' not satisfied*"
        );
    }

    [Test]
    public async Task Policy_Anonymous_Denies()
    {
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No authenticated user*"
        );
    }

    [Test]
    public async Task MultiplePolicies_AllSatisfied_Allows()
    {
        var service = CreateService(
            AuthenticatedContext(
                roles: ["Admin"],
                claims: [new KeyValuePair<string, string>("tenant", "internal")]
            )
        );
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy, TenantInternalPolicy]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task MultiplePolicies_OneMissing_Denies()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy, TenantInternalPolicy]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            $"*Policy '{TenantInternalPolicy}' not satisfied*"
        );
    }

    [Test]
    public async Task MultiplePolicies_FirstFailure_ReportsThatPolicy()
    {
        var service = CreateService(AuthenticatedContext());
        var reg = Registration(hasAuthorize: true, policies: [AlwaysFailPolicy, AdminPolicy]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            $"*Policy '{AlwaysFailPolicy}' not satisfied*"
        );
    }

    #endregion

    #region RolesOnly

    [Test]
    public async Task Role_UserHasOne_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin"]));
        var reg = Registration(hasAuthorize: true, roles: ["Admin", "Manager"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_UserHasAll_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin", "Manager"]));
        var reg = Registration(hasAuthorize: true, roles: ["Admin", "Manager"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_UserHasNone_Denies()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Player"]));
        var reg = Registration(hasAuthorize: true, roles: ["Admin", "Manager"]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*User lacks required role*Admin*Manager*"
        );
    }

    [Test]
    public async Task Role_Anonymous_Denies()
    {
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: true, roles: ["Admin"]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No authenticated user*"
        );
    }

    [Test]
    public async Task Role_SingleMatchIsEnough_OrSemantics()
    {
        // Verifies roles are OR'd, not AND'd.
        var service = CreateService(AuthenticatedContext(roles: ["Manager"]));
        var reg = Registration(hasAuthorize: true, roles: ["Admin", "Manager", "Auditor"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_CaseInsensitive_LowercaseClaim_UpperRequired_Allows()
    {
        // Discovery normalizes required roles to upper-invariant. The auth service
        // uppercases the user's role claims for comparison. A train declared with
        // Roles="Admin" and a principal with ClaimTypes.Role="admin" must match.
        var service = CreateService(AuthenticatedContext(roles: ["admin"]));
        var reg = Registration(hasAuthorize: true, roles: ["ADMIN"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_CaseInsensitive_UpperClaim_LowerRequired_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["ADMIN"]));
        // Simulating what the registration pipeline produces when someone writes
        // [TraxAuthorize(Roles="admin")]: the roles are normalized upper at discovery.
        var reg = Registration(hasAuthorize: true, roles: ["ADMIN"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_CaseInsensitive_MixedCasing_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["MaNaGeR"]));
        var reg = Registration(hasAuthorize: true, roles: ["MANAGER"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task Role_UsesClaimType_NotIsInRole()
    {
        // user.IsInRole() is ordinal case-sensitive. The service must enumerate
        // ClaimTypes.Role claims and compare upper-invariant instead.
        var service = CreateService(
            AuthenticatedContext(
                roles: ["editor"],
                claims: [new KeyValuePair<string, string>("custom-role", "admin")]
            )
        );
        var reg = Registration(hasAuthorize: true, roles: ["ADMIN"]);

        // "admin" only appears under a non-Role claim type; the check must deny.
        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*User lacks required role*"
        );
    }

    #endregion

    #region PolicyAndRole

    [Test]
    public async Task PolicyAndRole_BothSatisfied_Allows()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin", "Reader"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy], roles: ["Reader"]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task PolicyAndRole_PolicyFails_Denies()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Reader"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy], roles: ["Reader"]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            $"*Policy '{AdminPolicy}' not satisfied*"
        );
    }

    [Test]
    public async Task PolicyAndRole_RoleFails_Denies()
    {
        var service = CreateService(AuthenticatedContext(roles: ["Admin"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy], roles: ["Reader"]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*User lacks required role*Reader*"
        );
    }

    [Test]
    public async Task PolicyAndRole_PolicyCheckedBeforeRole()
    {
        // When both fail, the policy error surfaces first. Documents ordering.
        var service = CreateService(AuthenticatedContext(roles: ["Player"]));
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy], roles: ["Reader"]);

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            $"*Policy '{AdminPolicy}' not satisfied*"
        );
    }

    #endregion

    #region NoHttpContext

    [Test]
    public async Task NoHttpContext_WithRequirements_NoTrustedScope_Throws()
    {
        // Fail-closed: without an HttpContext AND without an active trusted scope,
        // the service denies. Callers that are genuinely trusted infrastructure must
        // opt in via ITrustedExecutionScope.BeginTrusted("...").
        var service = CreateService(httpContext: null);
        var reg = Registration(
            hasAuthorize: true,
            policies: [AdminPolicy, TenantInternalPolicy],
            roles: ["Admin"]
        );

        await AssertDeniedWithReason(
            async () => await service.AuthorizeAsync(reg),
            "*No request context and no trusted execution scope*"
        );
    }

    [Test]
    public async Task NoHttpContext_WithRequirements_TrustedScopeActive_Allows()
    {
        // Scheduler pipelines and remote workers open a trusted scope before
        // reaching the execution service; the check is then skipped.
        var scope = new TrustedExecutionScope();
        using var _ = scope.BeginTrusted("scheduler.remote-run");
        var service = CreateService(httpContext: null, trustedScope: scope);
        var reg = Registration(
            hasAuthorize: true,
            policies: [AdminPolicy, TenantInternalPolicy],
            roles: ["Admin"]
        );

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task NoHttpContext_NoAttribute_Allows()
    {
        var service = CreateService(httpContext: null);
        var reg = Registration(hasAuthorize: false);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task TrustedScope_DisposedBeforeCheck_DoesNotBypass()
    {
        var scope = new TrustedExecutionScope();
        scope.BeginTrusted("transient").Dispose();
        var service = CreateService(httpContext: null, trustedScope: scope);
        var reg = Registration(hasAuthorize: true);

        var act = async () => await service.AuthorizeAsync(reg);

        await act.Should().ThrowAsync<TrainAuthorizationException>();
    }

    [Test]
    public async Task TrustedScope_WithHttpContext_StillBypasses()
    {
        // When trusted scope is active, it takes precedence even if an HttpContext
        // is present. Matches the real request-handler flow where the scheduler's
        // HTTP endpoint serves a trusted call.
        var scope = new TrustedExecutionScope();
        using var _ = scope.BeginTrusted("scheduler.remote-run");
        var service = CreateService(AnonymousContext(), trustedScope: scope);
        var reg = Registration(hasAuthorize: true, policies: [AdminPolicy]);

        await service.AuthorizeAsync(reg);
    }

    [Test]
    public async Task TrustedScope_NestedFrames_InnerDisposeRestoresOuter()
    {
        var scope = new TrustedExecutionScope();
        using var outer = scope.BeginTrusted("outer");
        scope.IsTrusted.Should().BeTrue();
        scope.CurrentReason.Should().Be("outer");

        using (scope.BeginTrusted("inner"))
        {
            scope.CurrentReason.Should().Be("inner");
        }

        scope.IsTrusted.Should().BeTrue();
        scope.CurrentReason.Should().Be("outer");
    }

    [Test]
    public async Task TrustedScope_DoesNotLeakAcrossIndependentAsyncFlows()
    {
        var scope = new TrustedExecutionScope();

        var trustedFlow = Task.Run(async () =>
        {
            using var _ = scope.BeginTrusted("flow-a");
            await Task.Delay(50);
            return scope.IsTrusted;
        });

        var untrustedFlow = Task.Run(async () =>
        {
            await Task.Delay(10);
            return scope.IsTrusted;
        });

        (await trustedFlow).Should().BeTrue();
        (await untrustedFlow).Should().BeFalse();
    }

    #endregion

    #region ErrorMessageSurfacing

    [Test]
    public async Task TrainName_AvailableOnException_ButNotInPublicMessage()
    {
        // Diagnostic detail lives on the exception's TrainName / Reason properties
        // for server-side logging. The public Message is always the generic string
        // so the GraphQL error filter can forward it without leaking the train name.
        var service = CreateService(AnonymousContext());
        var reg = Registration(hasAuthorize: true, serviceTypeName: "MyNamespace.IDangerousTrain");

        var act = async () => await service.AuthorizeAsync(reg);

        var ex = (await act.Should().ThrowAsync<TrainAuthorizationException>()).Which;
        ex.TrainName.Should().Be("MyNamespace.IDangerousTrain");
        ex.Reason.Should().NotBeNullOrEmpty();
        ex.Message.Should().Be(TrainAuthorizationException.PublicMessage);
        ex.Message.Should().NotContain("MyNamespace.IDangerousTrain");
    }

    #endregion
}
