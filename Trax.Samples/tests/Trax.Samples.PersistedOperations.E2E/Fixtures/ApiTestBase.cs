using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Samples.PersistedOperations.Api.Auth;

namespace Trax.Samples.PersistedOperations.E2E.Fixtures;

/// <summary>
/// Shared base for E2E tests. Provides an HTTP client wired to the in-process
/// API and a fresh <see cref="IPersistedOperationStore"/> reference per test.
/// </summary>
public abstract class ApiTestBase
{
    protected HttpClient Http { get; private set; } = null!;
    protected IPersistedOperationStore Store { get; private set; } = null!;

    [SetUp]
    public async Task BaseSetUpAsync()
    {
        // Each test starts with empty persisted-operation tables so id reuse
        // across cases does not trigger the shape-diff guardrail against
        // stale state.
        await SharedApiSetup.ClearAsync(SharedApiSetup.Factory.Services);

        // The suite acts as the operator: the management mutations it drives require the role
        // the demo key holds. ProductionPostureTests covers the anonymous caller.
        Http = SharedApiSetup.Factory.CreateClient();
        Http.DefaultRequestHeaders.Add("X-Api-Key", DemoKeys.OperatorKey);
        Store = SharedApiSetup.Factory.Services.GetRequiredService<IPersistedOperationStore>();
    }

    [TearDown]
    public void BaseTearDown() => Http?.Dispose();

    /// <summary>
    /// POST a GraphQL request body, return the raw response.
    /// </summary>
    protected async Task<HttpResponseMessage> PostAsync(object body) =>
        await Http.PostAsJsonAsync("/trax/graphql/", body);

    /// <summary>
    /// POST and parse the response body as JSON.
    /// </summary>
    protected async Task<JsonDocument> PostJsonAsync(object body)
    {
        var resp = await PostAsync(body);
        var text = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// The call a shipped app makes: an id and its variables, with no credential.
    /// </summary>
    protected static async Task<JsonDocument> AppCallsAsync(string id, string name)
    {
        using var app = SharedApiSetup.Factory.CreateClient();
        var resp = await app.PostAsJsonAsync(
            "/trax/graphql/",
            new { id, variables = GreetDocuments.Variables(name) }
        );
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    /// <summary>Uploads a document through the management mutation, as the operator.</summary>
    protected Task<JsonDocument> UploadRawAsync(
        string id,
        string document,
        bool bypassShapeDiff = false
    ) =>
        PostJsonAsync(
            new
            {
                query = """
                mutation Upload($input: UploadPersistedOperationInput!) {
                  operations {
                    persistedOperations {
                      uploadPersistedOperation(input: $input) {
                        success
                        errors { code message oldFingerprint newFingerprint }
                      }
                    }
                  }
                }
                """,
                variables = new
                {
                    input = new
                    {
                        id,
                        document,
                        bypassShapeDiff,
                    },
                },
            }
        );

    /// <summary>The <c>uploadPersistedOperation</c> payload of an upload response.</summary>
    protected static JsonElement UploadPayload(JsonDocument json) =>
        json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");

    /// <summary>Uploads a document and asserts the upload succeeded.</summary>
    protected async Task UploadAsync(string id, string document, bool bypassShapeDiff = false)
    {
        using var json = await UploadRawAsync(id, document, bypassShapeDiff);
        var payload = UploadPayload(json);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
    }
}
