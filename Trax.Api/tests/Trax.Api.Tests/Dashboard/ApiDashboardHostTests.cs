using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trax.Api.Dashboard.Configuration;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.Dashboard.Serving;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Dashboard;

/// <summary>
/// A host serves the dashboard with one call: its page with the runtime config written in, its
/// files, the page again for any route of the app, a 404 for a file it does not have, and the
/// security and caching headers on every response. It refuses to start without an authorization
/// posture, and the posture gates every request for the app.
///
/// <para>Guard for <c>docs/adr/0039-the-api-dashboard-requires-an-authorization-posture.md</c>.</para>
/// </summary>
/// <remarks>
/// Most tests serve a small stand-in build, so they run without node; the last one serves the app
/// the build embedded, when it did.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Property("adr", "docs/adr/0039-the-api-dashboard-requires-an-authorization-posture.md")]
public partial class ApiDashboardHostTests
{
    private const string Adr =
        "docs/adr/0039-the-api-dashboard-requires-an-authorization-posture.md";

    private const string IndexTemplate = """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <title>Trax Dashboard</title>
            <script type="module" crossorigin src="./assets/index-3f2a9c.js"></script>
            <link rel="stylesheet" crossorigin href="./assets/index-77b1d0.css">
          </head>
          <body><div id="root"></div></body>
        </html>
        """;

    private static DashboardAssets StandInBuild() =>
        new(
            new Dictionary<string, byte[]>
            {
                ["index.html"] = Encoding.UTF8.GetBytes(IndexTemplate),
                ["assets/index-3f2a9c.js"] = Encoding.UTF8.GetBytes("console.log('app');"),
                ["assets/index-77b1d0.css"] = Encoding.UTF8.GetBytes("body{margin:0}"),
                ["favicon.svg"] = Encoding.UTF8.GetBytes("<svg/>"),
            }
        );

    // ---- Serving ----------------------------------------------------------------------------

    [Test]
    public async Task The_page_is_served_with_its_base_and_the_GraphQL_path_written_in()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, "/trax/dashboard/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("<base href=\"/trax/dashboard/\" />");
        Config(html).Should().Be(("/trax/dashboard/", "/trax/graphql"));
        html.IndexOf("<base", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                html.IndexOf("./assets/", StringComparison.Ordinal),
                "the base must precede every relative URL it resolves"
            );
        response.Headers.CacheControl!.NoCache.Should().BeTrue("a new build must be seen at once");
        response
            .Headers.CacheControl.Private.Should()
            .BeTrue("the page names who loaded it, so no shared cache may keep it");
    }

    [Test]
    public async Task The_bare_path_redirects_to_itself_with_a_slash_keeping_the_query()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, "/trax/dashboard?x=1");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/trax/dashboard/?x=1");
    }

    [Test]
    public async Task A_deep_link_gets_the_page_with_the_same_base()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, "/trax/dashboard/executions/42");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("<base href=\"/trax/dashboard/\" />");
        Config(html).Should().Be(("/trax/dashboard/", "/trax/graphql"));
    }

    [TestCase("/trax/dashboard/trains/Trax.Samples.Orders.IShipOrderTrain")]
    [TestCase("/trax/dashboard/trains/Trax.Samples.Orders.IShipOrderTrain/")]
    [TestCase(
        "/trax/dashboard/state-machines/Trax.Samples.Orders.OrderMachine/Trax.Samples.Orders.Order/order.42"
    )]
    [TestCase("/trax/dashboard/state-machines/Trax.Samples.Orders.OrderMachine")]
    public async Task A_reload_of_a_route_naming_a_dotted_NET_name_gets_the_page(string path)
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, path);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a train or machine name is not a file");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        Config(await response.Content.ReadAsStringAsync())
            .Should()
            .Be(("/trax/dashboard/", "/trax/graphql"));
    }

    [TestCase("assets/index-000000.js", true)]
    [TestCase("assets/Trax.Samples.Foo", true)]
    [TestCase("assets/fonts/missing.woff2", true)]
    [TestCase("favicon.ico", false)]
    [TestCase("executions/app.js", false)]
    [TestCase("robots.txt", false)]
    [TestCase("manifests/nightly.log", false)]
    [TestCase("state-machines/m/o/report.zip", false)]
    [TestCase("trains/Trax.Samples.Orders.IShipOrderTrain", false)]
    [TestCase("trains/Trax.Samples.Json", false)]
    [TestCase("state-machines/Orders.OrderMachine/Orders.Order/order.42", false)]
    [TestCase("trains/Trax.Samples.Orders.", false)]
    [TestCase("executions/42", false)]
    [TestCase("Assets/index-000000.js", false)]
    public void A_missing_path_names_a_file_only_under_assets(string relative, bool isFile) =>
        DashboardEndpoint.IsFileRequest(relative).Should().Be(isFile, relative);

    [TestCase("/trax/dashboard/state-machines/m/o/report.zip")]
    [TestCase("/trax/dashboard/manifests/nightly.log")]
    [TestCase("/trax/dashboard/state-machines/Orders.OrderMachine/Orders.Order/invoice.pdf")]
    [TestCase("/trax/dashboard/trains/Trax.Samples.Orders.app.js")]
    public async Task A_reload_of_a_route_whose_id_ends_in_a_file_extension_gets_the_page(
        string path
    )
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, path);

        response
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, "an id is not a file because it ends in .zip or .log");
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        Config(await response.Content.ReadAsStringAsync())
            .Should()
            .Be(("/trax/dashboard/", "/trax/graphql"));
    }

    [Test]
    public async Task A_hashed_asset_is_served_with_its_type_and_cached_for_good()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, "/trax/dashboard/assets/index-3f2a9c.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        (await response.Content.ReadAsStringAsync()).Should().Be("console.log('app');");
        response.Headers.CacheControl!.ToString().Should().Contain("immutable");
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response
            .Headers.CacheControl.Public.Should()
            .BeTrue("an anonymous shell nobody signed in to may be kept by shared caches");
    }

    [Test]
    public async Task Under_a_posture_a_hashed_asset_is_cached_by_the_browser_alone()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
            auth: AuthSetup.SchemeOnly
        );

        var asset = await GetAsync(
            app,
            "/trax/dashboard/assets/index-3f2a9c.js",
            user: "ada;Admin"
        );
        var page = await GetAsync(app, "/trax/dashboard/", user: "ada;Admin");

        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset
            .Headers.CacheControl!.Private.Should()
            .BeTrue($"{Adr}: a shared cache would hand a gated file to anyone");
        asset.Headers.CacheControl.Public.Should().BeFalse();
        asset.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        page.Headers.CacheControl!.Private.Should().BeTrue();
        page.Headers.CacheControl.NoCache.Should().BeTrue();
    }

    [Test]
    public async Task Under_an_anonymous_shell_a_signed_in_caller_gets_private_assets()
    {
        await using var app = await StartAsync(e => Anonymous(e), auth: AuthSetup.FallbackPolicy);

        var asset = await GetAsync(app, "/trax/dashboard/assets/index-3f2a9c.js", user: "ops");

        asset.StatusCode.Should().Be(HttpStatusCode.OK);
        asset
            .Headers.CacheControl!.Private.Should()
            .BeTrue("the fallback policy gated it, so no shared cache may keep it");
    }

    [Test]
    public async Task A_file_outside_assets_is_revalidated_because_its_name_is_not_its_content()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, "/trax/dashboard/favicon.svg");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [TestCase("/trax/dashboard/assets/index-000000.js")]
    [TestCase("/trax/dashboard/assets/index-000000.css")]
    [TestCase("/trax/dashboard/assets/nested/missing.png")]
    public async Task A_missing_file_is_a_404_never_the_page(string path)
    {
        await using var app = await StartAsync(e => Anonymous(e));

        var response = await GetAsync(app, path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<html");
    }

    [Test]
    public async Task An_unchanged_page_or_asset_answers_304_to_its_etag()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        foreach (var path in new[] { "/trax/dashboard/", "/trax/dashboard/assets/index-3f2a9c.js" })
        {
            var first = await GetAsync(app, path);
            var etag = first.Headers.ETag!;

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.IfNoneMatch.Add(etag);
            var second = await app.GetTestClient().SendAsync(request);

            second.StatusCode.Should().Be(HttpStatusCode.NotModified, path);
        }
    }

    [Test]
    public async Task A_HEAD_request_gets_the_headers_and_no_body()
    {
        await using var app = await StartAsync(e => Anonymous(e));

        using var request = new HttpRequestMessage(HttpMethod.Head, "/trax/dashboard/");
        var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [TestCase("GET", "/trax/dashboard/", null, HttpStatusCode.OK)]
    [TestCase("GET", "/trax/dashboard/executions/42", null, HttpStatusCode.OK)]
    [TestCase("GET", "/trax/dashboard/assets/index-3f2a9c.js", null, HttpStatusCode.OK)]
    [TestCase("GET", "/trax/dashboard/assets/index-000000.js", null, HttpStatusCode.NotFound)]
    [TestCase("GET", "/trax/dashboard", null, HttpStatusCode.Redirect)]
    [TestCase("POST", "/trax/dashboard/", null, HttpStatusCode.MethodNotAllowed)]
    [TestCase("GET", "/trax/dashboard/", "gated", HttpStatusCode.Unauthorized)]
    [TestCase(
        "GET",
        "/trax/dashboard/assets/index-3f2a9c.js",
        "gated",
        HttpStatusCode.Unauthorized
    )]
    [TestCase("GET", "/trax/dashboard/", "gated;ops;Viewer", HttpStatusCode.Forbidden)]
    [TestCase("POST", "/trax/dashboard/", "gated", HttpStatusCode.Unauthorized)]
    [TestCase("DELETE", "/trax/dashboard/runs", "gated;ada;Admin", HttpStatusCode.MethodNotAllowed)]
    [TestCase("GET", "/trax/dashboard/", "gated;ada;Admin", HttpStatusCode.OK)]
    public async Task Every_response_carries_the_security_headers(
        string method,
        string path,
        string? posture,
        HttpStatusCode status
    )
    {
        // "gated" serves the app behind RequireRoles("Admin"); what follows it signs the caller in.
        var gated = posture?.StartsWith("gated") == true;
        var user = posture?.Split(';', 2) is [_, var who] ? who : null;
        await using var app = gated
            ? await StartAsync(
                e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
                auth: AuthSetup.SchemeOnly
            )
            : await StartAsync(e => Anonymous(e));

        var response = await SendAsync(app, new HttpMethod(method), path, user);

        response.StatusCode.Should().Be(status);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        var csp = Header(response, "Content-Security-Policy");
        csp.Should().Contain("default-src 'none'");
        csp.Should().Contain("script-src 'self';");
        csp.Should().Contain("style-src 'self';");
        csp.Should().Contain("font-src 'self';");
        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("base-uri 'self'");
        csp.Should().Contain("connect-src 'self' ws://localhost wss://localhost;");
        csp.Should().NotContain("unsafe-inline").And.NotContain("unsafe-eval");
        if (status == HttpStatusCode.MethodNotAllowed)
            response.Content.Headers.Allow.Should().BeEquivalentTo(["GET", "HEAD"]);
    }

    [Test]
    public async Task The_host_s_other_routes_do_not_get_the_dashboard_s_headers()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
            auth: AuthSetup.SchemeOnly
        );

        var response = await GetAsync(app, "/elsewhere");

        Header(response, "Content-Security-Policy").Should().BeEmpty();
        Header(response, "X-Frame-Options").Should().BeEmpty();
    }

    // ---- Who loaded the page ----------------------------------------------------------------

    [Test]
    public async Task The_page_is_the_same_whoever_loads_it_and_no_shared_cache_keeps_it()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
            auth: AuthSetup.SchemeOnly
        );

        var ada = await GetAsync(app, "/trax/dashboard/", "ada;Admin");
        var grace = await GetAsync(app, "/trax/dashboard/", "grace;Admin");
        var adaPage = await ada.Content.ReadAsStringAsync();

        ConfigKeys(adaPage)
            .Should()
            .BeEquivalentTo(
                ["basePath", "graphqlPath"],
                "the app holds its credential in memory, so the page names nobody"
            );
        adaPage.Should().NotContain("ada");
        (await grace.Content.ReadAsStringAsync()).Should().Be(adaPage);
        grace.Headers.ETag.Should().Be(ada.Headers.ETag);
        ada.Headers.CacheControl!.Private.Should().BeTrue("no shared cache may keep the shell");
        ada.Headers.CacheControl.NoCache.Should().BeTrue();
    }

    [Test]
    public void A_host_header_that_is_not_a_plain_host_does_not_reach_the_policy()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString(
            "evil.example; script-src *"
        );

        DashboardEndpoint
            .ContentSecurityPolicy(context.Request)
            .Should()
            .Contain("connect-src 'self';")
            .And.NotContain("evil.example");
    }

    [Test]
    public async Task The_app_talks_to_the_path_UseTraxGraphQL_mapped_even_when_mapped_after_it()
    {
        await using var app = await StartAsync(e => Anonymous(e), graphQLPath: "/api/graphql");

        var html = await (await GetAsync(app, "/trax/dashboard/")).Content.ReadAsStringAsync();

        Config(html).GraphQLPath.Should().Be("/api/graphql");
    }

    [Test]
    public async Task GraphQLPath_names_the_endpoint_when_the_host_maps_it_another_way()
    {
        await using var app = await StartAsync(e =>
            e.UseTraxApiDashboard(o =>
            {
                o.AllowAnonymousShell();
                o.GraphQLPath = "/custom/graphql";
            })
        );

        var html = await (await GetAsync(app, "/trax/dashboard/")).Content.ReadAsStringAsync();

        Config(html).GraphQLPath.Should().Be("/custom/graphql");
    }

    [Test]
    public async Task A_GraphQLPath_on_another_origin_is_refused()
    {
        var act = () =>
            StartAsync(e =>
                e.UseTraxApiDashboard(o =>
                {
                    o.AllowAnonymousShell();
                    o.GraphQLPath = "https://elsewhere.example/graphql";
                })
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a path on this host*");
    }

    [Test]
    public async Task Under_a_path_base_the_base_and_the_GraphQL_path_include_it()
    {
        await using var app = await StartAsync(e => Anonymous(e), pathBase: "/ops");

        var html = await (
            await GetAsync(app, "/ops/trax/dashboard/runs")
        ).Content.ReadAsStringAsync();

        Config(html).Should().Be(("/ops/trax/dashboard/", "/ops/trax/graphql"));
    }

    [Test]
    public async Task Another_path_is_normalized_and_serves_the_same_app()
    {
        await using var app = await StartAsync(e =>
            e.UseTraxApiDashboard(o => o.AllowAnonymousShell(), path: "admin/ops/")
        );

        var html = await (await GetAsync(app, "/admin/ops/")).Content.ReadAsStringAsync();

        Config(html).BasePath.Should().Be("/admin/ops/");
        (await GetAsync(app, "/trax/dashboard/")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestCase("/")]
    [TestCase("")]
    [TestCase("/trax/{id}")]
    public async Task A_path_that_is_not_a_literal_path_of_its_own_is_refused(string path)
    {
        var act = () => StartAsync(e => e.UseTraxApiDashboard(o => o.AllowAnonymousShell(), path));

        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*cannot be the dashboard's path*");
    }

    [Test]
    public void The_runtime_config_is_encoded_so_a_path_cannot_break_out_of_the_tag()
    {
        var html = DashboardEndpoint.RenderIndex(
            IndexTemplate,
            new Microsoft.AspNetCore.Http.PathString("/a\"><script>x</script>"),
            "/trax/graphql",
            new Dictionary<string, string>()
        );

        html.Should().NotContain("<script>x</script>");
        html.Should().NotContain("\"><script");
    }

    [Test]
    public void The_runtime_config_names_each_documents_id_only_when_the_host_registered_them()
    {
        var path = new Microsoft.AspNetCore.Http.PathString("/trax/dashboard");

        var without = DashboardEndpoint.RenderIndex(
            IndexTemplate,
            path,
            "/trax/graphql",
            new Dictionary<string, string>()
        );
        var with = DashboardEndpoint.RenderIndex(
            IndexTemplate,
            path,
            "/trax/graphql",
            new Dictionary<string, string> { ["Overview"] = "trax_console_Overview_abc" }
        );

        without.Should().NotContain("documents");
        with.Should().Contain("documents").And.Contain("trax_console_Overview_abc");
    }

    [Test]
    public void The_package_carries_the_documents_the_app_sends()
    {
        ConsoleDocumentsResource
            .Documents.Select(d => d.Name)
            .Should()
            .Contain(["Overview", "OnJunctionEvent", "RequeueDeadLetter"])
            .And.NotContain("PersistedOperations", "management runs as text behind its own gate");
    }

    // ---- What it needs ----------------------------------------------------------------------

    [Test]
    public async Task Without_AddTraxGraphQL_it_refuses_to_start_naming_it()
    {
        var act = () => StartAsync(e => Anonymous(e), addGraphQL: false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires AddTraxGraphQL()*");
    }

    [Test]
    public async Task Without_the_operations_namespace_it_refuses_to_start_naming_it()
    {
        var act = () => StartAsync(e => Anonymous(e), exposeOperations: false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*operations namespace*ExposeOperationQueries()*");
    }

    [Test]
    public async Task A_build_without_the_app_refuses_to_start_saying_how_to_build_it()
    {
        var act = () =>
            StartAsync(
                e => Anonymous(e),
                assets: new DashboardAssets(new Dictionary<string, byte[]>())
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*carries no app*npm ci*");
    }

    // ---- The authorization posture ----------------------------------------------------------

    [Test]
    public async Task Without_a_posture_it_refuses_to_start_naming_the_three()
    {
        var act = () => StartAsync(e => e.UseTraxApiDashboard(_ => { }));

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                $"{Adr}: the dashboard maps nothing until the host chooses who may load it"
            )
            .WithMessage("*RequirePolicy*RequireRoles*AllowAnonymousShell*");
    }

    [Test]
    public void AllowAnonymousShell_and_a_policy_contradict_each_other()
    {
        var options = new TraxApiDashboardOptions().RequireRoles("Admin");

        var act = () => options.AllowAnonymousShell();

        act.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
        new TraxApiDashboardOptions()
            .AllowAnonymousShell()
            .Invoking(o => o.RequirePolicy("Admin"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*contradict*");
    }

    [Test]
    public async Task A_policy_that_is_not_registered_fails_at_startup()
    {
        var act = () =>
            StartAsync(
                e => e.UseTraxApiDashboard(o => o.RequirePolicy("TraxAdmin")),
                auth: AuthSetup.SchemeOnly
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*policy 'TraxAdmin', which is not registered*");
    }

    [Test]
    public async Task A_posture_with_no_scheme_to_challenge_with_fails_at_startup()
    {
        var act = () =>
            StartAsync(
                e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
                auth: AuthSetup.AuthorizationOnly
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no authentication scheme to challenge*");
    }

    [Test]
    public async Task RequireRoles_challenges_a_stranger_forbids_another_role_and_serves_the_role()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")),
            auth: AuthSetup.SchemeOnly
        );

        foreach (var path in new[] { "/trax/dashboard/", "/trax/dashboard/assets/index-3f2a9c.js" })
        {
            (await GetAsync(app, path))
                .StatusCode.Should()
                .Be(HttpStatusCode.Unauthorized, $"{Adr}: the posture gates {path}");
            (await GetAsync(app, path, user: "ops;Viewer"))
                .StatusCode.Should()
                .Be(HttpStatusCode.Forbidden, path);
            (await GetAsync(app, path, user: "ada;Admin"))
                .StatusCode.Should()
                .Be(HttpStatusCode.OK, path);
        }
    }

    [Test]
    public async Task RequirePolicy_gates_every_request_with_the_named_policy()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequirePolicy("TraxAdmin")),
            auth: AuthSetup.SchemeAndPolicy
        );

        (await GetAsync(app, "/trax/dashboard/runs"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await GetAsync(app, "/trax/dashboard/runs", user: "ops;Viewer"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await GetAsync(app, "/trax/dashboard/runs", user: "ada;Admin"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AllowAnonymousShell_serves_anyone_and_warns_on_start()
    {
        var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(e => Anonymous(e), logs: logs);

        (await GetAsync(app, "/trax/dashboard/")).StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("AllowAnonymousShell()")
                && entry.Message.Contains("/trax/dashboard")
            );
    }

    [Test]
    public async Task AllowAnonymousShell_leaves_a_fallback_policy_in_force()
    {
        await using var app = await StartAsync(e => Anonymous(e), auth: AuthSetup.FallbackPolicy);

        (await GetAsync(app, "/trax/dashboard/"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
        (await GetAsync(app, "/trax/dashboard/", user: "ops;Viewer"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AllowAnonymous_on_the_returned_builder_fails_at_startup_under_a_posture()
    {
        var act = () =>
            StartAsync(
                e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")).AllowAnonymous(),
                auth: AuthSetup.SchemeOnly
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                $"{Adr}: AllowAnonymous() would remove the posture, not add to it"
            )
            .WithMessage("*AllowAnonymous()*removes*AllowAnonymousShell()*");
    }

    [Test]
    public async Task AllowAnonymous_on_an_anonymous_shell_is_the_host_s_choice()
    {
        await using var app = await StartAsync(
            e => Anonymous(e).AllowAnonymous(),
            auth: AuthSetup.FallbackPolicy
        );

        (await GetAsync(app, "/trax/dashboard/"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, "the host opted the shell out of its fallback policy itself");
    }

    [Test]
    public async Task The_returned_builder_adds_conventions_on_top_of_the_posture()
    {
        await using var app = await StartAsync(
            e => e.UseTraxApiDashboard(o => o.RequireRoles("Admin")).RequireHost("admin.example"),
            auth: AuthSetup.SchemeOnly
        );

        (await GetAsync(app, "/trax/dashboard/", user: "ada;Admin"))
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound, "the host does not match");
        (await GetAsync(app, "/trax/dashboard/", user: "ada;Admin", host: "admin.example"))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await GetAsync(app, "/trax/dashboard/", host: "admin.example"))
            .StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized, "the posture still applies");
    }

    // ---- The app the build embedded ---------------------------------------------------------

    [Test]
    public async Task The_embedded_app_is_served_with_every_file_its_page_names()
    {
        if (DashboardAssets.Embedded.IndexTemplate is null)
            Unavailable(
                "This build carries no app: Trax.Api/src/Trax.Api.Dashboard/ClientApp has no "
                    + "node_modules."
            );

        await using var app = await StartAsync(e => Anonymous(e), assets: DashboardAssets.Embedded);

        var page = await GetAsync(app, "/trax/dashboard/");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        Config(html).Should().Be(("/trax/dashboard/", "/trax/graphql"));

        var files = AssetReference().Matches(html).Select(m => m.Groups["path"].Value).ToList();
        files.Should().NotBeEmpty().And.OnlyContain(f => f.StartsWith("./assets/"));
        foreach (var file in files)
        {
            var response = await GetAsync(app, "/trax/dashboard/" + file[2..]);
            response.StatusCode.Should().Be(HttpStatusCode.OK, file);
            response.Headers.CacheControl!.ToString().Should().Contain("immutable", file);
            Regex
                .IsMatch(file, @"-[A-Za-z0-9_-]{6,}\.(js|css)$")
                .Should()
                .BeTrue($"{file} is named by its content");
        }
    }

    /// <summary>
    /// Skips on a developer's machine without the app's npm packages, and fails under CI, which
    /// installs them for this folder: a skip there would report green without ever serving the app.
    /// </summary>
    private static void Unavailable(string why)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail(
                $"{why} CI installs it (npm ci in that folder before the build), so this test "
                    + "must run there."
            );
        Assert.Ignore($"{why} Run `npm ci --ignore-scripts` there and build again to run this.");
    }

    // ---- The host ---------------------------------------------------------------------------

    private static IEndpointConventionBuilder Anonymous(IEndpointRouteBuilder endpoints) =>
        endpoints.UseTraxApiDashboard(o => o.AllowAnonymousShell());

    private enum AuthSetup
    {
        None,
        AuthorizationOnly,
        SchemeOnly,
        SchemeAndPolicy,
        FallbackPolicy,
    }

    private static async Task<WebApplication> StartAsync(
        Func<IEndpointRouteBuilder, IEndpointConventionBuilder> map,
        AuthSetup auth = AuthSetup.None,
        string graphQLPath = "/trax/graphql",
        string? pathBase = null,
        DashboardAssets? assets = null,
        bool addGraphQL = true,
        bool exposeOperations = true,
        CapturingLoggerProvider? logs = null
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        if (logs is not null)
            builder.Logging.AddProvider(logs);

        var services = builder.Services;
        services.AddSingleton(assets ?? StandInBuild());
        if (addGraphQL)
        {
            services.AddSingleton<TraxMarker>();
            services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
            services.AddSingleton(Substitute.For<IEffectRegistry>());
            services.AddSingleton(Substitute.For<ITraxScheduler>());
            services.AddSingleton(Substitute.For<IOperationsService>());
            services.AddSingleton(Substitute.For<ITrainExecutionService>());
            services.AddSingleton(Substitute.For<ITraxHealthService>());
            services.AddDbContext<OrderTestDbContext>(o =>
                o.UseInMemoryDatabase("Dashboard_" + Guid.NewGuid())
            );
            services.AddTraxGraphQL(g =>
                exposeOperations
                    ? g.AddDbContext<OrderTestDbContext>()
                        .ExposeOperationQueries()
                        .AllowAnonymousOperations()
                    : g.AddDbContext<OrderTestDbContext>()
            );
        }

        if (auth != AuthSetup.None)
            services.AddAuthorization(o =>
            {
                if (auth == AuthSetup.SchemeAndPolicy)
                    o.AddPolicy("TraxAdmin", p => p.RequireRole("Admin"));
                if (auth == AuthSetup.FallbackPolicy)
                    o.FallbackPolicy =
                        new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build();
            });
        if (auth is AuthSetup.SchemeOnly or AuthSetup.SchemeAndPolicy or AuthSetup.FallbackPolicy)
            services
                .AddAuthentication(TestUserHandler.Name)
                .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(
                    TestUserHandler.Name,
                    null
                );

        var app = builder.Build();
        try
        {
            if (pathBase is not null)
                app.UsePathBase(pathBase);
            app.UseRouting();
            if (
                auth
                is AuthSetup.SchemeOnly
                    or AuthSetup.SchemeAndPolicy
                    or AuthSetup.FallbackPolicy
            )
                app.UseAuthentication();
            if (auth != AuthSetup.None)
                app.UseAuthorization();

            map(app);
            if (addGraphQL)
                app.UseTraxGraphQL(graphQLPath);
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static Task<HttpResponseMessage> GetAsync(
        WebApplication app,
        string path,
        string? user = null,
        string? host = null
    ) => SendAsync(app, HttpMethod.Get, path, user, host);

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplication app,
        HttpMethod method,
        string path,
        string? user = null,
        string? host = null
    )
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        if (user is not null)
            request.Headers.Add(TestUserHandler.Header, user);
        if (host is not null)
            request.Headers.Host = host;
        var client = app.GetTestClient();
        return await client.SendAsync(request);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var content) ? string.Join(",", content)
        : "";

    private static (string BasePath, string GraphQLPath) Config(string html)
    {
        var content = ConfigMeta().Match(html);
        content.Success.Should().BeTrue("the page carries its runtime config");
        using var json = JsonDocument.Parse(WebUtility.HtmlDecode(content.Groups["json"].Value));
        return (
            json.RootElement.GetProperty("basePath").GetString()!,
            json.RootElement.GetProperty("graphqlPath").GetString()!
        );
    }

    private static string[] ConfigKeys(string html)
    {
        var content = ConfigMeta().Match(html);
        content.Success.Should().BeTrue("the page carries its runtime config");
        using var json = JsonDocument.Parse(WebUtility.HtmlDecode(content.Groups["json"].Value));
        return json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    [GeneratedRegex("""<meta name="trax-dashboard-config" content="(?<json>[^"]*)" />""")]
    private static partial Regex ConfigMeta();

    [GeneratedRegex("""(?:src|href)="(?<path>[^"]+\.(?:js|css))""")]
    private static partial Regex AssetReference();

    /// <summary>
    /// Signs a request in as the user its <c>X-Test-User</c> header names (<c>name;Role1,Role2</c>);
    /// a request without it is anonymous.
    /// </summary>
    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var value))
                return Task.FromResult(AuthenticateResult.NoResult());

            var parts = value.ToString().Split(';');
            var claims = new List<Claim> { new(ClaimTypes.Name, parts[0]) };
            if (parts.Length > 1)
                claims.AddRange(parts[1].Split(',').Select(r => new Claim(ClaimTypes.Role, r)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, Name))
            );
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
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
            ) => provider.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
