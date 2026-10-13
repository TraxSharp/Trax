using AwesomeAssertions;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// Drives the persisted-operations admin surface end-to-end through the
/// GraphQL mutations exposed by the server. Mirrors what the dashboard and
/// the sample client do.
/// </summary>
[TestFixture]
public class MutationFlowTests : ApiTestBase
{
    [Test]
    public async Task Upload_ValidDocument_RegistersOperation_AndExecutableById()
    {
        await UploadAsync("mut_greet_v1", GreetDocuments.Casual);

        var resp = await PostJsonAsync(
            new { id = "mut_greet_v1", variables = GreetDocuments.Variables("Alice") }
        );

        GreetDocuments.GreetingOf(resp).Should().Be("Hey Alice!");
    }

    [Test]
    public async Task Upload_SchemaMismatch_ReturnsValidationError_AndRowNotPersisted()
    {
        var json = await UploadRawAsync("mut_bad_v1", "query Bad { nonexistentField }");

        var payload = UploadPayload(json);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        var errors = payload.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("SCHEMA_VALIDATION_FAILED");

        (await Store.GetAsync("mut_bad_v1", null, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task Upload_ShapeChange_WithoutBypass_ReturnsError_AndOriginalStillResolves()
    {
        await UploadAsync("mut_shape_v1", GreetDocuments.Casual);

        var json = await UploadRawAsync("mut_shape_v1", GreetDocuments.RenamedField);
        var payload = UploadPayload(json);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("SHAPE_DIFF_VIOLATION");

        var row = await Store.GetAsync("mut_shape_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GreetDocuments.Casual);
    }

    [Test]
    public async Task Upload_ShapeChange_WithBypass_OverridesDocument()
    {
        await UploadAsync("mut_bypass_v1", GreetDocuments.Casual);
        await UploadAsync("mut_bypass_v1", GreetDocuments.RenamedField, bypassShapeDiff: true);

        var row = await Store.GetAsync("mut_bypass_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GreetDocuments.RenamedField);
    }

    [Test]
    public async Task Deactivate_BlocksFutureExecutionsByThatId()
    {
        await UploadAsync("mut_deact_v1", GreetDocuments.Casual);

        var deact = await PostJsonAsync(
            new
            {
                query = """
                mutation Deactivate($input: DeactivatePersistedOperationInput!) {
                  operations {
                    persistedOperations {
                      deactivatePersistedOperation(input: $input) { success }
                    }
                  }
                }
                """,
                variables = new { input = new { id = "mut_deact_v1", reason = "rotating" } },
            }
        );
        deact
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("deactivatePersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        var resp = await PostJsonAsync(
            new { id = "mut_deact_v1", variables = GreetDocuments.Variables("Alice") }
        );
        // Deactivated id no longer resolves; HC returns a top-level error.
        GraphQLErrors.FirstCode(resp).Should().Be(GraphQLErrors.UnknownPersistedOperation);
    }
}
