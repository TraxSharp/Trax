using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// What the sample serves outside Development. The <c>dev_</c> allowlist, the dashboard and the
/// demo operator key exist only in Development, and the persisted-operations management
/// mutations require the operator role in every environment, so a copy of this host started
/// in Production enforces persistence on every request and serves no anonymous control plane.
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

    private const string GreetDoc =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

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
    public async Task A_dev_named_inline_document_is_refused_in_Production()
    {
        using var http = _production.CreateClient();

        var resp = await http.PostAsJsonAsync(
            "/trax/graphql/",
            new
            {
                query = "query dev_explore($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }",
                operationName = "dev_explore",
                variables = new { input = new { name = "Dev" } },
            }
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
                            document = GreetDoc,
                            bypassShapeDiff = false,
                        },
                    },
                }
            );
            return await resp.Content.ReadAsStringAsync();
        }
    }
}
