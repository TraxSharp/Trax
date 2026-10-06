using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Trax.Samples.Hub.Auth;

namespace Trax.Samples.Hub.Tests.IntegrationTests;

/// <summary>
/// Starts the whole application in memory, as `dotnet run` would, once in Development and
/// once in Production, and calls it over HTTP. These are the tests that fail when Program.cs
/// no longer starts, or when something meant for Development only reaches Production.
/// </summary>
[TestFixture]
public class HostTests
{
    private const string DispatchHelloWorld =
        "mutation { dispatch { helloWorld(input: { name: \"Test\" }) { metadataId } } }";

    private static WebApplicationFactory<Program> Start(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment(environment)
        );

    [Test]
    public async Task Development_DispatchWithTheDemoKey_RunsTheTrain()
    {
        await using var app = Start("Development");

        using var response = await PostGraphQL(app, DispatchHelloWorld, DemoKeys.DemoKey);

        Assert.That(ErrorCodes(response), Is.Empty, "the demo key holds the User role");
        Assert.That(
            response
                .RootElement.GetProperty("data")
                .GetProperty("dispatch")
                .GetProperty("helloWorld")
                .GetProperty("metadataId")
                .GetInt64(),
            Is.GreaterThan(0),
            "a run returns the id of the record Trax kept of it"
        );
    }

    [Test]
    public async Task Development_DispatchWithoutAKey_IsRefused()
    {
        await using var app = Start("Development");

        using var response = await PostGraphQL(app, DispatchHelloWorld, apiKey: null);

        Assert.That(ErrorCodes(response), Does.Contain("TRAX_AUTHORIZATION"));
    }

    [Test]
    public async Task Development_Dashboard_IsServed()
    {
        await using var app = Start("Development");

        var response = await app.CreateClient().GetAsync("/trax");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Production_Starts_AndAnswersTheHealthCheck()
    {
        await using var app = Start("Production");

        var response = await app.CreateClient().GetAsync("/trax/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Production_Dashboard_IsNotServed()
    {
        await using var app = Start("Production");

        var response = await app.CreateClient().GetAsync("/trax");

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound),
            "the dashboard has no authorization in front of it, so it exists only in Development"
        );
    }

    [Test]
    public async Task Production_DispatchWithTheDemoKey_IsRefused()
    {
        await using var app = Start("Production");

        using var response = await PostGraphQL(app, DispatchHelloWorld, DemoKeys.DemoKey);

        Assert.That(
            ErrorCodes(response),
            Does.Contain("TRAX_AUTHORIZATION"),
            "the demo key is registered only in Development"
        );
    }

    private static async Task<JsonDocument> PostGraphQL(
        WebApplicationFactory<Program> app,
        string query,
        string? apiKey
    )
    {
        var client = app.CreateClient();
        if (apiKey is not null)
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var response = await client.PostAsJsonAsync("/trax/graphql", new { query });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The <c>extensions.code</c> of every error in the response.</summary>
    private static List<string?> ErrorCodes(JsonDocument response) =>
        response.RootElement.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                        ? code.GetString()
                        : null
                )
                .ToList()
            : [];
}
