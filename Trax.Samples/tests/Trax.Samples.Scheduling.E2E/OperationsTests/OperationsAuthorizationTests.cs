using System.Net;
using System.Net.Http.Json;
using Trax.Samples.Scheduling.E2E.Factories;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Scheduling.Host;

namespace Trax.Samples.Scheduling.E2E.OperationsTests;

/// <summary>
/// <c>GateOperations(roles: "Operator")</c> keeps the operations namespace closed to anyone
/// without the role, and in Production the demo key does not exist at all.
///
/// <para>Enforces <c>Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md</c> for this sample.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md")]
public class OperationsAuthorizationTests : SchedulingTestFixture
{
    private const string ListManifests = "{ operations { manifests(take: 1) { totalCount } } }";

    [Test]
    public async Task Operations_refuse_a_caller_with_no_key()
    {
        var response = await Anonymous.Send(ListManifests);

        response.HasErrors.Should().BeTrue(response.Body);
        response.FirstErrorCode.Should().Be("TRAX_AUTHORIZATION");
        response.Body.Should().NotContain("totalCount\":");
    }

    [Test]
    public async Task Operations_refuse_an_unknown_key()
    {
        using var http = SharedSchedulingSetup.Factory.CreateClient();
        var stranger = new Utilities.GraphQLClient(http, "not-a-real-key");

        var response = await stranger.Send(ListManifests);

        response.HasErrors.Should().BeTrue(response.Body);
        response.Body.Should().NotContain("totalCount\":");
    }

    [Test]
    public async Task Operations_answer_the_operator_key()
    {
        var response = await Operator.Send(ListManifests);

        response
            .Data("operations", "manifests")
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .Be(6);
    }

    [Test]
    public async Task Production_refuses_the_demo_key_and_serves_no_dashboard()
    {
        await using var production = new ProductionHostFactory();
        using var http = production.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", DemoKeys.OperatorKey);

        var graphql = await http.PostAsJsonAsync("/trax/graphql", new { query = ListManifests });
        var body = await graphql.Content.ReadAsStringAsync();
        var dashboard = await http.GetAsync("/trax");

        graphql.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, body);
        body.Should().Contain("TRAX_AUTHORIZATION").And.NotContain("totalCount\":");
        dashboard.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
