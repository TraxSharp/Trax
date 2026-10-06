using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Extensions;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// <c>UseTraxDashboard()</c> maps the dashboard only once the host has said who may use it: a
/// policy, roles, or an explicit <c>AllowAnonymousDashboard()</c>. The posture is applied to
/// every endpoint the dashboard's Razor components map, the pages and the Blazor circuit hub
/// alike, and the host can compose further conventions on the builder it returns.
///
/// <para>Enforces <c>docs/adr/0002-the-dashboard-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0002-the-dashboard-requires-an-authorization-posture.md")]
[TestFixture]
public class DashboardAuthorizationTests
{
    private const string Adr =
        "see docs/adr/0002-the-dashboard-requires-an-authorization-posture.md";

    // MapStaticAssets() reads <ApplicationName>.staticwebassets.endpoints.json from the base
    // directory. A test project has none, so each fixture run writes an empty one.
    private const string ApplicationName = "Trax.Dashboard.Tests.AuthorizationPosture";

    private static readonly string Manifest = Path.Combine(
        AppContext.BaseDirectory,
        $"{ApplicationName}.staticwebassets.endpoints.json"
    );

    private readonly List<WebApplication> _apps = [];

    [OneTimeSetUp]
    public void WriteStaticAssetsManifest() =>
        File.WriteAllText(Manifest, """{"Version":1,"ManifestType":"Build","Endpoints":[]}""");

    [OneTimeTearDown]
    public void DeleteStaticAssetsManifest() => File.Delete(Manifest);

    [TearDown]
    public async Task TearDown()
    {
        foreach (var app in _apps)
            await app.DisposeAsync();
    }

    [Test]
    public void Without_a_posture_UseTraxDashboard_refuses_to_start()
    {
        var app = Build(configure: null);

        var act = () => app.UseTraxDashboard();

        act.Should()
            .Throw<InvalidOperationException>(Adr)
            .WithMessage("*RequirePolicy*RequireRoles*AllowAnonymousDashboard*");
        DashboardEndpoints(app).Should().BeEmpty("nothing is mapped when the posture is missing");
    }

    [Test]
    public void A_policy_is_required_on_every_dashboard_endpoint()
    {
        var app = Build(o => o.RequirePolicy("DashboardAdmin"));

        app.UseTraxDashboard();

        var endpoints = DashboardEndpoints(app);
        endpoints.Should().NotBeEmpty();
        endpoints
            .Should()
            .OnlyContain(
                e =>
                    e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Any(a => a.Policy == "DashboardAdmin"),
                Adr
            );
    }

    [Test]
    public void The_circuit_hub_carries_the_posture_as_well_as_the_pages()
    {
        var app = Build(o => o.RequireRoles("Admin", "Operator"));

        app.UseTraxDashboard();

        var endpoints = DashboardEndpoints(app);
        endpoints.Should().Contain(e => Route(e).StartsWith("/_blazor"), "the premise");
        endpoints.Should().Contain(e => Route(e) == "/trax", "the premise");
        endpoints
            .Should()
            .OnlyContain(
                e =>
                    e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                        .Any(a => a.Roles == "Admin,Operator"),
                Adr
            );
    }

    [Test]
    public void A_policy_that_is_not_registered_fails_at_startup()
    {
        var app = Build(o => o.RequirePolicy("Missing"));

        var act = () => app.UseTraxDashboard();

        act.Should().Throw<InvalidOperationException>(Adr).WithMessage("*Missing*");
    }

    [Test]
    public void A_policy_with_no_authentication_scheme_to_challenge_with_fails_at_startup()
    {
        var app = Build(o => o.RequirePolicy("DashboardAdmin"), authentication: false);

        var act = () => app.UseTraxDashboard();

        act.Should()
            .Throw<InvalidOperationException>(
                "every request the posture refuses would otherwise answer 500, not 401 or 403"
            )
            .WithMessage("*no authentication scheme*AddAuthentication*");
        DashboardEndpoints(app).Should().BeEmpty();
    }

    [Test]
    public void Roles_with_no_authentication_scheme_to_challenge_with_fail_at_startup()
    {
        var app = Build(o => o.RequireRoles("Admin"), authentication: false);

        var act = () => app.UseTraxDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no authentication scheme*");
    }

    [Test]
    public void A_policy_naming_an_unregistered_scheme_fails_at_startup()
    {
        var app = Build(o => o.RequirePolicy("NamesAMissingScheme"));

        var act = () => app.UseTraxDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'NoSuchScheme'*");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void A_default_forbid_scheme_that_is_not_registered_fails_at_startup(bool roles)
    {
        var app = Build(
            o =>
            {
                if (roles)
                    o.RequireRoles("Admin");
                else
                    o.RequirePolicy("DashboardAdmin");
            },
            authentication: false,
            configureAuthentication: services =>
                services
                    .AddAuthentication(o =>
                    {
                        o.DefaultScheme = "Cookies";
                        o.DefaultForbidScheme = "NoSuchForbidScheme";
                    })
                    .AddCookie()
        );

        var act = () => app.UseTraxDashboard();

        act.Should()
            .Throw<InvalidOperationException>(
                "a signed-in caller the posture refuses would otherwise answer 500, not 403"
            )
            .WithMessage("*'NoSuchForbidScheme'*");
    }

    [Test]
    public void A_registered_default_forbid_scheme_starts()
    {
        var app = Build(
            o => o.RequireRoles("Admin"),
            authentication: false,
            configureAuthentication: services =>
                services
                    .AddAuthentication(o =>
                    {
                        o.DefaultScheme = "Cookies";
                        o.DefaultForbidScheme = "Cookies";
                    })
                    .AddCookie()
        );

        var act = () => app.UseTraxDashboard();

        act.Should().NotThrow();
    }

    [Test]
    public void AllowAnonymousDashboard_needs_no_authentication_scheme()
    {
        var app = Build(o => o.AllowAnonymousDashboard(), authentication: false);

        var act = () => app.UseTraxDashboard();

        act.Should().NotThrow();
    }

    [Test]
    public void AllowAnonymousDashboard_maps_it_ungated_and_logs_a_warning()
    {
        var logs = new ListLoggerProvider();
        var app = Build(o => o.AllowAnonymousDashboard(), logs: logs);

        app.UseTraxDashboard();

        DashboardEndpoints(app)
            .Should()
            .OnlyContain(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any());
        logs.Entries.Should()
            .Contain(
                e => e.Level == LogLevel.Warning && e.Message.Contains("AllowAnonymousDashboard"),
                Adr
            );
    }

    [Test]
    public void AllowAnonymousDashboard_with_a_policy_is_a_contradiction()
    {
        var act = () => Build(o => o.AllowAnonymousDashboard().RequirePolicy("DashboardAdmin"));

        act.Should().Throw<InvalidOperationException>(Adr).WithMessage("*contradict*");
    }

    [TestCase("")]
    [TestCase("  ")]
    public void A_blank_policy_is_refused(string policy)
    {
        var act = () => new DashboardOptions().RequirePolicy(policy);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void No_roles_is_refused()
    {
        var act = () => new DashboardOptions().RequireRoles();

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void The_returned_builder_composes_further_conventions()
    {
        var app = Build(o => o.RequirePolicy("DashboardAdmin"));

        app.UseTraxDashboard().RequireHost("admin.example.com");

        DashboardEndpoints(app)
            .Should()
            .OnlyContain(e =>
                e.Metadata.GetMetadata<IHostMetadata>() != null
                && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any()
            );
    }

    private WebApplication Build(
        Action<DashboardOptions>? configure,
        ListLoggerProvider? logs = null,
        bool authentication = true,
        Action<IServiceCollection>? configureAuthentication = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Production",
                ApplicationName = ApplicationName,
            }
        );
        builder.Services.AddSingleton<TraxMarker>();
        builder.Services.AddScoped(_ => UnusedService<IOperationsService>.Create());
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("DashboardAdmin", p => p.RequireRole("Admin"));
            o.AddPolicy(
                "NamesAMissingScheme",
                p => p.AddAuthenticationSchemes("NoSuchScheme").RequireRole("Admin")
            );
        });
        if (authentication)
            builder.Services.AddAuthentication().AddCookie();
        configureAuthentication?.Invoke(builder.Services);
        if (logs is not null)
            builder.Logging.AddProvider(logs);
        builder.AddTraxDashboard(configure);
        var app = builder.Build();
        _apps.Add(app);
        return app;
    }

    // The endpoints MapRazorComponents<App>() contributes: the pages and the circuit hub. The
    // static assets MapStaticAssets() maps are the host's as much as the dashboard's, and stay
    // outside the posture.
    private static List<RouteEndpoint> DashboardEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app)
            .DataSources.Where(d => d.GetType().Name.StartsWith("RazorComponentEndpointDataSource"))
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    private static string Route(RouteEndpoint e) => "/" + e.RoutePattern.RawText?.TrimStart('/');

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(Entries);

        public void Dispose() { }

        private sealed class ListLogger(List<(LogLevel, string)> entries) : ILogger
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
                lock (entries)
                    entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
