using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// What the sample serves outside Development. The dashboard, the demo operator key and the page at
/// <c>/</c> that sends it exist only in Development, and the management mutations require the operator role in
/// every environment, so a copy of this host started in Production enforces persistence on every
/// request and serves no anonymous control plane.
/// </summary>
[TestFixture]
[Category("E2E")]
public class ProductionPostureTests
{
    private const string UploadMutation = """
        mutation Upload($input: UploadPersistedOperationInput!) {
          operations {
            persistedOperations {
              uploadPersistedOperation(input: $input) { success }
            }
          }
        }
        """;

    private PersistedOperationsApiFactory _production = null!;

    [OneTimeSetUp]
    public void StartProduction()
    {
        _production = new PersistedOperationsApiFactory
        {
            Configure = b => b.UseEnvironment("Production"),
        };
    }

    [OneTimeTearDown]
    public async Task StopProduction()
    {
        if (_production is not null)
            await _production.DisposeAsync();
    }

    [Test]
    public async Task An_inline_document_is_refused_in_Production()
    {
        using var http = _production.CreateClient();

        var resp = await http.PostAsJsonAsync(
            "/trax/graphql/",
            new { query = GreetDocuments.Casual, variables = GreetDocuments.Variables("Eve") }
        );

        ((int)resp.StatusCode).Should().Be(400);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task An_anonymous_upload_is_refused_in_Production()
    {
        var body = await PostUploadAsync(_production.CreateClient(), "anon_prod_v1");

        body.Should().NotContain("\"success\":true");
        GraphQLErrors.FirstCode(body).Should().Be("TRAX_AUTHORIZATION", body);
    }

    [Test]
    public async Task An_anonymous_upload_is_refused_in_Development()
    {
        var body = await PostUploadAsync(SharedApiSetup.Factory.CreateClient(), "anon_dev_v1");

        body.Should().NotContain("\"success\":true");
        GraphQLErrors.FirstCode(body).Should().Be("TRAX_AUTHORIZATION", body);
    }

    [Test]
    public async Task Production_serves_no_dashboard()
    {
        using var http = _production.CreateClient();

        var resp = await http.GetAsync("/trax");

        ((int)resp.StatusCode).Should().Be(404);
    }

    [Test]
    public async Task Development_serves_the_dashboard()
    {
        // The dashboard refuses to start without a posture. The sample opens it with
        // AllowAnonymousDashboard() inside its Development block, so it is served there.
        using var http = SharedApiSetup.Factory.CreateClient();

        var resp = await http.GetAsync("/trax");

        ((int)resp.StatusCode).Should().Be(200);
    }

    [Test]
    public async Task Production_serves_no_demo_page()
    {
        // The page sends the demo operator key, so it is served only where that key exists.
        using var http = _production.CreateClient();

        var resp = await http.GetAsync("/");

        ((int)resp.StatusCode).Should().Be(404);
        (await http.GetAsync("/demo.js")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Development_serves_the_demo_page()
    {
        using var http = SharedApiSetup.Factory.CreateClient();

        var page = await http.GetAsync("/");
        var script = await http.GetAsync("/demo.js");

        ((int)page.StatusCode).Should().Be(200);
        (await page.Content.ReadAsStringAsync()).Should().Contain("Stored query: greet_v1");
        ((int)script.StatusCode).Should().Be(200);
    }

    [Test]
    public async Task The_demo_page_does_not_shadow_the_endpoint_or_the_dashboard_in_Development()
    {
        using var http = SharedApiSetup.Factory.CreateClient();

        var dashboard = await http.GetAsync("/trax");
        var inline = await http.PostAsJsonAsync(
            "/trax/graphql/",
            new
            {
                query = "query { discover { greeting { greet(input: { name: \"Eve\", style: FORMAL }) { greeting } } } }",
            }
        );

        ((int)dashboard.StatusCode).Should().Be(200);
        (await dashboard.Content.ReadAsStringAsync()).Should().NotContain("Stored query: greet_v1");
        (await inline.Content.ReadAsStringAsync()).Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    private static async Task<string> PostUploadAsync(HttpClient http, string id)
    {
        using (http)
        {
            var resp = await http.PostAsJsonAsync(
                "/trax/graphql/",
                new
                {
                    query = UploadMutation,
                    variables = new
                    {
                        input = new
                        {
                            id,
                            document = GreetDocuments.Casual,
                            bypassShapeDiff = false,
                        },
                    },
                }
            );
            return await resp.Content.ReadAsStringAsync();
        }
    }
}
