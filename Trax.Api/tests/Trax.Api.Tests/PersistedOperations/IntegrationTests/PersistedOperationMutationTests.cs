using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Drives the management mutations end-to-end through HotChocolate. Verifies
/// that exception projection produces the documented payload codes and that
/// successful uploads materialise into rows reachable via the store.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PersistedOperationMutationTests
{
    private ServiceProvider _sp = null!;
    private IRequestExecutor _executor = null!;
    private IPersistedOperationStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        _sp = await GraphQLFixture.BuildAsync();
        _executor = await GraphQLFixture.GetExecutorAsync(_sp);
        _store = _sp.GetRequiredService<IPersistedOperationStore>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_sp is not null)
            await _sp.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp() => await PostgresFixture.ClearAsync();

    [Test]
    public async Task Upload_ValidDocument_ReturnsOperation_AndPersists()
    {
        var json = await ExecuteMutationAsync(
            "upload_v1",
            GraphQLFixture.ValidDocument,
            description: "first upload"
        );

        json.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        var row = await _store.GetAsync("upload_v1", null, CancellationToken.None);
        row.Should().NotBeNull();
        row!.Document.Should().Be(GraphQLFixture.ValidDocument);
        row.Description.Should().Be("first upload");
    }

    [Test]
    public async Task Upload_SyntaxError_ReturnsParseError_AndDoesNotPersist()
    {
        var json = await ExecuteMutationAsync("syntax_v1", GraphQLFixture.SyntaxErrorDocument);

        var errors = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("PARSE_FAILED");

        (await _store.GetAsync("syntax_v1", null, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task Upload_SchemaMismatch_ReturnsValidationError_AndDoesNotPersist()
    {
        var json = await ExecuteMutationAsync("schema_v1", GraphQLFixture.SchemaMismatchDocument);

        var errors = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("SCHEMA_VALIDATION_FAILED");
        errors[0].GetProperty("message").GetString().Should().NotBeNullOrEmpty();

        (await _store.GetAsync("schema_v1", null, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task Upload_ShapeDiffWithoutBypass_ReturnsShapeDiffError_WithFingerprints()
    {
        // Seed.
        await _store.UpsertAsync(
            "shape_v1",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );

        var json = await ExecuteMutationAsync("shape_v1", GraphQLFixture.ShapeChangingDocument);

        var errors = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("code").GetString().Should().Be("SHAPE_DIFF_VIOLATION");
        errors[0].GetProperty("oldFingerprint").GetString().Should().NotBeNullOrEmpty();
        errors[0].GetProperty("newFingerprint").GetString().Should().NotBeNullOrEmpty();

        // Row unchanged.
        var row = await _store.GetAsync("shape_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GraphQLFixture.ValidDocument);
    }

    [Test]
    public async Task Upload_ShapeDiffWithBypass_Succeeds()
    {
        await _store.UpsertAsync(
            "bypass_v1",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );

        var json = await ExecuteMutationAsync(
            "bypass_v1",
            GraphQLFixture.ShapeChangingDocument,
            bypassShapeDiff: true
        );

        json.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        var row = await _store.GetAsync("bypass_v1", null, CancellationToken.None);
        row!.Document.Should().Be(GraphQLFixture.ShapeChangingDocument);
    }

    [Test]
    public async Task Deactivate_ExistingId_MarksInactive()
    {
        await _store.UpsertAsync(
            "deact_v1",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );

        var json = await Execute(
            """
            mutation Deactivate($input: DeactivatePersistedOperationInput!) {
              operations {
                persistedOperations {
                  deactivatePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["id"] = "deact_v1",
                    ["reason"] = "rotating",
                },
            }
        );

        json.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("deactivatePersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        (await _store.GetAsync("deact_v1", null, CancellationToken.None))
            .Should()
            .BeNull("because GetAsync filters by IsActive");
    }

    [Test]
    public async Task Deactivate_UnknownId_ReturnsNotFound()
    {
        var json = await Execute(
            """
            mutation Deactivate($input: DeactivatePersistedOperationInput!) {
              operations {
                persistedOperations {
                  deactivatePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["id"] = "does_not_exist_v1",
                    ["reason"] = "anything",
                },
            }
        );

        var errors = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("deactivatePersistedOperation")
            .GetProperty("errors");
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Test]
    public async Task Restore_ReactivatesDeactivatedRow()
    {
        await _store.UpsertAsync(
            "restore_v1",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );
        await _store.DeactivateAsync("restore_v1", null, "test", CancellationToken.None);

        var json = await Execute(
            """
            mutation Restore($input: RestorePersistedOperationInput!) {
              operations {
                persistedOperations {
                  restorePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "restore_v1" },
            }
        );

        json.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("restorePersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue();

        var row = await _store.GetAsync("restore_v1", null, CancellationToken.None);
        row.Should().NotBeNull();
        row!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task Upload_EmptyId_ReturnsInvalidInput_AndDoesNotPersist()
    {
        var json = await ExecuteMutationAsync(
            id: string.Empty,
            document: GraphQLFixture.ValidDocument
        );

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
            .Be("INVALID_INPUT");
        payload.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("id");
    }

    [Test]
    public async Task Upload_EmptyDocument_ReturnsInvalidInput()
    {
        var json = await ExecuteMutationAsync(id: "ok_v1", document: string.Empty);

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
            .Be("INVALID_INPUT");
        payload
            .GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Contain("document");

        (await _store.GetAsync("ok_v1", null, CancellationToken.None)).Should().BeNull();
    }

    [Test]
    public async Task Deactivate_EmptyReason_ReturnsInvalidInput_AndKeepsRowActive()
    {
        // Reason is required for audit accountability; passing an empty
        // string must be rejected rather than silently writing "" to the
        // deprecation_reason column.
        await _store.UpsertAsync(
            "deact_noreason_v1",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );

        var json = await Execute(
            """
            mutation Deactivate($input: DeactivatePersistedOperationInput!) {
              operations {
                persistedOperations {
                  deactivatePersistedOperation(input: $input) {
                    success
                    errors { code message }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["id"] = "deact_noreason_v1",
                    ["reason"] = "  ",
                },
            }
        );

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("deactivatePersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("INVALID_INPUT");

        var row = await _store.GetAsync("deact_noreason_v1", null, CancellationToken.None);
        row.Should().NotBeNull("the rejected deactivate must not have run");
        row!.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task Restore_UnknownId_ReturnsNotFound()
    {
        var json = await Execute(
            """
            mutation Restore($input: RestorePersistedOperationInput!) {
              operations {
                persistedOperations {
                  restorePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "never_existed_v1" },
            }
        );

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("restorePersistedOperation");
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Test]
    public async Task Restore_EmptyId_ReturnsInvalidInput()
    {
        var json = await Execute(
            """
            mutation Restore($input: RestorePersistedOperationInput!) {
              operations {
                persistedOperations {
                  restorePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "" },
            }
        );

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("restorePersistedOperation");
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("INVALID_INPUT");
    }

    [Test]
    public async Task Deactivate_EmptyId_ReturnsInvalidInput()
    {
        var json = await Execute(
            """
            mutation Deactivate($input: DeactivatePersistedOperationInput!) {
              operations {
                persistedOperations {
                  deactivatePersistedOperation(input: $input) {
                    success
                    errors { code }
                  }
                }
              }
            }
            """,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "", ["reason"] = "x" },
            }
        );

        var payload = json
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("persistedOperations")
            .GetProperty("deactivatePersistedOperation");
        payload
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("INVALID_INPUT");
    }

    private Task<JsonDocument> ExecuteMutationAsync(
        string id,
        string document,
        string? description = null,
        bool bypassShapeDiff = false
    )
    {
        const string query = """
            mutation Upload($input: UploadPersistedOperationInput!) {
              operations {
                persistedOperations {
                  uploadPersistedOperation(input: $input) {
                    success
                    operation { id document shapeFingerprint isActive }
                    errors {
                      code
                      message
                      oldFingerprint
                      newFingerprint
                      locations { line column }
                    }
                  }
                }
              }
            }
            """;

        var input = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["document"] = document,
            ["bypassShapeDiff"] = bypassShapeDiff,
        };
        if (description is not null)
            input["description"] = description;

        return Execute(query, new Dictionary<string, object?> { ["input"] = input });
    }

    private async Task<JsonDocument> Execute(
        string query,
        IReadOnlyDictionary<string, object?> variables
    )
    {
        var request = OperationRequestBuilder
            .New()
            .SetDocument(query)
            .SetVariableValues(variables.ToDictionary(p => p.Key, p => p.Value))
            .Build();
        var result = await _executor.ExecuteAsync(request);
        var op = result as OperationResult;
        op.Should().NotBeNull("expected OperationResult");
        op!.Errors.Should().BeNullOrEmpty();
        return JsonDocument.Parse(op.ToJson());
    }
}
