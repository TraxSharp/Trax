using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The dashboard Trax.Api.Dashboard serves boots in a real browser under the Content-Security-Policy
/// it is served with, and talks to the host's GraphQL endpoint on the same origin: the operator's
/// API key reaches it over HTTP and over the subscription socket. Every kind of page it draws (the
/// overview's charts, the groups' dependency graph, a train's page, the state-machine pages) runs
/// under that policy too, each loaded from its own URL as a reload would.
/// </summary>
/// <remarks>
/// The host is <see cref="Factories.ConsoleHost"/>: two trains run on a schedule in two dependent manifest groups
/// and a system-owned machine has one instance (started here), so each page has something to draw. The dashboard's
/// app is the one the build embedded in Trax.Api.Dashboard, so the build needs the app's npm packages (CI installs
/// them for this suite).
/// </remarks>
[TestFixture]
public class ApiDashboardBrowserTests : ConsoleTest
{
    private static readonly TimeSpan Render = BrowserTimeouts.Render;

    private Guid _gate;

    [OneTimeSetUp]
    public async Task StartTheGate()
    {
        using var scope = Host.Services.CreateScope();
        _gate = (
            await scope
                .ServiceProvider.GetRequiredService<IMachineInstances>()
                .Start<GateMachine>(MachineKey.Of("lane-1"))
        ).Id;
    }

    [Test]
    [Covers("route:/runs")]
    public async Task The_app_boots_under_its_CSP_and_connects_to_GraphQL_with_the_operators_key()
    {
        await Console.GotoAsync("runs");

        // The footer's read of the Trax version the host runs, through the gated endpoint, by its id.
        var graphQL = Page.WaitForResponseAsync(
            r =>
                r.Url.EndsWith("/trax/graphql")
                && r.Request.Method == "POST"
                && r.Request.PostData?.Contains("\"operationName\":\"ServerVersion\"") == true,
            new() { Timeout = Render.Ms() }
        );
        // Deep link, no credential yet: the app asks for one.
        var connected = await Console.ConnectAsync(Caller.Operator);

        var response = await graphQL;
        response.Status.Should().Be(200, "the operator's key reaches the GraphQL endpoint");
        (await response.Request.HeaderValueAsync("x-api-key")).Should().Be(Caller.Operator.Key);

        await Assertions
            .Expect(Console.Footer)
            .ToContainTextAsync("Trax Dashboard — v", new() { Timeout = Render.Ms() });

        var socketUrl = await connected.Acknowledged.WaitAsync(Render);
        socketUrl.Should().Be($"ws://{Host.Address.Authority}/trax/graphql");

        // The router took the deep link under the base path the host wrote into the page.
        Page.Url.Should().EndWith("/trax/dashboard/runs");
    }

    // Each page, loaded from its own URL (as a reload is), and something only it draws. {gate} is the
    // machine instance started above.
    [TestCase("", "Overview", ".recharts-surface")]
    [TestCase(
        "schedules/groups",
        "Dependency graph",
        "[aria-label='Dependency graph'] .react-flow__node"
    )]
    [TestCase(
        "trains/Trax.Samples.ApiDashboard.Browser.Host.IRefreshRatesTrain",
        "Trax.Samples.ApiDashboard.Browser.Host.IRefreshRatesTrain",
        "text=Total runs"
    )]
    [TestCase("machines", "Machines", "text=browser-gate")]
    [TestCase(
        "machines/browser-gate",
        "browser-gate",
        "[aria-label='States of browser-gate'] .react-flow__node"
    )]
    [TestCase(
        "machines/browser-gate/system/{gate}",
        "browser-gate",
        "[aria-label='States of browser-gate'] [aria-current='step']"
    )]
    [Covers("route:/")]
    [Covers("route:/schedules/groups")]
    [Covers("route:/trains/:name")]
    [Covers("route:/machines")]
    [Covers("route:/machines/:name")]
    [Covers("route:/machines/:machine/:owner/:id")]
    public async Task Each_page_draws_under_the_CSP_when_loaded_from_its_own_URL(
        string route,
        string landmark,
        string drawn
    )
    {
        var url = Console.Url(route.Replace("{gate}", $"{_gate}"));
        var document = await Page.GotoAsync(url);
        document!.Status.Should().Be(200, $"{url} is a route of the app, not a missing file");

        // Nothing is stored, so every load asks for the key again.
        var connected = await Console.ConnectAsync(Caller.Operator);

        await Assertions
            .Expect(Page.GetByText(landmark, new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = Render.Ms() });
        await Assertions
            .Expect(Page.Locator(drawn).First)
            .ToBeVisibleAsync(new() { Timeout = Render.Ms() });
        Page.Url.Should().Be(url, "the router kept the route it was loaded at");

        var request = await connected.FirstRequest;
        (await request.HeaderValueAsync("x-api-key")).Should().Be(Caller.Operator.Key);
        (await connected.Acknowledged.WaitAsync(Render))
            .Should()
            .Be($"ws://{Host.Address.Authority}/trax/graphql");
    }

    [Test]
    public async Task The_key_is_never_stored_so_a_reload_asks_for_it_again()
    {
        await Console.GotoAsync("runs");
        var connected = await Console.ConnectAsync(Caller.Operator);
        (await (await connected.FirstRequest).HeaderValueAsync("x-api-key"))
            .Should()
            .Be(Caller.Operator.Key);
        await connected.Acknowledged.WaitAsync(Render);

        (await StoredAsync())
            .Should()
            .NotContain(
                Caller.Operator.Key,
                "the key is held in the page's memory, never in storage"
            );
        (await Page.Context.CookiesAsync()).Should().BeEmpty();

        // What an earlier version stored is deleted at startup and never used.
        await Page.EvaluateAsync(
            $$"""
            () => {
              localStorage.setItem("trax-dashboard-api-key", "{{Caller.Operator.Key}}");
              localStorage.setItem("trax-dashboard-auth-mode", "apikey");
              localStorage.setItem('trax-dashboard-credential:["/trax/dashboard/","/trax/graphql"]',
                JSON.stringify({ owner: "anonymous", mode: "apikey", credential: "{{Caller.Operator.Key}}" }));
            }
            """
        );
        await Page.ReloadAsync();

        await Assertions
            .Expect(Console.KeyPrompt)
            .ToBeVisibleAsync(new() { Timeout = Render.Ms() });
        (await StoredAsync())
            .Should()
            .NotContain(Caller.Operator.Key)
            .And.NotContain("trax-dashboard-api-key");

        await Console.ConnectAsync(Caller.Operator);
        await Assertions
            .Expect(Console.Footer)
            .ToContainTextAsync("Trax Dashboard — v", new() { Timeout = Render.Ms() });
        (await StoredAsync()).Should().NotContain(Caller.Operator.Key);
    }

    // Everything the page's origin keeps in localStorage, sessionStorage and IndexedDB, as text.
    private Task<string> StoredAsync() =>
        Page.EvaluateAsync<string>(
            """
            async () => {
              const dump = (s) => Object.keys(s).map((k) => `${k}=${s.getItem(k)}`).join("\n");
              const dbs = indexedDB.databases ? (await indexedDB.databases()).map((d) => d.name) : [];
              return [dump(localStorage), dump(sessionStorage), dbs.join(",")].join("\n");
            }
            """
        );
}
