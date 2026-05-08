using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;

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
        if (SharedApiSetup.Skipped || SharedApiSetup.Factory is null)
            Assert.Ignore("Postgres / API factory not reachable. Run docker compose up -d.");

        // Each test starts with empty persisted-operation tables so id reuse
        // across cases does not trigger the shape-diff guardrail against
        // stale state.
        await SharedApiSetup.ClearAsync(SharedApiSetup.Factory.Services);

        Http = SharedApiSetup.Factory.CreateClient();
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
}
