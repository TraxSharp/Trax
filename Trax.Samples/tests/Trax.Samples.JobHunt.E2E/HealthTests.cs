using System.Net;
using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E;

[TestFixture]
public class HealthTests : JobHuntApiTestFixture
{
    [Test]
    public async Task Health_GET_Returns200()
    {
        var response = await HttpClient.GetAsync("/trax/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Health_AfterStartup_ReportsHealthy()
    {
        var response = await HttpClient.GetAsync("/trax/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("Healthy");
    }
}
