using System.Net;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.OperationsTests;

/// <summary>The dashboard is mounted at <c>/trax</c> in Development.</summary>
public class DashboardTests : SchedulingTestFixture
{
    [TestCase("/trax")]
    [TestCase("/trax/trains")]
    [TestCase("/trax/data/dead-letters")]
    public async Task Dashboard_page_responds_in_Development(string path)
    {
        using var http = SharedSchedulingSetup.Factory.CreateClient();

        var response = await http.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
