using Trax.Samples.ChatService.E2E.Fixtures;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

[TestFixture]
public class HealthCheckTests : ChatApiTestFixture
{
    [Test]
    public async Task HealthCheck_Returns200()
    {
        var response = await HttpClient.GetAsync("/trax/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
