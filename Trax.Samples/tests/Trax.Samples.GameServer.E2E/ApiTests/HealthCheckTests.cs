using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class HealthCheckTests : ApiTestFixture
{
    [Test]
    public async Task HealthCheck_Returns200()
    {
        var response = await HttpClient.GetAsync("/trax/health");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
