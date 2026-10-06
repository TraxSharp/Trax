using System.Net;
using Trax.Samples.ContentShield.E2E.Fixtures;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

[TestFixture]
public class HealthCheckTests : ApiTestFixture
{
    [Test]
    public async Task Health_check_returns_200()
    {
        using var client = GetHttpClient();
        var response = await client.GetAsync("/trax/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Dashboard_is_served_in_development()
    {
        using var client = GetHttpClient();
        var response = await client.GetAsync("/trax");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
