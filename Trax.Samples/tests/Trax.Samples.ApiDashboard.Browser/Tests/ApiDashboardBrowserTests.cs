using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The dashboard Trax.Api.Dashboard serves boots in a real browser under the Content-Security-Policy
/// it is served with, and talks to the host's GraphQL endpoint on the same origin: the operator's
/// API key reaches it over HTTP and over the subscription socket.
/// </summary>
/// <remarks>
/// The host runs in this process on Kestrel, on a port of its own: Trax on the in-memory provider,
/// the GraphQL endpoint gated behind an API key, and the dashboard. It needs no database. The
/// dashboard's app is the one the build embedded in Trax.Api.Dashboard, so the build needs the
/// app's npm packages (CI installs them for this folder).
/// </remarks>
[TestFixture]
[NonParallelizable]
public class ApiDashboardBrowserTests
{
    private const string ApiKey = "browser-test-key-do-not-use-in-production";

    private static readonly TimeSpan Render = TimeSpan.FromSeconds(20);

    private WebApplication _host = null!;
    private Uri _address = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _host = BuildHost();
        await _host.StartAsync();
        _address = new Uri(
            _host
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single()
        );
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _playwright?.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Test]
    public async Task The_app_boots_under_its_CSP_and_connects_to_GraphQL_with_the_operators_key()
    {
        var context = await _browser.NewContextAsync();
        try
        {
            var page = await context.NewPageAsync();

            // Every policy violation the page reports, and every console error that names the policy.
            var violations = new ConcurrentQueue<string>();
            await page.AddInitScriptAsync(
                """
                window.__cspViolations = [];
                document.addEventListener("securitypolicyviolation", (e) =>
                  window.__cspViolations.push(`${e.violatedDirective} ${e.blockedURI}`));
                """
            );
            page.Console += (_, message) =>
            {
                if (message.Type == "error" && message.Text.Contains("Content Security Policy"))
                    violations.Enqueue(message.Text);
            };

            // The subscription socket: open on this origin's GraphQL path, and acknowledged.
            var acknowledged = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            page.WebSocket += (_, socket) =>
                socket.FrameReceived += (_, frame) =>
                {
                    if (frame.Text?.Contains("\"connection_ack\"") == true)
                        acknowledged.TrySetResult(socket.Url);
                };

            await page.GotoAsync(new Uri(_address, "/trax/dashboard/executions").ToString());

            // Deep link, no credential yet: the app asks for one.
            await page.GetByLabel("API key").FillAsync(ApiKey, new() { Timeout = Ms(Render) });
            // The footer's read of the Trax version the host runs, through the gated endpoint.
            var graphQL = page.WaitForResponseAsync(
                r =>
                    r.Url.EndsWith("/trax/graphql")
                    && r.Request.Method == "POST"
                    && r.Request.PostData?.Contains("\"operationName\":\"ServerVersion\"") == true,
                new() { Timeout = Ms(Render) }
            );
            await page.GetByRole(AriaRole.Button, new() { Name = "Connect" }).ClickAsync();

            var response = await graphQL;
            response.Status.Should().Be(200, "the operator's key reaches the GraphQL endpoint");
            (await response.Request.HeaderValueAsync("x-api-key")).Should().Be(ApiKey);

            await Assertions
                .Expect(page.GetByTestId("dashboard-footer"))
                .ToContainTextAsync("Trax Dashboard — v", new() { Timeout = Ms(Render) });

            var socketUrl = await acknowledged.Task.WaitAsync(Render);
            socketUrl.Should().Be($"ws://{_address.Authority}/trax/graphql");

            // The router took the deep link under the base path the host wrote into the page.
            page.Url.Should().EndWith("/trax/dashboard/executions");

            var reported = await page.EvaluateAsync<string[]>("window.__cspViolations");
            reported
                .Concat(violations)
                .Should()
                .BeEmpty("the app runs under the policy it is served with");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static float Ms(TimeSpan timeout) => (float)timeout.TotalMilliseconds;

    private static WebApplication BuildHost()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "operator", "Admin"));
        builder.Services.AddAuthorization();
        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(ApiDashboardBrowserTests).Assembly)
                .AddScheduler()
        );
        builder.Services.AddTraxGraphQL(graphql =>
            graphql.ExposeOperationQueries().ExposeOperationMutations().RequireAuthorization()
        );

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTraxGraphQL();
        // The GraphQL endpoint authorizes everything the app reads; the shell is open here because
        // a browser cannot present the API key on a page load (Trax.Api/docs/adr/0039).
        app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());
        return app;
    }
}
