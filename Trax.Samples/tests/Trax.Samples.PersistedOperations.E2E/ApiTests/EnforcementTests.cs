using AwesomeAssertions;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

[TestFixture]
[Category("E2E")]
public class EnforcementTests : ApiTestBase
{
    [Test]
    public async Task InlineQuery_IsRejectedWith400()
    {
        var resp = await PostAsync(
            new { query = GreetDocuments.Casual, variables = GreetDocuments.Variables("Bob") }
        );

        ((int)resp.StatusCode).Should().Be(400);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("PERSISTED_OPERATION_REQUIRED");
    }

    [Test]
    public async Task PersistedId_AfterUpload_DispatchesToTrainAndReturnsResult()
    {
        await Store.UpsertAsync(
            "greet_v1",
            GreetDocuments.Casual,
            options: null,
            CancellationToken.None
        );

        using var doc = await PostJsonAsync(
            new { id = "greet_v1", variables = GreetDocuments.Variables("Alice") }
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        GreetDocuments.GreetingOf(doc).Should().Be("Hey Alice!");
    }

    [Test]
    public async Task PersistedId_NotInStore_ReturnsNotFoundError()
    {
        var resp = await PostAsync(
            new { id = "nonexistent_v1", variables = GreetDocuments.Variables("X") }
        );
        var body = await resp.Content.ReadAsStringAsync();
        GraphQLErrors.FirstCode(body).Should().Be(GraphQLErrors.UnknownPersistedOperation, body);
    }
}
