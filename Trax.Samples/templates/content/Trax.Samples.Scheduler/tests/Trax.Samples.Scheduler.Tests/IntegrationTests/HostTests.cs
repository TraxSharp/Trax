using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.Scheduler.Tests.IntegrationTests;

/// <summary>
/// Starts the whole application in memory, as `dotnet run` would, once in Development and
/// once in Production, and calls it over HTTP. These are the tests that fail when Program.cs
/// no longer starts, or when something meant for Development only reaches Production.
/// </summary>
[TestFixture]
public class HostTests
{
    private static WebApplicationFactory<Program> Start(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment(environment)
        );

    [Test]
    public async Task Development_Dashboard_IsServed()
    {
        await using var app = Start("Development");

        var response = await app.CreateClient().GetAsync("/trax");

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
}
