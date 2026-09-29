using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Samples.Templates.Tests.IntegrationTests;

/// <summary>
/// What a project scaffolded from the <c>trax-hub</c>, <c>trax-scheduler</c> and
/// <c>trax-api</c> templates serves outside Development. The templates put no authorization in
/// front of the dashboard and ship a plaintext demo API key, so both exist only in
/// Development, where <c>dotnet run</c> starts through launchSettings.json. Every template
/// operation requires the demo key's role, so without the key nothing runs.
///
/// <para>Enforces <c>docs/adr/0003-templates-serve-the-dashboard-only-in-development.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0003-templates-serve-the-dashboard-only-in-development.md")]
public class TemplateEnvironmentTests
{
    private const string Adr =
        "see docs/adr/0003-templates-serve-the-dashboard-only-in-development.md";

    // Any public type in the template's assembly locates its entry point.
    private static WebApplicationFactory<T> Start<T>(string environment)
        where T : class =>
        new WebApplicationFactory<T>().WithWebHostBuilder(b => b.UseEnvironment(environment));

    [Test]
    public async Task The_hub_template_serves_no_dashboard_outside_Development()
    {
        await using var factory = Start<Hub.Data.AppDbContext>("Production");

        var response = await factory.CreateClient().GetAsync("/trax");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, Adr);
    }

    [Test]
    public async Task The_hub_template_serves_the_dashboard_in_Development()
    {
        await using var factory = Start<Hub.Data.AppDbContext>("Development");

        var response = await factory.CreateClient().GetAsync("/trax");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the premise of the Production test");
    }

    [Test]
    public async Task The_scheduler_template_serves_no_dashboard_outside_Development()
    {
        await using var factory = Start<Scheduler.Trains.HelloWorld.HelloWorldInput>("Production");

        var response = await factory.CreateClient().GetAsync("/trax");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, Adr);
    }

    [Test]
    public async Task The_scheduler_template_serves_the_dashboard_in_Development()
    {
        await using var factory = Start<Scheduler.Trains.HelloWorld.HelloWorldInput>("Development");

        var response = await factory.CreateClient().GetAsync("/trax");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the premise of the Production test");
    }

    [Test]
    public async Task The_hub_template_registers_the_demo_key_only_in_Development()
    {
        (await SchemeNames<Hub.Data.AppDbContext>("Production")).Should().BeEmpty(Adr);
        (await SchemeNames<Hub.Data.AppDbContext>("Development")).Should().NotBeEmpty();
    }

    [Test]
    public async Task The_api_template_registers_the_demo_key_only_in_Development()
    {
        (await SchemeNames<Api.Data.AppDbContext>("Production")).Should().BeEmpty(Adr);
        (await SchemeNames<Api.Data.AppDbContext>("Development")).Should().NotBeEmpty();
    }

    private const string DispatchHelloWorld =
        "mutation { dispatch { helloWorld(input: { name: \"Trax\" }) { externalId } } }";

    private const string DiscoverLookup =
        "query { discover { lookup(input: { id: \"42\" }) { id } } }";

    private const string DiscoverNotes = "query { discover { app { notes { nodes { id } } } } }";

    private const string DemoKey = "demo-key-do-not-use-in-production";

    [Test]
    public async Task The_hub_template_refuses_an_anonymous_notes_query_outside_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Production", DiscoverNotes, key: null))
            .Should()
            .NotBeEmpty(Adr);

    [Test]
    public async Task The_hub_template_serves_notes_with_the_demo_key_in_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Development", DiscoverNotes, DemoKey))
            .Should()
            .BeEmpty("the demo key carries the role the template query model requires");

    [Test]
    public async Task The_hub_template_refuses_an_anonymous_dispatch_outside_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Production", DispatchHelloWorld, key: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);

    [Test]
    public async Task The_api_template_refuses_an_anonymous_dispatch_outside_Development() =>
        (await ErrorCodes<Api.Data.AppDbContext>("Production", DispatchHelloWorld, key: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);

    [Test]
    public async Task The_hub_template_refuses_an_anonymous_lookup_outside_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Production", DiscoverLookup, key: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);

    [Test]
    public async Task The_api_template_refuses_an_anonymous_lookup_outside_Development() =>
        (await ErrorCodes<Api.Data.AppDbContext>("Production", DiscoverLookup, key: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);

    [Test]
    public async Task The_hub_template_refuses_an_anonymous_dispatch_in_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Development", DispatchHelloWorld, key: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr);

    [Test]
    public async Task The_hub_template_runs_a_dispatch_with_the_demo_key_in_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Development", DispatchHelloWorld, DemoKey))
            .Should()
            .BeEmpty("the demo key carries the role the template trains require");

    [Test]
    public async Task The_api_template_runs_a_dispatch_with_the_demo_key_in_Development() =>
        (await ErrorCodes<Api.Data.AppDbContext>("Development", DispatchHelloWorld, DemoKey))
            .Should()
            .BeEmpty("the demo key carries the role the template trains require");

    [Test]
    public async Task The_hub_template_runs_a_lookup_with_the_demo_key_in_Development() =>
        (await ErrorCodes<Hub.Data.AppDbContext>("Development", DiscoverLookup, DemoKey))
            .Should()
            .BeEmpty("the demo key carries the role the template trains require");

    /// <summary>
    /// Posts <paramref name="query"/> to the template's GraphQL endpoint and returns every error
    /// code in the response, or an empty list when it succeeded with data.
    /// </summary>
    private static async Task<List<string>> ErrorCodes<T>(
        string environment,
        string query,
        string? key
    )
        where T : class
    {
        await using var factory = Start<T>(environment);
        var client = factory.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var response = await client.PostAsJsonAsync("/trax/graphql", new { query });
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        if (!json.RootElement.TryGetProperty("errors", out var errors))
            return [];
        return errors
            .EnumerateArray()
            .Select(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var code)
                    ? code.GetString() ?? "(null code)"
                    : $"(no code) {e.GetProperty("message").GetString()}"
            )
            .ToList();
    }

    private static async Task<List<string>> SchemeNames<T>(string environment)
        where T : class
    {
        await using var factory = Start<T>(environment);
        var schemes = await factory
            .Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();
        return schemes.Select(s => s.Name).ToList();
    }
}
