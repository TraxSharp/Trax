using System.Net;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that the Trax Dashboard (Blazor Server) is correctly wired up
/// and serves at the configured route prefix.
/// </summary>
[TestFixture]
public class DashboardTests : SchedulerTestFixture
{
    [Test]
    public async Task Dashboard_TraxEndpoint_RespondsSuccessfully()
    {
        var client = GetHttpClient();

        var response = await client.GetAsync("/trax");

        // Blazor Server may return 200 (the page) or redirect to a Blazor route.
        // Either way, it should NOT return 404 or 500.
        response
            .StatusCode.Should()
            .NotBe(HttpStatusCode.NotFound, "dashboard should be mapped at /trax");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task Dashboard_TrainsPage_RespondsSuccessfully()
    {
        var client = GetHttpClient();

        var response = await client.GetAsync("/trax/trains");

        response
            .StatusCode.Should()
            .NotBe(HttpStatusCode.NotFound, "trains page should be mapped at /trax/trains");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
