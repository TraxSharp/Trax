using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Factories;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.ApiDashboard.Browser;

/// <summary>
/// Starts the console's host once, on an empty schema, reads the ids of the documents it registered for the console,
/// and launches chromium. Either failing fails the run instead of every test reporting its own red: a harness that
/// cannot start proves nothing about the console (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public partial class ConsoleSetup
{
    private static IPlaywright? _playwright;

    public static ConsoleHost Host { get; private set; } = null!;

    public static IBrowser Browser { get; private set; } = null!;

    /// <summary>The persisted id of each console document, by operation name, as the page is told them.</summary>
    public static IReadOnlyDictionary<string, string> DocumentIds { get; private set; } =
        new Dictionary<string, string>();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Host = await ConsoleHost.StartAsync();
        DocumentIds = await ReadDocumentIdsAsync(Host);
        DocumentIds
            .Should()
            .ContainKey(
                "RequeueDeadLetter",
                "the host registers the console's documents at startup"
            );
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        _playwright?.Dispose();
        if (Host is not null)
            await Host.DisposeAsync();
    }

    // The runtime config the dashboard writes into its entry page names the ids the page sends.
    private static async Task<IReadOnlyDictionary<string, string>> ReadDocumentIdsAsync(
        ConsoleHost host
    )
    {
        using var http = new HttpClient();
        var html = await http.GetStringAsync(host.Console());
        var content = ConfigMeta().Match(html).Groups["content"].Value;
        using var config = JsonDocument.Parse(WebUtility.HtmlDecode(content));
        return config.RootElement.TryGetProperty("documents", out var documents)
            ? documents.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : new Dictionary<string, string>();
    }

    [GeneratedRegex("<meta name=\"trax-dashboard-config\" content=\"(?<content>[^\"]*)\"")]
    private static partial Regex ConfigMeta();
}
