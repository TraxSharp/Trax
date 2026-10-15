using Microsoft.Playwright;
using Trax.Samples.Recovery.Browser.Factories;
using Trax.Samples.Recovery.Browser.Fixtures;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.Recovery.Browser;

/// <summary>
/// Builds the page once, starts the Recovery host as a process on the browser tests' database, and launches
/// chromium. Any of the three failing fails the run instead of every test reporting its own red: a harness
/// that cannot start proves nothing about the page (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public class BrowserSetup
{
    private static IPlaywright? _playwright;

    /// <summary>The built page's folder.</summary>
    public static string Dist { get; private set; } = null!;

    /// <summary>The host the page talks to. A test that kills it starts the next one with <see cref="RestartHostAsync"/>.</summary>
    public static RecoveryHostProcess Host { get; private set; } = null!;

    public static IBrowser Browser { get; private set; } = null!;

    /// <summary>The address the page's API calls are forwarded to.</summary>
    public static Uri HostAddress => Host.Http.BaseAddress!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Dist = await RecoveryClientBuild.BuildAsync();
        Host = await RecoveryBrowserHost.StartAsync();
        // Every page starts its draft as the demo user, and the database outlives a run.
        await TopicMapDrafts.ClearAsync(RecoveryBrowserHost.ConnectionString);
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });

        // The harness itself: the built page loads and renders the wizard against this host.
        await using var probe = await NewPageAsync();
        await probe.OpenTheWizardAsync();
    }

    /// <summary>A fresh browser context on the built page, forwarding to the current host.</summary>
    public static async Task<OpenPage> NewPageAsync(Func<string, string, string>? replace = null) =>
        new(await RecoveryPage.OpenAsync(Browser, Dist, () => HostAddress, replace));

    /// <summary>Starts a new host on the same database, after a test killed the last one.</summary>
    public static async Task RestartHostAsync()
    {
        await Host.DisposeAsync();
        Host = await RecoveryBrowserHost.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        _playwright?.Dispose();
        if (Host is not null)
            await Host.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>A <see cref="RecoveryPage"/> whose context closes when it is disposed.</summary>
public sealed class OpenPage(RecoveryPage page) : IAsyncDisposable
{
    public RecoveryPage Recovery { get; } = page;

    public Task OpenTheWizardAsync() => Recovery.OpenTheWizardAsync();

    public ValueTask DisposeAsync() => new(Recovery.Context.CloseAsync());
}
