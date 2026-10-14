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
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The dashboard Trax.Api.Dashboard serves boots in a real browser under the Content-Security-Policy
/// it is served with, and talks to the host's GraphQL endpoint on the same origin: the operator's
/// API key reaches it over HTTP and over the subscription socket. Every kind of page it draws (the
/// overview's charts, the groups' dependency graph, a train's page, the state-machine pages) runs
/// under that policy too, each loaded from its own URL as a reload would.
/// </summary>
/// <remarks>
/// The host runs in this process on Kestrel, on a port of its own: Trax on the in-memory provider,
/// the GraphQL endpoint gated behind an API key, and the dashboard. It needs no database. Two
/// trains run on a schedule in two dependent manifest groups and a system-owned machine has one
/// instance, so each page has something to draw (Host/BrowserHostModel.cs). The dashboard's app is
/// the one the build embedded in Trax.Api.Dashboard, so the build needs the app's npm packages (CI
/// installs them for this folder).
/// </remarks>
[TestFixture]
[NonParallelizable]
public class ApiDashboardBrowserTests
{
    private const string ApiKey = "browser-test-key-do-not-use-in-production";

    private static readonly TimeSpan Render = TimeSpan.FromSeconds(20);

    private WebApplication _host = null!;
    private Uri _address = null!;
    private Guid _gate;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _host = BuildHost();
        await _host.StartAsync();
        using (var scope = _host.Services.CreateScope())
            _gate = (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Start<GateMachine>(MachineKey.Of("lane-1"))
            ).Id;
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
            var violations = await WatchForViolationsAsync(page);

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

            (await ViolationsAsync(page, violations))
                .Should()
                .BeEmpty("the app runs under the policy it is served with");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    // Each page, loaded from its own URL (as a reload is), and something only it draws. {gate} is the
    // machine instance the host started.
    [TestCase("", "Overview", ".recharts-surface")]
    [TestCase("groups", "Dependency graph", "svg[role=img]")]
    [TestCase(
        "trains/Trax.Samples.ApiDashboard.Browser.Host.IRefreshRatesTrain",
        "Trax.Samples.ApiDashboard.Browser.Host.IRefreshRatesTrain",
        "text=Total runs"
    )]
    [TestCase("state-machines", "State machines", "text=browser-gate")]
    [TestCase(
        "state-machines/browser-gate/system/{gate}",
        "browser-gate",
        "text=All state machines"
    )]
    public async Task Each_page_draws_under_the_CSP_when_loaded_from_its_own_URL(
        string route,
        string landmark,
        string drawn
    )
    {
        var context = await _browser.NewContextAsync();
        try
        {
            var page = await context.NewPageAsync();
            var violations = await WatchForViolationsAsync(page);

            var url = new Uri(_address, "/trax/dashboard/" + route.Replace("{gate}", $"{_gate}"));
            var document = await page.GotoAsync(url.ToString());
            document!.Status.Should().Be(200, $"{url} is a route of the app, not a missing file");

            // Nothing is stored, so every load asks for the key again.
            var connected = await ConnectAsync(page);

            await Assertions
                .Expect(page.GetByText(landmark, new() { Exact = true }).First)
                .ToBeVisibleAsync(new() { Timeout = Ms(Render) });
            await Assertions
                .Expect(page.Locator(drawn).First)
                .ToBeVisibleAsync(new() { Timeout = Ms(Render) });
            page.Url.Should().Be(url.ToString(), "the router kept the route it was loaded at");

            var request = await connected.FirstRequest;
            (await request.HeaderValueAsync("x-api-key")).Should().Be(ApiKey);
            (await connected.Acknowledged.WaitAsync(Render))
                .Should()
                .Be($"ws://{_address.Authority}/trax/graphql");

            (await ViolationsAsync(page, violations))
                .Should()
                .BeEmpty($"/{route} draws under the policy it is served with");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Test]
    public async Task The_key_is_never_stored_so_a_reload_asks_for_it_again()
    {
        var context = await _browser.NewContextAsync();
        try
        {
            var page = await context.NewPageAsync();
            var violations = await WatchForViolationsAsync(page);
            var url = new Uri(_address, "/trax/dashboard/executions").ToString();

            await page.GotoAsync(url);
            var connected = await ConnectAsync(page);
            (await (await connected.FirstRequest).HeaderValueAsync("x-api-key"))
                .Should()
                .Be(ApiKey);
            await connected.Acknowledged.WaitAsync(Render);

            (await StoredAsync(page))
                .Should()
                .NotContain(ApiKey, "the key is held in the page's memory, never in storage");
            (await context.CookiesAsync()).Should().BeEmpty();

            // What an earlier version stored is deleted at startup and never used.
            await page.EvaluateAsync(
                $$"""
                () => {
                  localStorage.setItem("trax-dashboard-api-key", "{{ApiKey}}");
                  localStorage.setItem("trax-dashboard-auth-mode", "apikey");
                  localStorage.setItem('trax-dashboard-credential:["/trax/dashboard/","/trax/graphql"]',
                    JSON.stringify({ owner: "anonymous", mode: "apikey", credential: "{{ApiKey}}" }));
                }
                """
            );
            await page.ReloadAsync();

            await Assertions
                .Expect(page.GetByLabel("API key"))
                .ToBeVisibleAsync(new() { Timeout = Ms(Render) });
            (await StoredAsync(page))
                .Should()
                .NotContain(ApiKey)
                .And.NotContain("trax-dashboard-api-key");

            await ConnectAsync(page);
            await Assertions
                .Expect(page.GetByTestId("dashboard-footer"))
                .ToContainTextAsync("Trax Dashboard — v", new() { Timeout = Ms(Render) });
            (await StoredAsync(page)).Should().NotContain(ApiKey);

            (await ViolationsAsync(page, violations)).Should().BeEmpty();
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    // The operator enters the key through the app's own prompt, as on every page load. Returns the
    // first GraphQL request the app sends after it, and the URL of the subscription socket the
    // server acknowledges.
    private static async Task<(
        Task<IRequest> FirstRequest,
        Task<string> Acknowledged
    )> ConnectAsync(IPage page)
    {
        var acknowledged = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        page.WebSocket += (_, socket) =>
            socket.FrameReceived += (_, frame) =>
            {
                if (frame.Text?.Contains("\"connection_ack\"") == true)
                    acknowledged.TrySetResult(socket.Url);
            };

        await page.GetByLabel("API key").FillAsync(ApiKey, new() { Timeout = Ms(Render) });
        var request = page.WaitForRequestAsync(
            r => r.Url.EndsWith("/trax/graphql") && r.Method == "POST",
            new() { Timeout = Ms(Render) }
        );
        await page.GetByRole(AriaRole.Button, new() { Name = "Connect" }).ClickAsync();
        return (request, acknowledged.Task);
    }

    // Everything the page's origin keeps in localStorage, sessionStorage and IndexedDB, as text.
    private static Task<string> StoredAsync(IPage page) =>
        page.EvaluateAsync<string>(
            """
            async () => {
              const dump = (s) => Object.keys(s).map((k) => `${k}=${s.getItem(k)}`).join("\n");
              const dbs = indexedDB.databases ? (await indexedDB.databases()).map((d) => d.name) : [];
              return [dump(localStorage), dump(sessionStorage), dbs.join(",")].join("\n");
            }
            """
        );

    // Every policy violation the page reports, and every console error that names the policy.
    private static async Task<ConcurrentQueue<string>> WatchForViolationsAsync(IPage page)
    {
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
        return violations;
    }

    private static async Task<List<string>> ViolationsAsync(
        IPage page,
        ConcurrentQueue<string> console
    ) => [.. await page.EvaluateAsync<string[]>("window.__cspViolations"), .. console];

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
        builder.Services.AddSingleton<ISnapshotPrincipal, NoSnapshotPrincipal>();
        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddStateMachines(typeof(GateMachine).Assembly)
                .AddMediator(typeof(ApiDashboardBrowserTests).Assembly)
                .AddScheduler(scheduler =>
                    scheduler
                        .Schedule<IRefreshRatesTrain>(
                            "refresh-rates",
                            new RatesInput(),
                            Every.Seconds(2),
                            o => o.Group("ingest")
                        )
                        .ThenInclude<IRepriceCatalogTrain>(
                            "reprice-catalog",
                            new RatesInput(),
                            o => o.Group("publish")
                        )
                )
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
