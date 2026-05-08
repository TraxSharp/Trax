using System.Text.Json;
using FluentAssertions;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

[TestFixture]
[Category("E2E")]
public class EnforcementTests : ApiTestBase
{
    private const string GreetDoc =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

    [Test]
    public async Task InlineQuery_IsRejectedWith400()
    {
        var resp = await PostAsync(
            new { query = GreetDoc, variables = new { input = new { name = "Bob" } } }
        );

        ((int)resp.StatusCode).Should().Be(400);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task PersistedId_AfterUpload_DispatchesToTrainAndReturnsResult()
    {
        await Store.UpsertAsync("greet_v1", GreetDoc, options: null, CancellationToken.None);

        using var doc = await PostJsonAsync(
            new { id = "greet_v1", variables = new { input = new { name = "Alice" } } }
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        doc.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            .Should()
            .Be("Hello, Alice.");
    }

    [Test]
    public async Task PersistedId_NotInStore_ReturnsNotFoundError()
    {
        var resp = await PostAsync(
            new { id = "nonexistent_v1", variables = new { input = new { name = "X" } } }
        );
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("errors");
    }

    [Test]
    public async Task DevPrefixedOperation_BypassesEnforcement()
    {
        // The sample configures AllowOperationsMatching(id => id.StartsWith("dev_")).
        // An inline query with a dev_-prefixed operation name passes through
        // the middleware and HC executes it normally.
        var resp = await PostAsync(
            new
            {
                query = "query dev_explore($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }",
                operationName = "dev_explore",
                variables = new { input = new { name = "Dev" } },
            }
        );
        ((int)resp.StatusCode).Should().Be(200);
    }
}
