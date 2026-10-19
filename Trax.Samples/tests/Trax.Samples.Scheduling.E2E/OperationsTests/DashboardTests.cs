using System.Net;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.OperationsTests;

/// <summary>The operations console is mounted at <c>/trax/dashboard</c> in Development, every route of it.</summary>
public class DashboardTests : SchedulingTestFixture
{
    [TestCase("/trax/dashboard/")]
    [TestCase("/trax/dashboard/trains")]
    [TestCase("/trax/dashboard/runs/dead-letters")]
    public async Task Dashboard_page_responds_in_Development(string path)
    {
        using var http = SharedSchedulingSetup.Factory.CreateClient();

        var response = await http.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
