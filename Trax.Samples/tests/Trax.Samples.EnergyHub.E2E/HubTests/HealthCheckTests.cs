using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class HealthCheckTests : HubTestFixture
{
    [Test]
    public async Task HealthCheck_Returns200()
    {
        using var client = GetHttpClient();
        var response = await client.GetAsync("/trax/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Test]
    public async Task Dashboard_Responds()
    {
        using var client = GetHttpClient();
        var response = await client.GetAsync("/trax");
        ((int)response.StatusCode)
            .Should()
            .BeLessThan(500, "dashboard should not return server error");
        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.NotFound);
    }
}
