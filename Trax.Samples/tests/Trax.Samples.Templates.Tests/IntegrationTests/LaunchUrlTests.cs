using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Trax.Samples.Templates.Tests.Utils;

namespace Trax.Samples.Templates.Tests.IntegrationTests;

/// <summary>
/// The page <c>dotnet run</c> opens for a scaffolded template, the <c>launchUrl</c> of its
/// launchSettings.json, is a page the template serves in Development, where that profile runs it.
/// </summary>
[TestFixture]
public class LaunchUrlTests
{
    [Test]
    public async Task The_hub_template_serves_its_launch_url() =>
        (await LaunchUrlStatus<Hub.Data.AppDbContext>("Trax.Samples.Hub"))
            .Should()
            .Be(HttpStatusCode.OK);

    [Test]
    public async Task The_scheduler_template_serves_its_launch_url() =>
        (
            await LaunchUrlStatus<Scheduler.Trains.HelloWorld.HelloWorldInput>(
                "Trax.Samples.Scheduler"
            )
        )
            .Should()
            .Be(HttpStatusCode.OK);

    [Test]
    public async Task The_api_template_serves_its_launch_url() =>
        (await LaunchUrlStatus<Api.Data.AppDbContext>("Trax.Samples.Api"))
            .Should()
            .Be(HttpStatusCode.OK);

    // Any public type in the template's assembly locates its entry point.
    private static async Task<HttpStatusCode> LaunchUrlStatus<T>(string template)
        where T : class
    {
        var settings = Path.Combine(
            Dotnet.RepoRoot(),
            "templates",
            "content",
            template,
            "Properties",
            "launchSettings.json"
        );
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(settings));
        var launchUrl = json
            .RootElement.GetProperty("profiles")
            .EnumerateObject()
            .Select(p => p.Value.GetProperty("launchUrl").GetString())
            .Single();

        await using var factory = new WebApplicationFactory<T>().WithWebHostBuilder(b =>
            b.UseEnvironment("Development")
        );
        // As the browser asks for it: the GraphQL endpoint serves its IDE only to a request for HTML.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/" + launchUrl);
        request.Headers.Accept.ParseAdd("text/html");
        var response = await factory.CreateClient().SendAsync(request);
        return response.StatusCode;
    }
}
