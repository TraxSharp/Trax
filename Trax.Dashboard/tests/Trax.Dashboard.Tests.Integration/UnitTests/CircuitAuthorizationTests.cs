using System.Security.Claims;
using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Components.Shared;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Services.Authorization;
using Trax.Effect.Configuration.TraxBuilder;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// The posture is not checked only when the circuit connects. Inside an established circuit it is
/// re-checked against the host's authentication state whenever that state changes, on an interval
/// and on demand before a write; once the user no longer satisfies it, every further message the
/// circuit receives is refused and the page reloads through the endpoint posture.
///
/// <para>Enforces <c>docs/adr/0002-the-dashboard-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0002-the-dashboard-requires-an-authorization-posture.md")]
[TestFixture]
public class CircuitAuthorizationTests
{
    private const string Adr =
        " (see docs/adr/0002-the-dashboard-requires-an-authorization-posture.md)";

    private Bunit.TestContext _ctx = null!;
    private HostAuthenticationState _auth = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        // The real authorization services, and a provider standing in for the host's: one that
        // revalidates, like ASP.NET Identity's, and reports when the user changes.
        _ctx.Services.AddAuthorizationCore();
        // bUnit pre-registers placeholders that refuse every check; replace them.
        _ctx.Services.AddSingleton<IAuthorizationService, DefaultAuthorizationService>();
        _ctx.Services.AddSingleton<
            IAuthorizationPolicyProvider,
            DefaultAuthorizationPolicyProvider
        >();
        _auth = new HostAuthenticationState();
        _auth.SetUser("operator", "Admin");
        _ctx.Services.AddSingleton<AuthenticationStateProvider>(_auth);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_user_whose_role_is_revoked_mid_circuit_has_the_next_action_refused()
    {
        Use(o => o.RequireRoles("Admin"));
        var (authorization, inbound) = AttachedCircuit();
        var actions = 0;

        await inbound(() => actions++);
        actions.Should().Be(1, "the premise: an Admin may act");

        _auth.SetUser("operator", "Viewer");
        await authorization.RevalidateAsync();

        var act = () => inbound(() => actions++);
        await act.Should().ThrowAsync<UnauthorizedAccessException>(Adr);
        actions.Should().Be(1, "the refused action never ran" + Adr);
    }

    [Test]
    public async Task An_authentication_change_is_acted_on_without_waiting_for_the_interval()
    {
        Use(o => o.RequireRoles("Admin"));
        var (authorization, inbound) = AttachedCircuit();

        _auth.SetAnonymous();

        await WaitUntil(() => authorization.IsRefused);
        var act = () => inbound(() => { });
        await act.Should().ThrowAsync<UnauthorizedAccessException>(Adr);
    }

    [Test]
    public async Task A_policy_that_reads_live_state_is_re_run_on_revalidation()
    {
        var allowed = true;
        _ctx.Services.AddSingleton<IAuthorizationHandler>(new LiveHandler(() => allowed));
        _ctx.Services.Configure<AuthorizationOptions>(o =>
            o.AddPolicy("Live", p => p.AddRequirements(new LiveRequirement()))
        );
        Use(o => o.RequirePolicy("Live"));
        var (authorization, _) = AttachedCircuit();
        (await authorization.RevalidateAsync()).Should().BeTrue("the premise");

        allowed = false;

        (await authorization.RevalidateAsync()).Should().BeFalse(Adr);
        authorization.IsRefused.Should().BeTrue();
    }

    [Test]
    public async Task A_write_checks_the_posture_before_it_runs()
    {
        Use(o => o.RequireRoles("Admin"));
        var authorization = _ctx.Services.GetRequiredService<DashboardCircuitAuthorization>();
        (await authorization.EnsureAuthorizedAsync()).Should().BeTrue("the premise");

        _auth.SetUser("operator", "Viewer");

        (await authorization.EnsureAuthorizedAsync()).Should().BeFalse(Adr);
    }

    [Test]
    public async Task A_refused_circuit_reloads_the_page_through_the_endpoint_posture()
    {
        Use(o => o.RequireRoles("Admin"));
        _ctx.RenderComponent<DashboardAuthorizationMonitor>();
        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();

        _auth.SetUser("operator", "Viewer");

        await WaitUntil(() => navigation.History.Any(h => h.Options.ForceLoad));
        navigation.History.Should().Contain(h => h.Options.ForceLoad, Adr);
    }

    [Test]
    public async Task A_circuit_the_dashboard_never_attached_is_left_alone()
    {
        // A host's own Blazor circuit, which never renders the dashboard's root component.
        Use(o => o.RequireRoles("Admin"));
        _auth.SetUser("operator", "Viewer");
        var inbound = InboundHandler();
        var actions = 0;

        await inbound(() => actions++);

        actions.Should().Be(1);
    }

    [Test]
    public async Task Without_an_authentication_state_provider_the_circuit_is_refused()
    {
        var ctx = new Bunit.TestContext();
        try
        {
            ctx.Services.AddAuthorizationCore();
            ctx.Services.AddSingleton(new DashboardOptions().RequireRoles("Admin"));
            ctx.Services.AddScoped<DashboardCircuitAuthorization>();
            var authorization = ctx.Services.GetRequiredService<DashboardCircuitAuthorization>();

            authorization.Attach();

            authorization.IsRefused.Should().BeTrue("a posture that cannot be read fails closed");
            (await authorization.EnsureAuthorizedAsync()).Should().BeFalse();
        }
        finally
        {
            ctx.Dispose();
        }
    }

    [Test]
    public async Task AllowAnonymousDashboard_is_never_refused()
    {
        Use(o => o.AllowAnonymousDashboard());
        _auth.SetAnonymous();
        var (authorization, inbound) = AttachedCircuit();

        (await authorization.EnsureAuthorizedAsync()).Should().BeTrue();
        await inbound(() => { });
    }

    [Test]
    public void AddTraxDashboard_registers_the_circuit_check()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();

        services.AddTraxDashboard(o => o.RequireRoles("Admin"));

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(CircuitHandler)
                && d.ImplementationType == typeof(DashboardAuthorizationCircuitHandler)
                && d.Lifetime == ServiceLifetime.Scoped
            );
        services
            .Should()
            .NotContain(
                d =>
                    d.ServiceType == typeof(AuthenticationStateProvider)
                    && d.ImplementationType != null
                    && d.ImplementationType.Assembly == typeof(DashboardOptions).Assembly,
                "the dashboard reads the host's provider and never replaces it" + Adr
            );
    }

    private void Use(Action<DashboardOptions> posture)
    {
        var options = new DashboardOptions();
        posture(options);
        _ctx.Services.AddSingleton(options);
        _ctx.Services.AddScoped<DashboardCircuitAuthorization>();
        _ctx.Services.AddScoped<DashboardAuthorizationCircuitHandler>();
    }

    private (
        DashboardCircuitAuthorization Authorization,
        Func<Action, Task> Inbound
    ) AttachedCircuit()
    {
        var authorization = _ctx.Services.GetRequiredService<DashboardCircuitAuthorization>();
        authorization.Attach();
        return (authorization, InboundHandler());
    }

    // What the framework runs for every message the circuit receives: the handler's pipeline
    // around the message's own work.
    private Func<Action, Task> InboundHandler()
    {
        var handler = _ctx.Services.GetRequiredService<DashboardAuthorizationCircuitHandler>();
        return work =>
        {
            var pipeline = handler.CreateInboundActivityHandler(_ =>
            {
                work();
                return Task.CompletedTask;
            });
            return pipeline(null!);
        };
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("The condition was not met within 10 seconds.");
            await Task.Yield();
        }
    }

    private sealed class HostAuthenticationState : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity()));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(_state);

        public void SetUser(string name, params string[] roles)
        {
            var claims = roles
                .Select(r => new Claim(ClaimTypes.Role, r))
                .Append(new Claim(ClaimTypes.Name, name));
            Set(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        }

        public void SetAnonymous() => Set(new ClaimsPrincipal(new ClaimsIdentity()));

        private void Set(ClaimsPrincipal user)
        {
            _state = new AuthenticationState(user);
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }

    private sealed class LiveRequirement : IAuthorizationRequirement;

    private sealed class LiveHandler(Func<bool> allowed) : AuthorizationHandler<LiveRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            LiveRequirement requirement
        )
        {
            if (allowed())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
