using System.Text.Json;
using FluentAssertions;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E.ApiTests;

/// <summary>
/// Drives the persisted-operations admin surface end-to-end through the
/// GraphQL mutations exposed by the server. Mirrors what the dashboard and
/// the (now mutation-only) sample client do.
/// </summary>
[TestFixture]
public class MutationFlowTests : ApiTestBase
{
    private const string GreetQuery =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting } } } }";

    private const string GreetWithGreetedAt =
        "query Greet($input: GreetInput!) { discover { greeting { greet(input: $input) { greeting greetedAt } } } }";

    [Test]
    public async Task Upload_ValidDocument_RegistersOperation_AndExecutableById()
    {
        await UploadAsync("mut_greet_v1", GreetQuery);

        var resp = await PostJsonAsync(
            new { id = "mut_greet_v1", variables = new { input = new { name = "Alice" } } }
        );

        resp.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("greeting")
            .GetProperty("greet")
            .GetProperty("greeting")
            .GetString()
            .Should()
            .Contain("Alice");
    }

    [Test]
    public async Task Upload_SchemaMismatch_ReturnsValidationError_AndRowNotPersisted()
    {
        var json = await UploadRawAsync("mut_bad_v1", "query Bad { nonexistentField }");

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        var errors = payload.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("SCHEMA_VALIDATION_FAILED");

        (await Store.GetAsync("mut_bad_v1", null, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task Upload_ShapeChange_WithoutBypass_ReturnsError_AndOriginalStillResolves()
    {
        await UploadAsync("mut_shape_v1", GreetQuery);

        var json = await UploadRawAsync("mut_shape_v1", GreetWithGreetedAt);
        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("SHAPE_DIFF_VIOLATION");

        var row = await Store.GetAsync("mut_shape_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GreetQuery);
    }

    [Test]
    public async Task Upload_ShapeChange_WithBypass_OverridesDocument()
    {
        await UploadAsync("mut_bypass_v1", GreetQuery);
        await UploadAsync("mut_bypass_v1", GreetWithGreetedAt, bypassShapeDiff: true);

        var row = await Store.GetAsync("mut_bypass_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GreetWithGreetedAt);
    }

    [Test]
    public async Task Deactivate_BlocksFutureExecutionsByThatId()
    {
        await UploadAsync("mut_deact_v1", GreetQuery);

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
            new { id = "mut_deact_v1", variables = new { input = new { name = "Alice" } } }
        );
        // Deactivated id no longer resolves; HC returns a top-level error.
        resp.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
    }

    private async Task UploadAsync(string id, string document, bool bypassShapeDiff = false)
    {
        var json = await UploadRawAsync(id, document, bypassShapeDiff);
        if (
            !json.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null
        )
            throw new InvalidOperationException(
                "Unexpected GraphQL response: " + json.RootElement.GetRawText()
            );
        var payload = data.GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeTrue(payload.GetRawText());
    }

    private Task<JsonDocument> UploadRawAsync(
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
}
